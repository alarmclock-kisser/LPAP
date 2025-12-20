using LPAP.OpenVino.Util;

namespace LPAP.OpenVino.Separation
{
	internal static class AudioTensorConverter
	{
		/// <summary>
		/// Convert interleaved audio (L,R,L,R...) to channel-major [C][T].
		/// </summary>
		public static float[][] Deinterleave(float[] interleaved, int channels)
		{
			Guard.True(channels > 0, "channels must be > 0");
			Guard.NotNull(interleaved, nameof(interleaved));
			Guard.True(interleaved.Length % channels == 0, "Interleaved length must be divisible by channels.");

			int frames = interleaved.Length / channels;
			var ch = new float[channels][];
			for (int c = 0; c < channels; c++)
			{
				ch[c] = new float[frames];
			}

			int k = 0;
			for (int t = 0; t < frames; t++)
			{
				for (int c = 0; c < channels; c++)
				{
					ch[c][t] = interleaved[k++];
				}
			}
			return ch;
		}

		/// <summary>
		/// Convert channel-major [C][T] to interleaved float[].
		/// </summary>
		public static float[] Interleave(float[][] channelMajor)
		{
			Guard.NotNull(channelMajor, nameof(channelMajor));
			Guard.True(channelMajor.Length > 0, "No channels.");
			int channels = channelMajor.Length;
			int frames = channelMajor[0].Length;
			for (int c = 1; c < channels; c++)
			{
				Guard.True(channelMajor[c].Length == frames, "Channel lengths must match.");
			}

			var interleaved = new float[channels * frames];
			int k = 0;
			for (int t = 0; t < frames; t++)
			{
				for (int c = 0; c < channels; c++)
				{
					interleaved[k++] = channelMajor[c][t];
				}
			}
			return interleaved;
		}

		/// <summary>
		/// Slice channel-major audio [C][T] into [C][len] starting at offset.
		/// Pads with zeros if beyond end.
		/// </summary>
		public static float[][] SlicePad(float[][] channelMajor, int startFrame, int lengthFrames)
		{
			int channels = channelMajor.Length;
			int total = channelMajor[0].Length;

			var outCh = new float[channels][];
			for (int c = 0; c < channels; c++)
			{
				outCh[c] = new float[lengthFrames];
			}

			for (int t = 0; t < lengthFrames; t++)
			{
				int src = startFrame + t;
				if (src < 0 || src >= total)
				{
					continue;
				}

				for (int c = 0; c < channels; c++)
				{
					outCh[c][t] = channelMajor[c][src];
				}
			}

			return outCh;
		}

		/// <summary>
		/// Flatten [B][C][T] into contiguous array matching NCT layout.
		/// </summary>
		public static float[] FlattenNct(IReadOnlyList<float[][]> batch, int batchSize, int channels, int frames)
		{
			var flat = new float[batchSize * channels * frames];
			int o = 0;
			for (int b = 0; b < batchSize; b++)
			{
				var item = batch[b];
				for (int c = 0; c < channels; c++)
				{
					var src = item[c];
					Buffer.BlockCopy(src, 0, flat, o * sizeof(float), frames * sizeof(float));
					o += frames;
				}
			}
			return flat;
		}

		public static float PeakAbs(float[] data)
		{
			float p = 0f;
			for (int i = 0; i < data.Length; i++)
			{
				float a = MathF.Abs(data[i]);
				if (a > p)
				{
					p = a;
				}
			}
			return p;
		}

		public static void ClampInPlace(float[] data, float min = -1f, float max = 1f)
		{
			for (int i = 0; i < data.Length; i++)
			{
				float v = data[i];
				if (v < min)
				{
					v = min;
				}
				else if (v > max)
				{
					v = max;
				}

				data[i] = v;
			}
		}
	}
}
