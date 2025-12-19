// CudaKernels.Core/AudioObj.Buffer.cs
using System;
using System.Drawing;

namespace LPAP.Audio
{
	public partial class AudioObj
	{
		public float SizeInMb => this.Data != null ? (this.Data.Length * sizeof(float)) / (1024.0f * 1024.0f) : 0.0f;

		public IntPtr Pointer { get; set; } = nint.Zero;
		public bool OnDevice => this.Pointer != nint.Zero;
		public bool OnHost => this.Data != null && this.Data.Length > 0;

		public string Form { get; set; } = "f";
		public int ChunkSize { get; set; } = 0;
		public float Overlap { get; set; } = 0.0f;

		public bool IsProcessing { get; set; } = false;



		public async Task<List<float[]>> GetChunksAsync(int chunkSize, float overlap = 0.5f, int maxWorkers = 0, bool keepData = false)
		{
			maxWorkers = maxWorkers <= 0
				? Environment.ProcessorCount
				: Math.Clamp(maxWorkers, 1, Environment.ProcessorCount);

			// Input Validation (sync part for fast fail)
			if (this.Data == null || this.Data.Length == 0)
			{
				return [];
			}

			if (chunkSize <= 0 || overlap < 0 || overlap >= 1)
			{
				return [];
			}

			// Calculate chunk metrics (sync)
			this.ChunkSize = chunkSize;
			this.Overlap = overlap;
			this.OverlapSize = (int) (chunkSize * overlap);

			int step = chunkSize - this.OverlapSize;
			step = Math.Max(1, step);

			// IMPORTANT: Ceil so tail is NOT dropped (last chunk is zero-padded)
			int numChunks = (int) Math.Ceiling((this.Data.Length - chunkSize) / (double) step) + 1;
			numChunks = Math.Max(numChunks, 1);

			// Prepare result array
			float[][] chunks = new float[numChunks][];

			await Task.Run(() =>
			{
				Parallel.For(0, numChunks, new ParallelOptions
				{
					MaxDegreeOfParallelism = maxWorkers
				}, i =>
				{
					int sourceOffset = i * step;
					int remaining = this.Data.Length - sourceOffset;

					float[] chunk = new float[chunkSize];
					if (remaining > 0)
					{
						int copyCount = Math.Min(chunkSize, remaining);
						Buffer.BlockCopy(this.Data, sourceOffset * sizeof(float), chunk, 0, copyCount * sizeof(float));
					}

					chunks[i] = chunk;
				});
			});

			if (!keepData)
			{
				this.Data = [];
			}

			return chunks.ToList();
		}

		public async Task AggregateStretchedChunksAsync(IEnumerable<float[]> chunks, int maxWorkers = 0, bool keepPointer = false)
		{
			if (maxWorkers <= 0)
			{
				maxWorkers = Environment.ProcessorCount;
			}
			maxWorkers = Math.Clamp(maxWorkers, 1, Environment.ProcessorCount);

			if (chunks == null || !chunks.Any())
			{
				return;
			}

			// Pointer
			this.Pointer = keepPointer ? this.Pointer : IntPtr.Zero;

			// Pre-calculate all values that don't change
			double stretchFactor = this.StretchFactor;
			int chunkSize = this.ChunkSize;
			int overlapSize = this.OverlapSize;
			int originalHopSize = chunkSize - overlapSize;
			int stretchedHopSize = (int) Math.Round(originalHopSize * stretchFactor);
			int outputLength = (chunks.Count() - 1) * stretchedHopSize + chunkSize;

			// Create window function (cosine window)
			double[] window = await Task.Run(() =>
				Enumerable.Range(0, chunkSize)
						  .Select(i => 0.5 * (1.0 - Math.Cos(2.0 * Math.PI * i / (chunkSize - 1))))
						  .ToArray()  // Korrekte Methode ohne Punkt
			).ConfigureAwait(false);

			// Initialize accumulators in parallel
			double[] outputAccumulator = new double[outputLength];
			double[] weightSum = new double[outputLength];

			await Task.Run(() =>
			{
				var parallelOptions = new ParallelOptions
				{
					MaxDegreeOfParallelism = maxWorkers
				};

				// Phase 1: Process chunks in parallel
				Parallel.For(0, chunks.LongCount(), parallelOptions, chunkIndex =>
				{
					var chunk = chunks.ElementAt((int) chunkIndex);
					int offset = (int) chunkIndex * stretchedHopSize;

					for (int j = 0; j < Math.Min(chunkSize, chunk.Length); j++)
					{
						int idx = offset + j;
						if (idx >= outputLength)
						{
							break;
						}

						double windowedSample = chunk[j] * window[j];

						// Using Interlocked for thread-safe accumulation
						AddAtomic(ref outputAccumulator[idx], windowedSample);
						AddAtomic(ref weightSum[idx], window[j]);
					}
				});

				// Phase 2: Normalize results
				float[] finalOutput = new float[outputLength];
				Parallel.For(0, outputLength, parallelOptions, i =>
				{
					finalOutput[i] = weightSum[i] > 1e-6
						? (float) (outputAccumulator[i] / weightSum[i])
						: 0.0f;
				});

				// Final assignment (thread-safe)
				this.Data = finalOutput;
			}).ConfigureAwait(true);
		}


