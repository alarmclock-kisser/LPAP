using System;
using System.Threading;
using System.Threading.Tasks;

namespace LPAP.Audio.Processing
{
	public static partial class AudioProcessor
	{
		public static async Task NormalizeAsync(
			AudioObj audio,
			float amplitude = 0.85f,
			int? maxWorkers = null,
			CancellationToken ct = default)
		{
			if (audio == null)
			{
				throw new ArgumentNullException(nameof(audio));
			}

			if (audio.Data == null || audio.Data.Length == 0)
			{
				return;
			}

			maxWorkers ??= Environment.ProcessorCount - 1;
			maxWorkers = Math.Clamp(maxWorkers.Value, 1, Environment.ProcessorCount);

			await Task.Run(() =>
			{
				AudioScheduling.DemoteCurrentThreadForBackgroundWork();

				var data = audio.Data;
				int len = data.Length;

				// 1) Peak finden
				float maxAbs = 0f;

				if (maxWorkers == 1)
				{
					for (int i = 0; i < len; i++)
					{
						ct.ThrowIfCancellationRequested();
						float v = MathF.Abs(data[i]);
						if (v > maxAbs)
						{
							maxAbs = v;
						}
					}
				}
				else
				{
					object lockObj = new();
					int chunkSize = len / maxWorkers.Value;

					Parallel.For(0, maxWorkers.Value, new ParallelOptions
					{
						MaxDegreeOfParallelism = maxWorkers.Value
					},
					worker =>
					{
						int start = worker * chunkSize;
						int end = (worker == maxWorkers.Value - 1) ? len : start + chunkSize;

						float localMax = 0f;
						for (int i = start; i < end; i++)
						{
							if (ct.IsCancellationRequested)
							{
								return;
							}

							float v = MathF.Abs(data[i]);
							if (v > localMax)
							{
								localMax = v;
							}
						}

						lock (lockObj)
						{
							if (localMax > maxAbs)
							{
								maxAbs = localMax;
							}
						}
					});
				}

				if (maxAbs <= 0f)
				{
					return;
				}

				float target = amplitude;
				float factor = target / maxAbs;

				// 2) Skalieren
				if (maxWorkers == 1)
				{
					for (int i = 0; i < len; i++)
					{
						ct.ThrowIfCancellationRequested();
						data[i] *= factor;
					}
				}
				else
				{
					Parallel.For(0, maxWorkers.Value, new ParallelOptions
					{
						MaxDegreeOfParallelism = maxWorkers.Value
					},
					worker =>
					{
						int chunkSize = len / maxWorkers.Value;
						int start = worker * chunkSize;
						int end = (worker == maxWorkers.Value - 1) ? len : start + chunkSize;

						for (int i = start; i < end; i++)
						{
							if (ct.IsCancellationRequested)
							{
								return;
							}

							data[i] *= factor;
						}
					});
				}

				audio.DataChanged();

			}, ct).ConfigureAwait(false);
		}
	}
}
