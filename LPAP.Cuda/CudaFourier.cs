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

		public Task<IntPtr> PerformFftAsync(IntPtr indexPointer, bool keep = false)
			=> this.ExecuteTransformAsync(indexPointer, keep, cufftType.R2C, inverse: false, preferPlanReuse: false);

		public Task<IntPtr> PerformIfftAsync(IntPtr indexPointer, bool keep = false)
			=> this.ExecuteTransformAsync(indexPointer, keep, cufftType.C2R, inverse: true, preferPlanReuse: false);

		public Task<IntPtr> PerformFftManyAsync(IntPtr indexPointer, bool keep = false)
			=> this.ExecuteTransformAsync(indexPointer, keep, cufftType.R2C, inverse: false, preferPlanReuse: true);

		public Task<IntPtr> PerformIfftManyAsync(IntPtr indexPointer, bool keep = false)
			=> this.ExecuteTransformAsync(indexPointer, keep, cufftType.C2R, inverse: true, preferPlanReuse: true);

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

		private async Task<IntPtr> ExecuteTransformAsync(IntPtr indexPointer, bool keep, cufftType transformType, bool inverse, bool preferPlanReuse)
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