		public async Task ResampleAsync(int targetSampleRate = 44100, int maxWorkers = 0)
		{
			maxWorkers = maxWorkers <= 0
				? Environment.ProcessorCount
				: Math.Clamp(maxWorkers, 1, Environment.ProcessorCount);

			if (this.Data == null || this.Data.Length == 0)
			{
				return;
			}

			if (targetSampleRate <= 0)
			{
				throw new ArgumentOutOfRangeException(nameof(targetSampleRate));
			}

			if (this.SampleRate <= 0 || this.Channels <= 0)
			{
				throw new InvalidOperationException("SampleRate/Channels must be set before resampling.");
			}

			if (this.SampleRate == targetSampleRate)
			{
				return;
			}

			await Task.Run(() =>
			{
				int ch = this.Channels;
				int inFrames = this.Data.Length / ch;
				if (inFrames <= 1)
				{
					return;
				}

				double srcRate = this.SampleRate;
				double dstRate = targetSampleRate;

				int outFrames = Math.Max(1, (int) Math.Round(inFrames * (dstRate / srcRate)));
				var src = this.Data;

				// Optional anti-alias when downsampling
				float[] work = src;
				if (dstRate < srcRate)
				{
					work = new float[src.Length];

					double cutoffHz = 0.45 * (dstRate * 0.5); // 0.45 * Nyquist(dst)
					if (cutoffHz < 10)
					{
						cutoffHz = 10;
					}

					var coeff = DesignLowpassBiquad((float) cutoffHz, (float) srcRate, q: 0.707f);

					Parallel.For(0, ch, new ParallelOptions { MaxDegreeOfParallelism = maxWorkers }, c =>
					{
						float x1 = 0, x2 = 0;
						float y1 = 0, y2 = 0;

						for (int i = 0; i < inFrames; i++)
						{
							float x0 = src[i * ch + c];
							float y0 = coeff.B0 * x0 + coeff.B1 * x1 + coeff.B2 * x2
								- coeff.A1 * y1 - coeff.A2 * y2;

							x2 = x1; x1 = x0;
							y2 = y1; y1 = y0;

							work[i * ch + c] = y0;
						}
					});
				}

				var dst = new float[outFrames * ch];

				double posStep = srcRate / dstRate;

				int block = 8192; // frames per block
				int blocks = (outFrames + block - 1) / block;

				Parallel.For(0, blocks, new ParallelOptions { MaxDegreeOfParallelism = maxWorkers }, b =>
				{
					int start = b * block;
					int end = Math.Min(outFrames, start + block);

					for (int of = start; of < end; of++)
					{
						double srcPos = of * posStep;
						int i1 = (int) srcPos;
						float t = (float) (srcPos - i1);

						int i0 = Math.Max(0, i1 - 1);
						int i2 = Math.Min(inFrames - 1, i1 + 1);
						int i3 = Math.Min(inFrames - 1, i1 + 2);
						i1 = Math.Min(inFrames - 1, i1);

						for (int c = 0; c < ch; c++)
						{
							float y0 = work[i0 * ch + c];
							float y1 = work[i1 * ch + c];
							float y2 = work[i2 * ch + c];
							float y3 = work[i3 * ch + c];

							dst[of * ch + c] = CatmullRom(y0, y1, y2, y3, t);
						}
					}
				});

				this.Data = dst;
				this.SampleRate = targetSampleRate;

			}).ConfigureAwait(false);

			// Update CustomTags ID3 SampleRate
			this.CustomTags["ID3v2.SampleRate"] = targetSampleRate.ToString();

		}

