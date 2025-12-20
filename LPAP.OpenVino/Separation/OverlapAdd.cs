namespace LPAP.OpenVino.Separation
{
	internal static class OverlapAdd
	{
		/// <summary>
		/// Crossfade overlap-add for channel-major arrays [C][T].
		/// </summary>
		public static float[][] Stitch(
			int channels,
			int totalFrames,
			IReadOnlyList<Chunking.Chunk> chunks,
			IReadOnlyList<float[][]> chunkAudio, // per chunk: [C][len]
			int chunkFrames,
			double overlapFraction)
		{
			overlapFraction = Math.Clamp(overlapFraction, 0.0, 0.95);
			int overlap = (int) Math.Round(chunkFrames * overlapFraction);

			var outC = new float[channels][];
			for (int c = 0; c < channels; c++)
			{
				outC[c] = new float[totalFrames];
			}

			// Weight buffer for perfect normalization (sum of fade weights)
			var w = new float[totalFrames];

			for (int i = 0; i < chunks.Count; i++)
			{
				var ch = chunks[i];
				var buf = chunkAudio[i];
				int len = ch.LengthFrames;

				for (int t = 0; t < len; t++)
				{
					int dst = ch.StartFrame + t;
					float wt = 1f;

					// fade-in on first overlap region of each chunk (except first chunk)
					if (overlap > 0 && i > 0 && t < overlap)
					{
						wt = (float) t / overlap;
					}

					// fade-out on last overlap region (except last chunk)
					if (overlap > 0 && i < chunks.Count - 1 && t >= len - overlap)
					{
						float wt2 = (float) (len - 1 - t) / overlap;
						wt = MathF.Min(wt, wt2);
					}

					for (int c = 0; c < channels; c++)
					{
						outC[c][dst] += buf[c][t] * wt;
					}

					w[dst] += wt;
				}
			}

			// normalize
			for (int t = 0; t < totalFrames; t++)
			{
				float wt = w[t];
				if (wt <= 1e-8f)
				{
					continue;
				}

				float inv = 1f / wt;
				for (int c = 0; c < channels; c++)
				{
					outC[c][t] *= inv;
				}
			}

			return outC;
		}
	}
}
