// CudaKernels.Core/AudioObj.Buffer.cs
using System;
using System.Drawing;

namespace LPAP.Audio
{
	public partial class AudioObj
	{
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
			this.OverlapSize = (int) (chunkSize * overlap);
			int step = chunkSize - this.OverlapSize;
			int numChunks = (this.Data.Length - chunkSize) / step + 1;

			// Prepare result array
			float[][] chunks = new float[numChunks][];

			await Task.Run(() =>
			{
				// Parallel processing with optimal worker count
				Parallel.For(0, numChunks, new ParallelOptions
				{
					MaxDegreeOfParallelism = maxWorkers
				}, i =>
				{
					int sourceOffset = i * step;
					float[] chunk = new float[chunkSize];
					Buffer.BlockCopy( // Faster than Array.Copy for float[]
						src: this.Data,
						srcOffset: sourceOffset * sizeof(float),
						dst: chunk,
						dstOffset: 0,
						count: chunkSize * sizeof(float));
					chunks[i] = chunk;
				});
			});

			// Cleanup if requested
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
						Interlocked.Exchange(ref outputAccumulator[idx], outputAccumulator[idx] + windowedSample);
						Interlocked.Exchange(ref weightSum[idx], weightSum[idx] + window[j]);
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

	}
}