		public async Task TransformChannelsAsync(int targetChannels = 2, int maxWorkers = 0)
		{
			maxWorkers = maxWorkers <= 0
				? Environment.ProcessorCount
				: Math.Clamp(maxWorkers, 1, Environment.ProcessorCount);

			if (this.Data == null || this.Data.Length == 0)
			{
				return;
			}

			if (targetChannels <= 0)
			{
				throw new ArgumentOutOfRangeException(nameof(targetChannels));
			}

			if (this.Channels <= 0)
			{
				throw new InvalidOperationException("Channels must be set before channel transform.");
			}

			if (this.Channels == targetChannels)
			{
				return;
			}

			await Task.Run(() =>
			{
				int srcCh = this.Channels;
				int dstCh = targetChannels;
				var src = this.Data;

				int frames = src.Length / srcCh;
				if (frames <= 0)
				{
					return;
				}

				var dst = new float[frames * dstCh];

				int block = 16384;
				int blocks = (frames + block - 1) / block;

				Parallel.For(0, blocks, new ParallelOptions { MaxDegreeOfParallelism = maxWorkers }, b =>
				{
					int start = b * block;
					int end = Math.Min(frames, start + block);

					for (int f = start; f < end; f++)
					{
						int srcBase = f * srcCh;
						int dstBase = f * dstCh;

						if (dstCh == 1)
						{
							double sum = 0.0;
							for (int c = 0; c < srcCh; c++)
							{
								sum += src[srcBase + c];
							}

							dst[dstBase] = (float) (sum / srcCh);
							continue;
						}

						if (srcCh == 1)
						{
							float v = src[srcBase];
							for (int c = 0; c < dstCh; c++)
							{
								dst[dstBase + c] = v;
							}

							continue;
						}

						if (dstCh == 2)
						{
							float l = src[srcBase + 0];
							float r = src[srcBase + 1];

							if (srcCh > 2)
							{
								double otherSum = 0.0;
								for (int c = 2; c < srcCh; c++)
								{
									otherSum += src[srcBase + c];
								}

								float otherAvg = (float) (otherSum / srcCh);

								l += otherAvg;
								r += otherAvg;
							}

							dst[dstBase + 0] = l * 0.5f;
							dst[dstBase + 1] = r * 0.5f;
							continue;
						}

						int copy = Math.Min(srcCh, dstCh);
						for (int c = 0; c < copy; c++)
						{
							dst[dstBase + c] = src[srcBase + c];
						}

						if (dstCh > srcCh)
						{
							double sum = 0.0;
							for (int c = 0; c < srcCh; c++)
							{
								sum += src[srcBase + c];
							}

							float avg = (float) (sum / srcCh);

							for (int c = srcCh; c < dstCh; c++)
							{
								dst[dstBase + c] = avg;
							}
						}
					}
				});

				this.Data = dst;
				this.Channels = dstCh;

			}).ConfigureAwait(false);

			// Update CustomTags ID3 Channels
			this.CustomTags["ID3v2.Channels"] = targetChannels.ToString();

		}




		private readonly struct BiquadCoeffs
		{
			public readonly float B0, B1, B2, A1, A2;
			public BiquadCoeffs(float b0, float b1, float b2, float a1, float a2)
			{
				this.B0 = b0; this.B1 = b1; this.B2 = b2; this.A1 = a1; this.A2 = a2;
			}
		}

		private static BiquadCoeffs DesignLowpassBiquad(float cutoffHz, float sampleRate, float q = 0.707f)
		{
			// RBJ cookbook lowpass
			float w0 = 2f * (float) Math.PI * cutoffHz / sampleRate;
			float cosw0 = (float) Math.Cos(w0);
			float sinw0 = (float) Math.Sin(w0);
			float alpha = sinw0 / (2f * q);

			float b0 = (1f - cosw0) * 0.5f;
			float b1 = 1f - cosw0;
			float b2 = (1f - cosw0) * 0.5f;
			float a0 = 1f + alpha;
			float a1 = -2f * cosw0;
			float a2 = 1f - alpha;

			// normalize a0 to 1
			b0 /= a0; b1 /= a0; b2 /= a0; a1 /= a0; a2 /= a0;

			return new BiquadCoeffs(b0, b1, b2, a1, a2);
		}

		private static float CatmullRom(float y0, float y1, float y2, float y3, float t)
		{
			float t2 = t * t;
			float t3 = t2 * t;

			return 0.5f * (
				(2f * y1) +
				(-y0 + y2) * t +
				(2f * y0 - 5f * y1 + 4f * y2 - y3) * t2 +
				(-y0 + 3f * y1 - 3f * y2 + y3) * t3
			);
		}

		private static void AddAtomic(ref double location, double value)
		{
			double current, next;
			do
			{
				current = location;
				next = current + value;
			} while (Interlocked.CompareExchange(ref location, next, current) != current);
		}

	}
}
