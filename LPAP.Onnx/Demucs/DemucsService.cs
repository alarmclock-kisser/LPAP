using System;
using System.Threading;
using System.Threading.Tasks;

namespace LPAP.Demucs
{
	public sealed class DemucsService
	{
		private readonly DemucsModel _model;

		public DemucsService(DemucsModel model)
		{
			_model = model ?? throw new ArgumentNullException(nameof(model));
		}

		/// <summary>
		/// Full-track separation with overlap-stitching. Returns stems as interleaved float[].
		/// </summary>
		public async Task<float[][]> SeparateInterleavedAsync(
			ReadOnlyMemory<float> inputInterleaved,
			int sampleRate,
			int channels,
			IProgress<float>? progress = null,
			CancellationToken ct = default)
		{
			if (channels != 2) throw new InvalidOperationException("This implementation expects stereo input.");

			int totalFrames = inputInterleaved.Length / channels;
			int segT = _model.FixedInputFrames;

			// 50% overlap (good default)
			int overlap = segT / 2;
			int step = segT - overlap;
			if (step <= 0) step = segT;

			// Output buffers [stem][interleaved]
			int stems = _model.StemsWanted;
			var outStems = new float[stems][];
			for (int s = 0; s < stems; s++)
				outStems[s] = new float[totalFrames * channels];

			// Window for stitching in time domain (not the ISTFT window)
			var w = BuildHann(segT);

			int segCount = (totalFrames <= segT) ? 1 : (int) Math.Ceiling((totalFrames - segT) / (double) step) + 1;
			int segIndex = 0;

			for (int startFrame = 0; startFrame < totalFrames; startFrame += step)
			{
				ct.ThrowIfCancellationRequested();

				int framesThis = Math.Min(segT, totalFrames - startFrame);

				// Build segment interleaved (pad with zeros)
				var seg = new float[segT * channels];
				var src = inputInterleaved.Span;

				for (int t = 0; t < framesThis; t++)
				{
					int srcBase = (startFrame + t) * channels;
					int dstBase = t * channels;
					seg[dstBase + 0] = src[srcBase + 0];
					seg[dstBase + 1] = src[srcBase + 1];
				}

				CudaLog.Info($"Demucs: Start SeparateInterleaved (SR={sampleRate}, CH={channels}, FRAMES={segT})", "", "Demucs");

				// Run model on segment
				float[][] segStems = await _model.SeparateAsync(seg, sampleRate, channels, progress: null, ct).ConfigureAwait(false);

				// Stitch into output with window
				for (int s = 0; s < stems; s++)
				{
					var dstStem = outStems[s];
					var srcStem = segStems[s];

					for (int t = 0; t < framesThis; t++)
					{
						float ww = w[t];

						int dstBase = (startFrame + t) * channels;
						int srcBase2 = t * channels;

						dstStem[dstBase + 0] += srcStem[srcBase2 + 0] * ww;
						dstStem[dstBase + 1] += srcStem[srcBase2 + 1] * ww;
					}
				}

				segIndex++;
				progress?.Report(segIndex / (float) segCount);
			}

			return outStems;
		}

		private static float[] BuildHann(int n)
		{
			var w = new float[n];
			if (n <= 1) { if (n == 1) w[0] = 1f; return w; }
			for (int i = 0; i < n; i++)
				w[i] = 0.5f - 0.5f * MathF.Cos(2f * MathF.PI * i / (n - 1));
			return w;
		}
	}
}
