using ManagedCuda;
using ManagedCuda.BasicTypes;
using ManagedCuda.CudaFFT;
using ManagedCuda.VectorTypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace LPAP.Cuda
{
    internal sealed class CudaFourier : IDisposable
    {
        private readonly PrimaryContext _ctx;
        private readonly CudaRegister _register;

        internal CudaFourier(PrimaryContext ctx, CudaRegister register)
        {
            this._ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
            this._register = register ?? throw new ArgumentNullException(nameof(register));
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
        }

        public IntPtr PerformFft(IntPtr indexPointer, bool keep = false)
            => this.ExecuteTransform(indexPointer, keep, cufftType.R2C, inverse: false);

        public IntPtr PerformIfft(IntPtr indexPointer, bool keep = false)
            => this.ExecuteTransform(indexPointer, keep, cufftType.C2R, inverse: true);

        public Task<IntPtr> PerformFftAsync(IntPtr indexPointer, bool keep = false, IProgress<double>? progress = null)
            => this.ExecuteTransformAsync(indexPointer, keep, cufftType.R2C, inverse: false, preferPlanReuse: false, progress: progress);

        public Task<IntPtr> PerformIfftAsync(IntPtr indexPointer, bool keep = false, IProgress<double>? progress = null)
            => this.ExecuteTransformAsync(indexPointer, keep, cufftType.C2R, inverse: true, preferPlanReuse: false, progress: progress);

		public Task<IntPtr> PerformFftManyAsync(IntPtr indexPointer, bool keep = false, IProgress<double>? progress = null)
			=> this.ExecuteTransformAsync(indexPointer, keep, cufftType.R2C, inverse: false, preferPlanReuse: true, progress: progress);

		public Task<IntPtr> PerformIfftManyAsync(IntPtr indexPointer, bool keep = false, IProgress<double>? progress = null)
			=> this.ExecuteTransformAsync(indexPointer, keep, cufftType.C2R, inverse: true, preferPlanReuse: true, progress: progress);


		private IntPtr ExecuteTransform(IntPtr indexPointer, bool keep, cufftType transformType, bool inverse)
        {
            var mem = this._register[indexPointer];
            if (!this.ValidateInput(mem, inverse))
            {
                return IntPtr.Zero;
            }

            var outputMem = inverse
                ? this._register.AllocateGroup<float>(mem!.Lengths)
                : this._register.AllocateGroup<float2>(mem!.Lengths);

            if (outputMem == null)
            {
                return indexPointer;
            }

            Dictionary<int, CudaFFTPlan1D>? plans = null;
            try
            {
                plans = [];
                for (int i = 0; i < mem.Count; i++)
                {
                    int length = (int) mem.Lengths[i].ToInt64();
                    if (!plans.TryGetValue(length, out var plan))
                    {
                        plan = new CudaFFTPlan1D(length, transformType, 1);
                        plans[length] = plan;
                    }

                    plan.Exec(new CUdeviceptr(mem.Pointers[i]), new CUdeviceptr(outputMem.Pointers[i]));
                }
            }
            catch (Exception ex)
            {
                CudaLog.Error("FFT execution failed", ex.Message);
                this._register.FreeMemory(outputMem);
                return indexPointer;
            }
            finally
            {
                if (plans != null)
                {
                    foreach (var plan in plans.Values)
                    {
                        plan.Dispose();
                    }
                }
            }

            if (!keep)
            {
                this._register.FreeMemory(indexPointer);
            }

            return outputMem.IndexPointer;
        }

        private async Task<IntPtr> ExecuteTransformAsyncOld(IntPtr indexPointer, bool keep, cufftType transformType, bool inverse, bool preferPlanReuse, IProgress<double>? progress = null)
        {
            var mem = this._register[indexPointer];
            if (!this.ValidateInput(mem, inverse))
            {
                return IntPtr.Zero;
            }

            var outputMem = inverse
                ? await this._register.AllocateGroupAsync<float>(mem!.Lengths).ConfigureAwait(false)
                : await this._register.AllocateGroupAsync<float2>(mem!.Lengths).ConfigureAwait(false);

            if (outputMem == null)
            {
                return indexPointer;
            }

            var stream = this._register.GetStream();
            if (stream == null)
            {
                this._register.FreeMemory(outputMem);
                return indexPointer;
            }

            Dictionary<int, CudaFFTPlan1D>? cachedPlans = preferPlanReuse ? new() : null;
            try
            {
                progress?.Report(0.0);
				for (int i = 0; i < mem!.Count; i++)
                {
                    int length = (int) mem.Lengths[i].ToInt64();
                    CudaFFTPlan1D plan;
                    if (cachedPlans != null)
                    {
                        if (!cachedPlans.TryGetValue(length, out plan!))
                        {
                            plan = new CudaFFTPlan1D(length, transformType, 1, stream.Stream);
                            cachedPlans[length] = plan;
                        }
                    }
                    else
                    {
                        plan = new CudaFFTPlan1D(length, transformType, 1, stream.Stream);
                    }

                    plan.Exec(new CUdeviceptr(mem.Pointers[i]), new CUdeviceptr(outputMem.Pointers[i]));

                    if (cachedPlans == null)
                    {
                        plan.Dispose();
                    }
                    progress?.Report((i + 1) / (double)mem.Count);
				}

                await Task.Run(stream.Synchronize).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                CudaLog.Error("Async FFT execution failed", ex.Message);
                this._register.FreeMemory(outputMem);
                return indexPointer;
            }
            finally
            {
                if (cachedPlans != null)
                {
                    foreach (var plan in cachedPlans.Values)
                    {
                        plan.Dispose();
                    }
                }
            }

            if (!keep)
            {
                this._register.FreeMemory(indexPointer);
            }

            return outputMem.IndexPointer;
        }

		private async Task<IntPtr> ExecuteTransformAsync(
	IntPtr indexPointer,
	bool keep,
	cufftType transformType,
	bool inverse,
	bool preferPlanReuse,
	IProgress<double>? progress = null)
		{
			var mem = this._register[indexPointer];
			if (!this.ValidateInput(mem, inverse))
			{
				return IntPtr.Zero;
			}

			var outputMem = inverse
				? await this._register.AllocateGroupAsync<float>(mem!.Lengths).ConfigureAwait(false)
				: await this._register.AllocateGroupAsync<float2>(mem!.Lengths).ConfigureAwait(false);

			if (outputMem == null)
			{
				return indexPointer;
			}

			var stream = this._register.GetStream();
			if (stream == null)
			{
				this._register.FreeMemory(outputMem);
				return indexPointer;
			}

			// local helpers
			static long ElemSizeBytes(Type t) => t == typeof(float) ? sizeof(float)
										: t == typeof(float2) ? sizeof(float) * 2
										: throw new NotSupportedException("Unsupported element type: " + t.Name);

			static bool IsContiguousBatch(IntPtr[] ptrs, int start, int count, long strideBytes)
			{
				// checks ptrs[start + k] == ptrs[start] + k*strideBytes
				long baseAddr = ptrs[start].ToInt64();
				for (int k = 1; k < count; k++)
				{
					long expected = baseAddr + (k * strideBytes);
					long actual = ptrs[start + k].ToInt64();
					if (actual != expected)
					{
						return false;
					}
				}
				return true;
			}

			// We either do true batched exec (asMany) per length (if contiguous) OR fallback per-chunk.
			// We'll drive progress on "chunks done" basis regardless of path.
			int done = 0;
			int total = mem!.Count;

			// Plan cache (key includes length + batch) because batch changes the plan.
			Dictionary<(int length, int batch), CudaFFTPlan1D>? cachedPlans =
				preferPlanReuse ? new Dictionary<(int, int), CudaFFTPlan1D>() : null;

			try
			{
				progress?.Report(0.0);

				// Group indices by length (because batch requires uniform length)
				// Preserve original order within each length group (important if pointers are contiguous)
				var groups = new Dictionary<int, List<int>>();
				for (int i = 0; i < mem.Count; i++)
				{
					int length = (int) mem.Lengths[i].ToInt64();
					if (!groups.TryGetValue(length, out var list))
					{
						list = new List<int>();
						groups[length] = list;
					}
					list.Add(i);
				}

				long elemBytes = ElemSizeBytes(mem.ElementType);

				foreach (var kv in groups)
				{
					int length = kv.Key;
					List<int> idxs = kv.Value;

					// If preferPlanReuse, attempt "real batch" if:
					// - indices are consecutive (i, i+1, i+2...) AND
					// - pointers are contiguous with stride = length * elemBytes
					// Same for output pointers.
					bool didBatch = false;

					if (preferPlanReuse && idxs.Count > 1)
					{
						bool consecutive = true;
						for (int k = 1; k < idxs.Count; k++)
						{
							if (idxs[k] != idxs[k - 1] + 1)
							{
								consecutive = false;
								break;
							}
						}

						if (consecutive)
						{
							int start = idxs[0];
							int batch = idxs.Count;

							long strideBytes = length * elemBytes;

							// input pointers are in mem.Pointers[] (IntPtr[])
							// output pointers are in outputMem.Pointers[] (IntPtr[])
							bool inContig = IsContiguousBatch(mem.Pointers, start, batch, strideBytes);
							bool outContig = IsContiguousBatch(outputMem.Pointers, start, batch, strideBytes);

							if (inContig && outContig)
							{
								// Create/reuse a batched plan (this is effectively cufftPlan1d with batch)
								CudaFFTPlan1D plan;
								if (cachedPlans != null)
								{
									if (!cachedPlans.TryGetValue((length, batch), out plan!))
									{
										plan = new CudaFFTPlan1D(length, transformType, batch, stream.Stream);
										cachedPlans[(length, batch)] = plan;
									}
								}
								else
								{
									plan = new CudaFFTPlan1D(length, transformType, batch, stream.Stream);
								}

								// One exec processes batch contiguous signals
								plan.Exec(new CUdeviceptr(mem.Pointers[start]), new CUdeviceptr(outputMem.Pointers[start]));

								if (cachedPlans == null)
								{
									plan.Dispose();
								}

								done += batch;
								progress?.Report(done / (double) total);
								didBatch = true;
							}
						}
					}

					if (didBatch)
					{
						continue;
					}

					// Fallback path: per-chunk exec, but still reuse plan by (length, batch=1) if preferPlanReuse
					CudaFFTPlan1D? plan1 = null;
					if (cachedPlans != null)
					{
						if (!cachedPlans.TryGetValue((length, 1), out plan1!))
						{
							plan1 = new CudaFFTPlan1D(length, transformType, 1, stream.Stream);
							cachedPlans[(length, 1)] = plan1;
						}
					}

					for (int k = 0; k < idxs.Count; k++)
					{
						int i = idxs[k];

						CudaFFTPlan1D plan = plan1 ?? new CudaFFTPlan1D(length, transformType, 1, stream.Stream);
						plan.Exec(new CUdeviceptr(mem.Pointers[i]), new CUdeviceptr(outputMem.Pointers[i]));

						if (plan1 == null)
						{
							plan.Dispose();
						}

						done++;
						progress?.Report(done / (double) total);
					}
				}

				await Task.Run(stream.Synchronize).ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				CudaLog.Error("Async FFT execution failed", ex.Message);
				this._register.FreeMemory(outputMem);
				return indexPointer;
			}
			finally
			{
				if (cachedPlans != null)
				{
					foreach (var plan in cachedPlans.Values)
					{
						plan.Dispose();
					}
				}
			}

			if (!keep)
			{
				this._register.FreeMemory(indexPointer);
			}

			progress?.Report(1.0);
			return outputMem.IndexPointer;
		}


		private bool ValidateInput(CudaMem? mem, bool inverse)
        {
            if (mem == null || mem.IndexPointer == IntPtr.Zero || mem.Count == 0)
            {
                CudaLog.Warn("CudaFourier input memory invalid");
                return false;
            }

            var expectedType = inverse ? typeof(float2) : typeof(float);
            if (mem.ElementType != expectedType)
            {
                CudaLog.Warn("CudaFourier unexpected element type", $"Expected {expectedType.Name}, got {mem.ElementType.Name}");
                return false;
            }

            return true;
        }
    }
}