using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace LPAP.Demucs
{
	public sealed class DemucsModel : IDisposable
	{
		public InferenceSession Session { get; }

		public string InputName { get; }
		public string OutputName { get; }

		/// <summary>Fixed input length in frames (T) the model expects, e.g. 343980</summary>
		public int FixedInputFrames { get; }

		public int StemsWanted { get; }
		public int ChannelsWanted { get; }

		public DemucsModel(InferenceSession session, int stemsWanted = 4, int channelsWanted = 2, string? inputName = null, string? outputName = null)
		{
			Session = session ?? throw new ArgumentNullException(nameof(session));

			// Names autodetect if not supplied
			InputName = inputName ?? Session.InputMetadata.Keys.First();
			OutputName = outputName ?? Session.OutputMetadata.Keys.First();

			StemsWanted = stemsWanted;
			ChannelsWanted = channelsWanted;

			// Determine FixedInputFrames from input metadata (expect [1, C, T] or [B, C, T])
			var meta = Session.InputMetadata[InputName];
			if (meta.Dimensions == null || meta.Dimensions.Length < 3)
			{
				throw new InvalidOperationException($"DemucsModel: unexpected input dims for '{InputName}'.");
			}

			// Often dims: [1, 2, 343980]
			int t = -1;
			for (int i = meta.Dimensions.Length - 1; i >= 0; i--)
			{
				var d = meta.Dimensions[i];
				if (d > 0)
				{
					t = d;
					break;
				}
			}
			if (t <= 0)
			{
				throw new InvalidOperationException($"DemucsModel: cannot determine FixedInputFrames from input dims '{string.Join("x", meta.Dimensions)}'.");
			}

			FixedInputFrames = t;

			CudaLog.Info($"DemucsModel: Input='{InputName}', Output='{OutputName}', FixedInputFrames={FixedInputFrames}", "", "Demucs");
		}

		public void Dispose() => Session.Dispose();

		public Task<float[][]> SeparateAsync(
			ReadOnlyMemory<float> interleavedInput,
			int sampleRate,
			int channels,
			IProgress<float>? progress = null,
			CancellationToken ct = default)
		{
			// ORT Run is sync; keep your UI thread clean
			return Task.Run(() => SeparateSync(interleavedInput, sampleRate, channels, progress, ct), ct);
		}

		private float[][] SeparateSync(
			ReadOnlyMemory<float> interleavedInput,
			int sampleRate,
			int channels,
			IProgress<float>? progress,
			CancellationToken ct)
		{
			ct.ThrowIfCancellationRequested();

			if (channels <= 0)
			{
				throw new ArgumentOutOfRangeException(nameof(channels));
			}

			if (channels != ChannelsWanted)
			{
				// Du kannst das später erweitern (Downmix/Upmix). Für jetzt: hart.
				throw new InvalidOperationException($"DemucsModel: channels={channels} but model configured for ChannelsWanted={ChannelsWanted}.");
			}

			// 1) Build planar input [1, C, T]
			float[] inputPlanar = BuildPlanarInputFixedT(interleavedInput.Span, channels, FixedInputFrames, out float inputPeak);

			// 2) Run ONNX
			var inputTensor = new DenseTensor<float>(inputPlanar, new[] { 1, channels, FixedInputFrames });

			DisposableNamedOnnxValue in0 = (DisposableNamedOnnxValue) DisposableNamedOnnxValue.CreateFromTensor(this.InputName, inputTensor);
			try
			{
				List<NamedOnnxValue> inputs = [in0];

				using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results = this.Session.Run(inputs);

				DisposableNamedOnnxValue? outVal = results.FirstOrDefault(r => string.Equals(r.Name, this.OutputName, StringComparison.Ordinal));
				if (outVal == null)
				{
					outVal = results.First(); // fallback
				}

				Tensor<float> t = outVal.AsTensor<float>();
				int[] dims = GetDimsArray(t);

				// Debug raw output range
				(float mnRaw, float mxRaw) = ApproxMinMax(t);
				CudaLog.Info($"Demucs: Raw out name='{outVal.Name}', dims={string.Join("x", dims)}, len={t.Length}, min={mnRaw:0.0000}, max={mxRaw:0.0000}", "", "Demucs");

				// 3) Convert to planar waveform [S, C, T]
				float[] planar = ConvertToWaveformPlanar(t, dims, stemsWanted: this.StemsWanted, channelsWanted: channels, targetFrames: this.FixedInputFrames);

				// 4) Optional: DC removal (hilft gegen “über 0 schwebende” Wellen)
				RemoveDcOffsetInPlace(planar, this.StemsWanted, channels, this.FixedInputFrames);

				// 5) Split planar -> interleaved stems
				float[][] stems = new float[this.StemsWanted][];
				for (int s = 0; s < this.StemsWanted; s++)
				{
					stems[s] = PlanarStemToInterleaved(planar, s, this.StemsWanted, channels, this.FixedInputFrames);
					(float mn, float mx) = ApproxMinMax(stems[s]);
					CudaLog.Info($"Demucs: Stem[{s}] interleaved len={stems[s].Length}, min={mn:0.0000}, max={mx:0.0000}", "", "Demucs");
				}

				return stems;
			}
			finally
			{
				in0.Dispose();
			}
		}

		private static float[] ConvertToWaveformPlanar(Tensor<float> tf, int[] dims, int stemsWanted, int channelsWanted, int targetFrames)
		{
			// Accept common waveform outputs:
			// [S, C, T] or [1, S, C, T] or [S, T] (mono)
			if (dims.Length == 4 && dims[0] == 1)
			{
				// [1, S, C, T]
				int S = dims[1], C = dims[2], T = dims[3];
				if (T <= 0)
				{
					throw new InvalidOperationException("Demucs: invalid T.");
				}

				return Convert4DWaveformToPlanar(tf, S, C, T, stemsWanted, channelsWanted, targetFrames);
			}

			if (dims.Length == 3)
			{
				// [S, C, T]
				int S = dims[0], C = dims[1], T = dims[2];
				return Convert3DWaveformToPlanar(tf, S, C, T, stemsWanted, channelsWanted, targetFrames);
			}

			if (dims.Length == 5)
			{
				// Your case: [1, 6, 4, 2048, 336] => complex TF that needs ISTFT.
				int B = dims[0], Src = dims[1], ChLike = dims[2], FreqOrWin = dims[3], Frames = dims[4];

				CudaLog.Warn($"Demucs: Output is NOT waveform tensor. dims={string.Join("x", dims)}. Attempting 5D ISTFT reconstruction.", "Demucs");

				if (B != 1)
				{
					CudaLog.Warn($"Demucs: 5D decode expects batch=1, got {B}. Using batch 0 only.", "Demucs");
				}

				// Heuristic decision:
				// If ChLike==4 and FreqOrWin is power-of-two (2048), treat it as complex spectrum bins and do ISTFT.
				if (ChLike == 4 && IsPowerOfTwo(FreqOrWin))
				{
					return Convert5DComplexTfToWaveformPlanar_Istft(tf, Src, ChLike, nFft: FreqOrWin, nFrames: Frames, stemsWanted, channelsWanted, targetFrames);
				}

				// Fallback: if it really was framed time-domain, you can keep OLA. (Not your case.)
				return Convert5DToWaveformPlanar_OlaTimeDomain(tf, Src, ChLike, win: FreqOrWin, frames: Frames, stemsWanted, channelsWanted, targetFrames);
			}

			throw new InvalidOperationException($"Demucs: unsupported output dims={string.Join("x", dims)}.");
		}

		private static float[] Convert4DWaveformToPlanar(Tensor<float> tf, int S, int C, int T, int stemsWanted, int channelsWanted, int targetFrames)
		{
			int sCount = Math.Min(stemsWanted, S);
			int cCount = Math.Min(channelsWanted, C);

			var planar = new float[stemsWanted * channelsWanted * targetFrames];

			for (int s = 0; s < stemsWanted; s++)
			{
				int srcS = Math.Min(s, sCount - 1);
				for (int c = 0; c < channelsWanted; c++)
				{
					int srcC = Math.Min(c, cCount - 1);
					int dstBase = (s * channelsWanted + c) * targetFrames;

					int copy = Math.Min(targetFrames, T);
					for (int i = 0; i < copy; i++)
					{
						planar[dstBase + i] = tf[0, srcS, srcC, i];
					}
				}
			}

			return planar;
		}

		private static float[] Convert3DWaveformToPlanar(Tensor<float> tf, int S, int C, int T, int stemsWanted, int channelsWanted, int targetFrames)
		{
			int sCount = Math.Min(stemsWanted, S);
			int cCount = Math.Min(channelsWanted, C);

			var planar = new float[stemsWanted * channelsWanted * targetFrames];

			for (int s = 0; s < stemsWanted; s++)
			{
				int srcS = Math.Min(s, sCount - 1);
				for (int c = 0; c < channelsWanted; c++)
				{
					int srcC = Math.Min(c, cCount - 1);
					int dstBase = (s * channelsWanted + c) * targetFrames;

					int copy = Math.Min(targetFrames, T);
					for (int i = 0; i < copy; i++)
					{
						planar[dstBase + i] = tf[srcS, srcC, i];
					}
				}
			}

			return planar;
		}

		/// <summary>
		/// Correct decode for your tensor: dims [1, Src, 4, NFFT(2048), Frames(336)]
		/// where 4 = (L.re, L.im, R.re, R.im). Does IFFT per frame and OLA.
		/// </summary>
		private static float[] Convert5DComplexTfToWaveformPlanar_Istft(
			Tensor<float> tf,
			int src,
			int chLike,
			int nFft,
			int nFrames,
			int stemsWanted,
			int channelsWanted,
			int targetFrames)
		{
			if (channelsWanted != 2)
			{
				throw new InvalidOperationException("ISTFT decoder currently expects stereo (channelsWanted=2).");
			}

			int hop = nFft / 2; // common
			int olaLen = (nFrames - 1) * hop + nFft;

			var window = BuildHannWindow(nFft);

			// planar [S,C,T]
			var planar = new float[stemsWanted * channelsWanted * targetFrames];

			// temp buffers (reused to avoid GC)
			var spec = new Complex[nFft];
			var time = new Complex[nFft];

			for (int s = 0; s < stemsWanted; s++)
			{
				int srcS = Math.Min(s, src - 1);

				// For each channel separately
				for (int c = 0; c < 2; c++)
				{
					int reIdx = (c == 0) ? 0 : 2;
					int imIdx = (c == 0) ? 1 : 3;

					var ola = new float[olaLen];
					var norm = new float[olaLen];

					for (int f = 0; f < nFrames; f++)
					{
						int outOffset = f * hop;

						// Build complex spectrum for this frame
						for (int k = 0; k < nFft; k++)
						{
							float re = tf[0, srcS, reIdx, k, f];
							float im = tf[0, srcS, imIdx, k, f];
							spec[k] = new Complex(re, im);
						}

						// IFFT -> time
						Array.Copy(spec, time, nFft);
						Fft.InverseInPlace(time);

						// Overlap-add with window + normalization
						for (int n = 0; n < nFft; n++)
						{
							float w = window[n];

							// time[n].Real is the real waveform sample
							float v = (float) time[n].Real;

							int pos = outOffset + n;
							if ((uint) pos >= (uint) olaLen)
							{
								continue;
							}

							ola[pos] += v * w;
							norm[pos] += w * w;
						}
					}

					for (int i = 0; i < olaLen; i++)
					{
						float d = norm[i];
						if (d > 1e-12f)
						{
							ola[i] /= d;
						}
					}

					// Crop to targetFrames (center crop like before)
					int srcStart = 0;
					if (olaLen > targetFrames)
					{
						srcStart = (olaLen - targetFrames) / 2;
					}

					int dstBase = (s * channelsWanted + c) * targetFrames;
					int copy = Math.Min(targetFrames, olaLen - srcStart);
					Array.Copy(ola, srcStart, planar, dstBase, copy);
				}
			}

			(float mn, float mx) = ApproxMinMax(planar);
			CudaLog.Info($"Demucs: ISTFT planar len={planar.Length}, min={mn:0.000000}, max={mx:0.000000} (nFft={nFft}, hop={hop}, frames={nFrames}, olaLen={olaLen})", "", "Demucs");

			return planar;
		}

		// Fallback OLA if it really was time-domain frames (not your case, but safe)
		private static float[] Convert5DToWaveformPlanar_OlaTimeDomain(
			Tensor<float> tf,
			int src,
			int chLike,
			int win,
			int frames,
			int stemsWanted,
			int channelsWanted,
			int targetFrames)
		{
			int hop = win / 2;
			int olaLen = (frames - 1) * hop + win;
			var window = BuildHannWindow(win);

			int stemsToRead = Math.Min(stemsWanted, src);

			var planar = new float[stemsWanted * channelsWanted * targetFrames];

			for (int s = 0; s < stemsWanted; s++)
			{
				int srcS = Math.Min(s, stemsToRead - 1);

				for (int c = 0; c < channelsWanted; c++)
				{
					int srcC = Math.Min(c, chLike - 1);

					var ola = new float[olaLen];
					var norm = new float[olaLen];

					for (int f = 0; f < frames; f++)
					{
						int outOffset = f * hop;
						for (int n = 0; n < win; n++)
						{
							float w = window[n];
							float v = tf[0, srcS, srcC, n, f];

							int pos = outOffset + n;
							if ((uint) pos >= (uint) olaLen)
							{
								continue;
							}

							ola[pos] += v * w;
							norm[pos] += w * w;
						}
					}

					for (int i = 0; i < olaLen; i++)
					{
						float d = norm[i];
						if (d > 1e-12f)
						{
							ola[i] /= d;
						}
					}

					int srcStart = 0;
					if (olaLen > targetFrames)
					{
						srcStart = (olaLen - targetFrames) / 2;
					}

					int dstBase = (s * channelsWanted + c) * targetFrames;
					int copy = Math.Min(targetFrames, olaLen - srcStart);
					Array.Copy(ola, srcStart, planar, dstBase, copy);
				}
			}

			return planar;
		}

		private static float[] BuildPlanarInputFixedT(ReadOnlySpan<float> interleaved, int channels, int fixedT, out float inputPeak)
		{
			int framesIn = interleaved.Length / channels;
			if (framesIn <= 0)
			{
				throw new InvalidOperationException("No samples.");
			}

			inputPeak = 0f;
			int scanStep = Math.Max(1, interleaved.Length / 131072);
			for (int i = 0; i < interleaved.Length; i += scanStep)
			{
				float a = MathF.Abs(interleaved[i]);
				if (a > inputPeak)
				{
					inputPeak = a;
				}
			}
			if (inputPeak <= 0f)
			{
				inputPeak = 1f;
			}

			// If your floats are already [-1..1], inputPeak ~1.
			// If not, normalize down to [-1..1] (Demucs expects audio-like scale).
			float scale = (inputPeak > 1.2f) ? (1f / inputPeak) : 1f;

			var planar = new float[channels * fixedT];

			// Copy center (or pad) to fixedT
			int copyFrames = Math.Min(framesIn, fixedT);
			int srcStart = 0;

			if (framesIn > fixedT)
			{
				srcStart = (framesIn - fixedT) / 2;
			}

			for (int t = 0; t < copyFrames; t++)
			{
				int srcFrame = srcStart + t;
				int srcBase = srcFrame * channels;

				for (int c = 0; c < channels; c++)
				{
					planar[c * fixedT + t] = interleaved[srcBase + c] * scale;
				}
			}

			return planar;
		}

		private static float[] PlanarStemToInterleaved(float[] planar, int stemIndex, int stems, int channels, int frames)
		{
			var inter = new float[frames * channels];
			for (int t = 0; t < frames; t++)
			{
				for (int c = 0; c < channels; c++)
				{
					int src = ((stemIndex * channels + c) * frames) + t;
					inter[t * channels + c] = planar[src];
				}
			}
			return inter;
		}

		private static void RemoveDcOffsetInPlace(float[] planar, int stems, int channels, int frames)
		{
			for (int s = 0; s < stems; s++)
			{
				for (int c = 0; c < channels; c++)
				{
					int baseIdx = (s * channels + c) * frames;

					double sum = 0;
					for (int i = 0; i < frames; i++)
					{
						sum += planar[baseIdx + i];
					}

					float mean = (float) (sum / frames);

					// Only if meaningful offset
					if (MathF.Abs(mean) < 1e-6f)
					{
						continue;
					}

					for (int i = 0; i < frames; i++)
					{
						planar[baseIdx + i] -= mean;
					}
				}
			}
		}

		private static float[] BuildHannWindow(int n)
		{
			var w = new float[n];
			if (n <= 1) { if (n == 1) { w[0] = 1f; } return w; }

			for (int i = 0; i < n; i++)
			{
				w[i] = 0.5f - 0.5f * MathF.Cos(2f * MathF.PI * i / (n - 1));
			}

			// quick sanity log if needed:
			// (min,max,energy)
			return w;
		}

		private static int[] GetDimsArray(Tensor<float> tf)
		{
			var dims = tf.Dimensions; // usually ReadOnlySpan<int>
			var arr = new int[dims.Length];
			for (int i = 0; i < arr.Length; i++)
			{
				arr[i] = dims[i];
			}

			return arr;
		}

		private static (float min, float max) ApproxMinMax(Tensor<float> tf)
		{
			// Tensor<T> implements IEnumerable<T> but may allocate; do index scan via Buffer if possible:
			float min = float.MaxValue;
			float max = float.MinValue;

			// Safe generic enumeration (OK for logging)
			long step = Math.Max(1, tf.Length / 16384);
			int idx = 0;

			foreach (var v in tf)
			{
				if ((idx++ % step) != 0)
				{
					continue;
				}

				if (v < min)
				{
					min = v;
				}

				if (v > max)
				{
					max = v;
				}
			}

			if (min == float.MaxValue)
			{
				min = 0;
			}

			if (max == float.MinValue)
			{
				max = 0;
			}

			return (min, max);
		}

		private static (float min, float max) ApproxMinMax(float[] data)
		{
			float min = float.MaxValue, max = float.MinValue;
			int step = Math.Max(1, data.Length / 16384);
			for (int i = 0; i < data.Length; i += step)
			{
				float v = data[i];
				if (v < min)
				{
					min = v;
				}

				if (v > max)
				{
					max = v;
				}
			}
			if (min == float.MaxValue)
			{
				min = 0;
			}

			if (max == float.MinValue)
			{
				max = 0;
			}

			return (min, max);
		}

		private static bool IsPowerOfTwo(int n) => n > 0 && (n & (n - 1)) == 0;

		private static class Fft
		{
			// In-place inverse FFT for power-of-two sizes (radix-2 Cooley-Tukey)
			public static void InverseInPlace(Complex[] buffer)
			{
				int n = buffer.Length;
				if (n == 1)
				{
					return;
				}

				if ((n & (n - 1)) != 0)
				{
					throw new ArgumentException("FFT size must be power of two.");
				}

				BitReverse(buffer);

				for (int len = 2; len <= n; len <<= 1)
				{
					double ang = +2.0 * Math.PI / len; // inverse
					Complex wLen = new Complex(Math.Cos(ang), Math.Sin(ang));

					for (int i = 0; i < n; i += len)
					{
						Complex w = Complex.One;
						int half = len >> 1;

						for (int j = 0; j < half; j++)
						{
							Complex u = buffer[i + j];
							Complex v = buffer[i + j + half] * w;

							buffer[i + j] = u + v;
							buffer[i + j + half] = u - v;

							w *= wLen;
						}
					}
				}

				// Normalize
				double invN = 1.0 / n;
				for (int i = 0; i < n; i++)
				{
					buffer[i] *= invN;
				}
			}

			private static void BitReverse(Complex[] a)
			{
				int n = a.Length;
				int j = 0;
				for (int i = 1; i < n; i++)
				{
					int bit = n >> 1;
					while ((j & bit) != 0)
					{
						j ^= bit;
					}

					j ^= bit;

					if (i < j)
					{
						(a[i], a[j]) = (a[j], a[i]);
					}
				}
			}
		}
	}

	// Dummy logger placeholder – lass das weg, wenn du CudaLog schon hast.
	internal static class CudaLog
	{
		public static void Info(string msg, string? a = null, string? b = null) => Console.WriteLine(msg);
		public static void Warn(string msg, string? a = null) => Console.WriteLine("WARN " + msg);
	}
}
