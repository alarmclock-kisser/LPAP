using MathNet.Numerics;
using MathNet.Numerics.IntegralTransforms;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace LPAP.Audio.Processing
{
	public static partial class AudioProcessor
	{
		// PPFFT (legacy)
		public static async Task<AudioObj> TimeStretchParallelAsync_V1(
			AudioObj obj,
			int chunkSize = 16384,
			float overlap = 0.5f,
			double factor = 1.000,
			bool keepData = false,
			float normalize = 1.0f,
			int? maxWorkers = null,
			IProgress<double>? progress = null)
		{
			if (maxWorkers == null)
			{
				maxWorkers = Environment.ProcessorCount;
			}
			else
			{
				maxWorkers = Math.Clamp(maxWorkers.Value, 1, Environment.ProcessorCount);
			}

			float[] backupData = obj.Data;
			int sampleRate = obj.SampleRate;
			int overlapSize = obj.OverlapSize;

			double totalMs = 0;
			var sw = Stopwatch.StartNew();

			var chunkEnumerable = await obj.GetChunksAsync(chunkSize, overlap, keepData, maxWorkers.Value);
			var chunks = chunkEnumerable as IList<float[]> ?? chunkEnumerable.ToList();
			if (chunks.Count == 0)
			{
				obj.Data = backupData;
				return obj;
			}

			var tracker = CreateTracker(progress, chunks.Count, normalize > 0);
			tracker?.ReportWork(chunks.Count);
			// obj["chunk"] = sw.Elapsed.TotalMilliseconds;
			totalMs += sw.Elapsed.TotalMilliseconds;
			sw.Restart();

			var fftTasks = chunks.Select(chunk => FourierTransformForwardAsync(chunk, tracker));
			var fftChunks = await Task.WhenAll(fftTasks);
			if (fftChunks.Length == 0)
			{
				obj.Data = backupData;
				return obj;
			}
			// obj["fft"] = sw.Elapsed.TotalMilliseconds;
			totalMs += sw.Elapsed.TotalMilliseconds;
			sw.Restart();

			var stretchTasks = fftChunks.Select(transformedChunk =>
				StretchChunkAsync(transformedChunk, chunkSize, overlapSize, sampleRate, factor, tracker));
			var stretchChunks = await Task.WhenAll(stretchTasks);
			if (stretchChunks.Length == 0)
			{
				obj.Data = backupData;
				return obj;
			}
			// obj["stretch"] = sw.Elapsed.TotalMilliseconds;
			totalMs += sw.Elapsed.TotalMilliseconds;
			sw.Restart();

			obj.StretchFactor = factor;

			var ifftTasks = stretchChunks.Select(stretchChunk => FourierTransformInverseAsync(stretchChunk, tracker));
			var ifftChunks = await Task.WhenAll(ifftTasks);
			if (ifftChunks.Length == 0)
			{
				obj.Data = backupData;
				return obj;
			}
			// obj["ifft"] = (float) sw.Elapsed.TotalMilliseconds;
			totalMs += sw.Elapsed.TotalMilliseconds;
			sw.Restart();

			await obj.AggregateStretchedChunksAsync(ifftChunks, obj.StretchFactor, maxWorkers.Value);
			tracker?.ReportWork(chunks.Count);
			if (obj.Data.LongLength <= 0)
			{
				obj.Data = backupData;
				return obj;
			}
			// obj["aggregate"] = sw.Elapsed.TotalMilliseconds;
			totalMs += sw.Elapsed.TotalMilliseconds;

			obj.BeatsPerMinute = (float) (obj.BeatsPerMinute / factor);

			sw.Restart();

			if (normalize > 0)
			{
				await obj.NormalizeAsync(normalize, maxWorkers.Value);
				tracker?.ReportWork(chunks.Count);
			}

			// obj["normalize"] = sw.Elapsed.TotalMilliseconds;
			totalMs += sw.Elapsed.TotalMilliseconds;
			sw.Restart();

			tracker?.Complete();

			return obj;
		}

		// WSOLA (Waveform Similarity Overlap-Add)
		// Time-domain stretcher, transient-friendly.
		// Alignment uses normalized correlation on the overlap region.
		// - corrMode:
		//   Mono    : fastest, ok for narrow mixes
		//   Stereo  : correlate L+R separately (more stable on wide material)
		//   MidSide : correlate Mid + Side (often most stable); sideWeight controls width influence
		//
		// Tuning params (NO hidden magic numbers):
		// - searchRadiusMul : searchRadius = Ha * searchRadiusMul (bigger => more robust, more CPU)
		// - corrStride      : correlation subsampling (1 best, 2 balanced, 4 fast)
		public static async Task<AudioObj> TimeStretchParallelAsync_V2(
			AudioObj obj,
			int chunkSize = 16384,
			float overlap = 0.5f,
			double factor = 1.000,
			bool keepData = false,
			float normalize = 1.0f,
			int? maxWorkers = null,
			IProgress<double>? progress = null,
			StereoCorrelationMode corrMode = StereoCorrelationMode.MidSide,
			float sideWeight = 0.75f,
			float searchRadiusMul = 1.0f,
			int corrStride = 2)
		{
			return await TimeStretchWsolaCoreAsync(
				obj: obj,
				chunkSize: chunkSize,
				overlap: overlap,
				factor: factor,
				keepData: keepData,
				normalize: normalize,
				maxWorkers: maxWorkers,
				progress: progress,
				corrMode: corrMode,
				sideWeight: sideWeight,
				searchRadiusMul: searchRadiusMul,
				corrStride: corrStride
			).ConfigureAwait(false);
		}

		// WSOLA preset: FAST
		// - smaller window, lower overlap, smaller search radius, higher corrStride
		// - best for previews / weak CPUs
		public static Task<AudioObj> TimeStretchParallelAsync_V2_Fast(
			AudioObj obj,
			double factor = 1.0,
			bool keepData = false,
			float normalize = 1.0f,
			int? maxWorkers = null,
			IProgress<double>? progress = null)
		{
			return TimeStretchParallelAsync_V2(
				obj: obj,
				chunkSize: 8192,
				overlap: 0.35f,
				factor: factor,
				keepData: keepData,
				normalize: normalize,
				maxWorkers: maxWorkers,
				progress: progress,
				corrMode: StereoCorrelationMode.Mono,
				sideWeight: 0.0f,
				searchRadiusMul: 0.75f,
				corrStride: 4);
		}

		// WSOLA preset: BALANCED (recommended default)
		// - good stability/CPU tradeoff
		public static Task<AudioObj> TimeStretchParallelAsync_V2_Balanced(
			AudioObj obj,
			double factor = 1.0,
			bool keepData = false,
			float normalize = 1.0f,
			int? maxWorkers = null,
			IProgress<double>? progress = null,
			StereoCorrelationMode corrMode = StereoCorrelationMode.MidSide)
		{
			return TimeStretchParallelAsync_V2(
				obj: obj,
				chunkSize: 16384,
				overlap: 0.50f,
				factor: factor,
				keepData: keepData,
				normalize: normalize,
				maxWorkers: maxWorkers,
				progress: progress,
				corrMode: corrMode,
				sideWeight: 0.60f,
				searchRadiusMul: 1.0f,
				corrStride: 2);
		}

		// WSOLA preset: BEST
		// - larger window, high overlap, larger search radius, full correlation
		// - best for wide mixes / strong stereo FX; highest CPU cost
		public static Task<AudioObj> TimeStretchParallelAsync_V2_Best(
			AudioObj obj,
			double factor = 1.0,
			bool keepData = false,
			float normalize = 1.0f,
			int? maxWorkers = null,
			IProgress<double>? progress = null)
		{
			return TimeStretchParallelAsync_V2(
				obj: obj,
				chunkSize: 32768,
				overlap: 0.75f,
				factor: factor,
				keepData: keepData,
				normalize: normalize,
				maxWorkers: maxWorkers,
				progress: progress,
				corrMode: StereoCorrelationMode.MidSide,
				sideWeight: 0.75f,
				searchRadiusMul: 1.5f,
				corrStride: 1);
		}

		// PVSTFT
		public static async Task<AudioObj> TimeStretchParallelAsync_V3(
			AudioObj obj,
			int chunkSize = 16384,
			float overlap = 0.75f,
			double factor = 1.000,
			bool keepData = false,
			float normalize = 1.0f,
			int? maxWorkers = null,
			IProgress<double>? progress = null)
		{
			if (obj == null)
			{
				throw new ArgumentNullException(nameof(obj));
			}

			if (obj.Data == null || obj.Data.Length == 0)
			{
				return obj;
			}

			if (obj.SampleRate <= 0 || obj.Channels <= 0)
			{
				return obj;
			}

			int workers = maxWorkers.HasValue
				? Math.Clamp(maxWorkers.Value, 1, Environment.ProcessorCount)
				: Math.Max(1, Environment.ProcessorCount - 1);

			overlap = Math.Clamp(overlap, 0.10f, 0.95f);
			factor = Math.Clamp(factor, 0.05, 20.0);

			var src = obj.Data;
			var backup = src;

			if (!keepData)
			{
				obj.Data = Array.Empty<float>();
			}

			int ch = obj.Channels;
			int inFrames = src.Length / ch;
			if (inFrames <= 0)
			{
				obj.Data = backup;
				return obj;
			}

			// Convert chunkSize (interleaved samples) -> frames, and make FFT size a power of two.
			int n = Math.Max(512, chunkSize / ch);
			n = NextPow2(n);

			int ov = (int) Math.Round(n * overlap);
			ov = Math.Clamp(ov, 32, n - 1);

			int Ha = n - ov;
			int Hs = Math.Max(1, (int) Math.Round(Ha * factor));

			float[] window = Hann(n);

			// Split channels
			float[][] chSrc = new float[ch][];
			for (int c = 0; c < ch; c++)
			{
				chSrc[c] = DeinterleaveChannel(src, inFrames, ch, c);
			}

			float[][] chDst = new float[ch][];

			await Task.Run(() =>
			{
				// Parallelize per channel (best perf/quality tradeoff)
				Parallel.For(0, ch, new ParallelOptions { MaxDegreeOfParallelism = Math.Min(workers, ch) }, c =>
				{
					chDst[c] = PhaseVocoderChannel(chSrc[c], n, Ha, Hs, window);
				});
			}).ConfigureAwait(false);

			// Re-interleave (output frames = max of channels)
			int outFrames = 0;
			for (int c = 0; c < ch; c++)
			{
				outFrames = Math.Max(outFrames, chDst[c].Length);
			}

			var dstInterleaved = new float[outFrames * ch];
			for (int f = 0; f < outFrames; f++)
			{
				int baseIdx = f * ch;
				for (int c = 0; c < ch; c++)
				{
					float v = (f < chDst[c].Length) ? chDst[c][f] : 0f;
					dstInterleaved[baseIdx + c] = v;
				}
			}

			obj.Data = dstInterleaved;
			obj.StretchFactor = factor;
			obj.OverlapSize = ov * ch;
			obj.DataChanged();

			if (obj.BeatsPerMinute > 0.0 && factor > 0.0)
			{
				obj.BeatsPerMinute = obj.BeatsPerMinute / factor;
			}

			if (normalize > 0f)
			{
				await obj.NormalizeAsync(normalize).ConfigureAwait(false);
			}

			progress?.Report(1.0);
			return obj;
		}



		// Fourier Transform
		private static async Task<Complex[]> FourierTransformForwardAsync(float[] samples, ProgressTracker? tracker = null)
		{
			// FFT using nuget (samples.Length is guaranteed 2^n)
			return await Task.Run(() =>
			{
				var complexSamples = samples.Select(s => new Complex(s, 0)).ToArray();
				Fourier.Forward(complexSamples, FourierOptions.Matlab);
				tracker?.ReportWork(1);
				return complexSamples;
			});
		}

		private static async Task<float[]> FourierTransformInverseAsync(Complex[] samples, ProgressTracker? tracker = null)
		{
			// IFFT using nuget (samples.Length is guaranteed 2^n)
			return await Task.Run(() =>
			{
				Fourier.Inverse(samples, FourierOptions.Matlab);
				tracker?.ReportWork(1);
				return samples.Select(c => (float) c.Real).ToArray();
			});
		}




		// PPFFT helpers
		private static async Task<Complex[]> StretchChunkAsync(Complex[] samples, int chunkSize, int overlapSize, int sampleRate, double factor, ProgressTracker? tracker = null)
		{
			int hopIn = chunkSize - overlapSize;
			int hopOut = (int) (hopIn * factor + 0.5);

			int totalBins = chunkSize;
			int totalChunks = samples.Length / chunkSize;

			var output = new Complex[samples.Length];

			await Task.Run(() =>
			{
				for (int chunk = 0; chunk < totalChunks; chunk++)
				{
					for (int bin = 0; bin < totalBins; bin++)
					{
						int idx = chunk * chunkSize + bin;
						int prevIdx = (chunk > 0) ? (chunk - 1) * chunkSize + bin : idx;

						if (bin >= totalBins || chunk == 0)
						{
							output[idx] = samples[idx];
							continue;
						}

						Complex cur = samples[idx];
						Complex prev = samples[prevIdx];

						float phaseCur = (float) Math.Atan2(cur.Imaginary, cur.Real);
						float phasePrev = (float) Math.Atan2(prev.Imaginary, prev.Real);
						float mag = (float) Math.Sqrt(cur.Real * cur.Real + cur.Imaginary * cur.Imaginary);

						float deltaPhase = phaseCur - phasePrev;
						float freqPerBin = (float) sampleRate / chunkSize;
						float expectedPhaseAdv = 2.0f * (float) Math.PI * freqPerBin * bin * hopIn / sampleRate;

						float delta = deltaPhase - expectedPhaseAdv;
						delta = (float) (delta + Math.PI) % (2.0f * (float) Math.PI) - (float) Math.PI;

						float phaseOut = phasePrev + expectedPhaseAdv + (float) (delta * factor);

						output[idx] = new Complex(mag * Math.Cos(phaseOut), mag * Math.Sin(phaseOut));
					}
				}
			});

			tracker?.ReportWork(1);

			return output;
		}


		// WSOLA helpers
		private static async Task<AudioObj> TimeStretchWsolaCoreAsync(
			AudioObj obj,
			int chunkSize,
			float overlap,
			double factor,
			bool keepData,
			float normalize,
			int? maxWorkers,
			IProgress<double>? progress,
			StereoCorrelationMode corrMode,
			float sideWeight,
			float searchRadiusMul,
			int corrStride)
		{
			if (obj == null)
			{
				throw new ArgumentNullException(nameof(obj));
			}

			if (obj.Data == null || obj.Data.Length == 0)
			{
				return obj;
			}

			if (obj.SampleRate <= 0 || obj.Channels <= 0)
			{
				return obj;
			}

			// Leave 1 core for UI/playback by default.
			int workers = maxWorkers.HasValue
				? Math.Clamp(maxWorkers.Value, 1, Environment.ProcessorCount)
				: Math.Max(1, Environment.ProcessorCount - 1);

			overlap = Math.Clamp(overlap, 0.05f, 0.95f);
			factor = Math.Clamp(factor, 0.05, 20.0);

			searchRadiusMul = Math.Clamp(searchRadiusMul, 0.25f, 4.0f);
			corrStride = Math.Clamp(corrStride, 1, 16);
			sideWeight = Math.Clamp(sideWeight, 0.0f, 2.0f);

			var srcInterleaved = obj.Data;
			var backupData = srcInterleaved;

			// Free memory early if desired (keep snapshot in local var)
			if (!keepData)
			{
				obj.Data = Array.Empty<float>();
			}

			int ch = obj.Channels;
			int inFrames = srcInterleaved.Length / ch;
			if (inFrames <= 0)
			{
				obj.Data = backupData;
				return obj;
			}

			// If not stereo, force Mono correlation
			if (ch != 2)
			{
				corrMode = StereoCorrelationMode.Mono;
				sideWeight = 0.0f;
			}

			// chunkSize is "samples (interleaved)" in your codebase; convert to frames.
			int frameLen = Math.Max(256, chunkSize / ch);
			frameLen = Math.Max(frameLen, 512);

			int ov = (int) Math.Round(frameLen * overlap);
			ov = Math.Clamp(ov, 16, frameLen - 1);

			int Ha = frameLen - ov;                            // analysis hop
			int Hs = Math.Max(1, (int) Math.Round(Ha * factor)); // synthesis hop

			// Search radius around predicted input position
			int searchRadius = (int) Math.Round(Ha * searchRadiusMul);
			searchRadius = Math.Clamp(searchRadius, 64, frameLen);

			// Precompute crossfade (sqrt-Hann feels nice)
			float[] fadeIn = new float[ov];
			float[] fadeOut = new float[ov];
			for (int i = 0; i < ov; i++)
			{
				float w = 0.5f - 0.5f * (float) Math.Cos(2.0 * Math.PI * i / Math.Max(1, ov - 1));
				float fi = (float) Math.Sqrt(w);
				float fo = (float) Math.Sqrt(1.0f - w);
				fadeIn[i] = fi;
				fadeOut[i] = fo;
			}

			// Output size estimate (plus a bit for safety)
			int outFramesEst = Math.Max(frameLen + 1, (int) Math.Round(inFrames * factor) + frameLen + 8);
			float[] dst = new float[outFramesEst * ch];

			// Copy first frame directly
			CopyFramesInterleaved(srcInterleaved, 0, dst, 0, Math.Min(frameLen, inFrames), ch);

			int inPos = 0;
			int outPos = 0;

			double lastReported = 0.0;
			void Report(double v)
			{
				if (progress == null)
				{
					return;
				}

				v = Math.Clamp(v, 0.0, 1.0);
				if (v - lastReported >= 0.002 || v >= 1.0)
				{
					lastReported = v;
					progress.Report(v);
				}
			}

			await Task.Run(() =>
			{
				// expected number of steps ~ outFrames / Hs
				int approxSteps = Math.Max(1, (outFramesEst - frameLen) / Math.Max(1, Hs));
				int stepIndex = 0;

				var po = new ParallelOptions { MaxDegreeOfParallelism = workers };

				while (true)
				{
					int nextOut = outPos + Hs;
					if (nextOut + frameLen >= outFramesEst)
					{
						break;
					}

					// Predicted next input position
					int predIn = inPos + Ha;
					if (predIn + frameLen >= inFrames)
					{
						break;
					}

					int searchStart = Math.Max(0, predIn - searchRadius);
					int searchEnd = Math.Min(inFrames - frameLen - 1, predIn + searchRadius);
					if (searchEnd < searchStart)
					{
						break;
					}

					// Reference overlap taken from already-written output [nextOut .. nextOut+ov)
					var refBuf = new CorrLocalBuffers(ov, ch, corrMode)
					{
						SideWeight = sideWeight,
						Stride = corrStride
					};
					refBuf.FillFromOutput(dst, nextOut);

					int bestIn = predIn;
					double bestScore = double.NegativeInfinity;
					object gate = new object();

					Parallel.For(searchStart, searchEnd + 1, po,
						() =>
						{
							var local = new CorrLocalBuffers(ov, ch, corrMode)
							{
								SideWeight = sideWeight,
								Stride = corrStride,
								BestScore = double.NegativeInfinity,
								BestIn = predIn
							};
							local.CopyReferenceFrom(refBuf);
							return local;
						},
						(candIn, _, local) =>
						{
							local.FillFromSource(srcInterleaved, candIn);

							double score = local.Score();

							if (score > local.BestScore)
							{
								local.BestScore = score;
								local.BestIn = candIn;
							}
							return local;
						},
						local =>
						{
							lock (gate)
							{
								if (local.BestScore > bestScore)
								{
									bestScore = local.BestScore;
									bestIn = local.BestIn;
								}
							}
						});

					// Overlap-add (crossfade)
					for (int f = 0; f < frameLen; f++)
					{
						int dstFrame = nextOut + f;
						int srcFrame = bestIn + f;

						if (dstFrame >= outFramesEst)
						{
							break;
						}

						if (srcFrame >= inFrames)
						{
							break;
						}

						int dstBase = dstFrame * ch;
						int srcBase = srcFrame * ch;

						if (f < ov)
						{
							float fo = fadeOut[f];
							float fi = fadeIn[f];
							for (int c = 0; c < ch; c++)
							{
								dst[dstBase + c] = dst[dstBase + c] * fo + srcInterleaved[srcBase + c] * fi;
							}
						}
						else
						{
							for (int c = 0; c < ch; c++)
							{
								dst[dstBase + c] = srcInterleaved[srcBase + c];
							}
						}
					}

					inPos = bestIn;
					outPos = nextOut;

					stepIndex++;
					Report(stepIndex / (double) approxSteps);
				}

				// Trim to actually written length
				int finalFrames = Math.Min(outFramesEst, outPos + frameLen);
				var trimmed = new float[finalFrames * ch];
				Array.Copy(dst, 0, trimmed, 0, trimmed.Length);
				dst = trimmed;

			}).ConfigureAwait(false);

			// Assign back
			obj.Data = dst;
			obj.StretchFactor = factor;
			obj.OverlapSize = ov * ch;
			obj.DataChanged();

			// Optional BPM adjustment: stretching longer => BPM goes down
			if (obj.BeatsPerMinute > 0.0 && factor > 0.0)
			{
				obj.BeatsPerMinute = obj.BeatsPerMinute / factor;
			}

			if (normalize > 0f)
			{
				// Keep your solution's NormalizeAsync signature.
				// (You previously had both variants in your repo history.)
				await obj.NormalizeAsync(normalize).ConfigureAwait(false);
			}

			progress?.Report(1.0);
			return obj;
		}

		private static void CopyFramesInterleaved(float[] src, int srcFrame, float[] dst, int dstFrame, int frames, int ch)
		{
			int srcIdx = srcFrame * ch;
			int dstIdx = dstFrame * ch;
			int count = Math.Max(0, frames * ch);
			if (count <= 0)
			{
				return;
			}

			Array.Copy(
				src, srcIdx,
				dst, dstIdx,
				Math.Min(count, Math.Min(src.Length - srcIdx, dst.Length - dstIdx)));
		}



		// PVSTFT helpers
		private static float[] PhaseVocoderChannel(float[] src, int n, int Ha, int Hs, float[] window)
		{
			int inLen = src.Length;
			if (inLen < n)
			{
				return (float[]) src.Clone();
			}

			int frames = 1 + (int) Math.Ceiling((inLen - n) / (double) Ha);
			int outLen = (frames - 1) * Hs + n;

			// Weighted OLA like your aggregator does (accumulator + weightSum)
			double[] acc = new double[outLen];
			double[] wsum = new double[outLen];

			var buf = new Complex[n];
			var spec = new Complex[n];

			double[] prevPhase = new double[n];
			double[] sumPhase = new double[n];

			double twoPi = 2.0 * Math.PI;

			for (int t = 0; t < frames; t++)
			{
				int inPos = t * Ha;

				// analysis window -> complex buffer
				for (int i = 0; i < n; i++)
				{
					int si = inPos + i;
					double x = (si < inLen) ? src[si] : 0.0;
					buf[i] = new Complex(x * window[i], 0.0);
				}

				Fourier.Forward(buf, FourierOptions.Matlab);

				// phase propagation
				for (int k = 0; k < n; k++)
				{
					double re = buf[k].Real;
					double im = buf[k].Imaginary;
					double mag = Math.Sqrt(re * re + im * im);
					double phase = Math.Atan2(im, re);

					double omega = twoPi * k / n;          // radians per sample
					double expected = omega * Ha;

					double dphi = phase - prevPhase[k] - expected;
					dphi = WrapPi(dphi);

					// true frequency increment per analysis hop:
					double trueInc = expected + dphi;

					sumPhase[k] += trueInc * (Hs / (double) Ha);
					prevPhase[k] = phase;

					spec[k] = Complex.FromPolarCoordinates(mag, sumPhase[k]);
				}

				// synth
				Array.Copy(spec, buf, n);
				Fourier.Inverse(buf, FourierOptions.Matlab);

				int outPos = t * Hs;
				for (int i = 0; i < n; i++)
				{
					int oi = outPos + i;
					if (oi >= outLen)
					{
						break;
					}

					// Matlab option in MathNet already handles scaling to match Matlab conventions.
					double y = buf[i].Real * window[i];

					acc[oi] += y;
					wsum[oi] += window[i];
				}
			}

			var dst = new float[outLen];
			for (int i = 0; i < outLen; i++)
			{
				double w = wsum[i];
				dst[i] = (w > 1e-9) ? (float) (acc[i] / w) : 0f;
			}

			return dst;
		}

		private static float[] DeinterleaveChannel(float[] interleaved, int frames, int ch, int channelIndex)
		{
			var dst = new float[frames];
			for (int f = 0; f < frames; f++)
			{
				dst[f] = interleaved[f * ch + channelIndex];
			}

			return dst;
		}

		private static int NextPow2(int x)
		{
			x = Math.Max(1, x);
			x--;
			x |= x >> 1;
			x |= x >> 2;
			x |= x >> 4;
			x |= x >> 8;
			x |= x >> 16;
			return x + 1;
		}

		private static float[] Hann(int n)
		{
			var w = new float[n];
			for (int i = 0; i < n; i++)
			{
				w[i] = (float) (0.5 - 0.5 * Math.Cos(2.0 * Math.PI * i / Math.Max(1, n - 1)));
			}

			return w;
		}

		private static double WrapPi(double x)
		{
			// wrap to [-pi, pi]
			x = (x + Math.PI) % (2.0 * Math.PI);
			if (x < 0)
			{
				x += 2.0 * Math.PI;
			}

			return x - Math.PI;
		}





		// ProgressTracker
		private static ProgressTracker? CreateTracker(IProgress<double>? progress, int chunkCount, bool includeNormalize)
		{
			if (progress == null)
			{
				return null;
			}

			int safeChunkCount = Math.Max(1, chunkCount);
			int stageCount = 5; // chunking, FFT, stretch, IFFT, aggregate
			if (includeNormalize)
			{
				stageCount++;
			}

			double totalWork = safeChunkCount * stageCount;
			return new ProgressTracker(progress, totalWork);
		}

		private sealed class ProgressTracker
		{
			private readonly Lock gate = new();
			private readonly IProgress<double> progress;
			private readonly double totalWork;
			private double completed;

			internal ProgressTracker(IProgress<double> progress, double totalWork)
			{
				this.progress = progress;
				this.totalWork = Math.Max(1.0, totalWork);
			}

			internal void ReportWork(double workUnits)
			{
				if (workUnits <= 0)
				{
					return;
				}

				double normalized;
				lock (this.gate)
				{
					this.completed += workUnits;
					normalized = Math.Clamp(this.completed / this.totalWork, 0.0, 1.0);
				}

				this.progress.Report(normalized);
			}

			internal void Complete()
			{
				this.progress.Report(1.0);
			}
		}



		// Reflection Methods
		public static Dictionary<MethodInfo, string> GetTimeStretchMethods_DisplayMap()
		{
			// Finds public static methods on AudioProcessor with names like:
			// TimeStretchParallelAsync_V2
			// TimeStretchParallelAsync_V2_Best
			// TimeStretchParallelAsync_V3
			//
			// Returns: Dictionary<MethodInfo, "V2 (Best)"> etc.

			var result = new Dictionary<MethodInfo, string>();

			// Adjust if your class name / type differs (e.g. partial in another namespace).
			var type = typeof(AudioProcessor);

			const BindingFlags flags = BindingFlags.Public | BindingFlags.Static;

			foreach (var m in type.GetMethods(flags))
			{
				// Only "TimeStretch..." family
				if (!m.Name.StartsWith("TimeStretch", StringComparison.Ordinal))
				{
					continue;
				}

				// Basic signature sanity:
				// - first parameter must be AudioObj
				var ps = m.GetParameters();
				if (ps.Length == 0 || ps[0].ParameterType != typeof(AudioObj))
				{
					continue;
				}

				// - return type should be Task<AudioObj> (or derived generic Task)
				if (!IsTaskOfAudioObj(m.ReturnType))
				{
					continue;
				}

				if (TryBuildTimeStretchLabel(m.Name, out string? label))
				{
					// Avoid duplicates if any (overloads) - keep unique keys (MethodInfo is unique anyway)
					result[m] = label!;
				}
			}

			return result;
		}

		private static bool IsTaskOfAudioObj(Type t)
		{
			// Task<AudioObj>
			if (!t.IsGenericType)
			{
				return false;
			}

			if (t.GetGenericTypeDefinition() != typeof(Task<>))
			{
				return false;
			}

			return t.GetGenericArguments()[0] == typeof(AudioObj);
		}

		private static bool TryBuildTimeStretchLabel(string methodName, out string? label)
		{
			label = null;

			int firstUnderscore = methodName.IndexOf('_');
			if (firstUnderscore < 0 || firstUnderscore >= methodName.Length - 1)
			{
				return false;
			}

			string suffix = methodName.Substring(firstUnderscore + 1); // e.g. "V2_Best" or "V3"

			var rx = new Regex(@"^V(?<ver>\d+)(?:_(?<preset>.+))?$", RegexOptions.CultureInvariant);
			var match = rx.Match(suffix);
			if (!match.Success)
			{
				return false;
			}

			string ver = match.Groups["ver"].Value; // "2"
			string presetRaw = match.Groups["preset"].Success ? match.Groups["preset"].Value : "";

			if (string.IsNullOrWhiteSpace(presetRaw))
			{
				label = $"V{ver}";
				return true;
			}

			// Convert preset: "Best" => "Best", "Super_Fast" => "Super Fast"
			string preset = presetRaw.Replace('_', ' ').Trim();

			// Title-case it nicely, but don't mangle all-caps acronyms too hard
			preset = ToTitleCaseSoft(preset);

			label = $"V{ver} ({preset})";
			return true;
		}

		private static string ToTitleCaseSoft(string s)
		{
			if (string.IsNullOrWhiteSpace(s))
			{
				return s;
			}

			// If user already wrote ALL CAPS, keep it.
			bool allCaps = true;
			foreach (char c in s)
			{
				if (char.IsLetter(c) && !char.IsUpper(c))
				{
					allCaps = false;
					break;
				}
			}
			if (allCaps)
			{
				return s;
			}

			// TitleCase with invariant culture (stable UI)
			TextInfo ti = CultureInfo.InvariantCulture.TextInfo;
			return ti.ToTitleCase(s.ToLowerInvariant());
		}



		// Enums
		public enum StereoCorrelationMode
		{
			Mono,     // old behavior
			Stereo,   // correlate L and R separately and sum
			MidSide   // correlate mid and side separately (often best for wide mixes)
		}


		// Buffers
		private sealed class CorrLocalBuffers
		{
			private readonly int _ov;
			private readonly int _ch;
			private readonly StereoCorrelationMode _mode;

			// Reference
			private readonly float[] _refA;
			private readonly float[] _refB;
			private double _refEA;
			private double _refEB;

			// Candidate
			private readonly float[] _candA;
			private readonly float[] _candB;
			private double _candEA;
			private double _candEB;

			public double BestScore { get; set; } = double.NegativeInfinity;
			public int BestIn { get; set; }

			// Mid/Side: weight for Side correlation term
			public float SideWeight { get; set; } = 0.75f;

			// Correlation subsampling stride (1 = full quality)
			public int Stride { get; set; } = 2;

			public CorrLocalBuffers(int ov, int ch, StereoCorrelationMode mode)
			{
				_ov = ov;
				_ch = ch;
				_mode = mode;

				_refA = new float[ov];
				_candA = new float[ov];

				if (mode != StereoCorrelationMode.Mono)
				{
					_refB = new float[ov];
					_candB = new float[ov];
				}
				else
				{
					_refB = Array.Empty<float>();
					_candB = Array.Empty<float>();
				}
			}

			public void CopyReferenceFrom(CorrLocalBuffers other)
			{
				Array.Copy(other._refA, _refA, _ov);
				_refEA = other._refEA;

				if (_mode != StereoCorrelationMode.Mono)
				{
					Array.Copy(other._refB, _refB, _ov);
					_refEB = other._refEB;
					SideWeight = other.SideWeight;
				}
			}

			public void FillFromOutput(float[] outInterleaved, int startFrame)
			{
				FillRepresentation(outInterleaved, startFrame, isSource: false);
				ComputeEnergyRef();
			}

			public void FillFromSource(float[] srcInterleaved, int startFrame)
			{
				FillRepresentation(srcInterleaved, startFrame, isSource: true);
				ComputeEnergyCand();
			}

			private void FillRepresentation(float[] interleaved, int startFrame, bool isSource)
			{
				// If not stereo, fall back to mono mixdown of all channels
				bool isStereo = (_ch == 2);

				for (int i = 0; i < _ov; i++)
				{
					int baseIdx = (startFrame + i) * _ch;

					if (!isStereo)
					{
						double sum = 0.0;
						for (int c = 0; c < _ch; c++)
						{
							sum += interleaved[baseIdx + c];
						}

						float m = (float) (sum / _ch);

						if (isSource)
						{
							_candA[i] = m;
						}
						else
						{
							_refA[i] = m;
						}

						continue;
					}

					float L = interleaved[baseIdx + 0];
					float R = interleaved[baseIdx + 1];

					switch (_mode)
					{
						case StereoCorrelationMode.Mono:
							{
								float m = 0.5f * (L + R);
								if (isSource)
								{
									_candA[i] = m;
								}
								else
								{
									_refA[i] = m;
								}

								break;
							}
						case StereoCorrelationMode.Stereo:
							{
								if (isSource) { _candA[i] = L; _candB[i] = R; }
								else { _refA[i] = L; _refB[i] = R; }
								break;
							}
						case StereoCorrelationMode.MidSide:
							{
								float mid = 0.5f * (L + R);
								float side = 0.5f * (L - R);
								if (isSource) { _candA[i] = mid; _candB[i] = side; }
								else { _refA[i] = mid; _refB[i] = side; }
								break;
							}
					}
				}
			}

			public double Score()
			{
				int stride = Math.Clamp(Stride, 1, 16);

				double dotA = Dot(_refA, _candA, stride);
				double denomA = Math.Sqrt(_refEA * _candEA) + 1e-12;
				double scoreA = dotA / denomA;

				if (_mode == StereoCorrelationMode.Mono)
				{
					return scoreA;
				}

				double dotB = Dot(_refB, _candB, stride);
				double denomB = Math.Sqrt(_refEB * _candEB) + 1e-12;
				double scoreB = dotB / denomB;

				if (_mode == StereoCorrelationMode.Stereo)
				{
					return scoreA + scoreB;
				}

				// MidSide: weight Side a bit less (configurable)
				return scoreA + (SideWeight * scoreB);
			}

			private void ComputeEnergyRef()
			{
				int stride = Math.Clamp(Stride, 1, 16);
				_refEA = Energy(_refA, stride);
				_refEB = (_mode == StereoCorrelationMode.Mono) ? 0.0 : Energy(_refB, stride);
			}

			private void ComputeEnergyCand()
			{
				int stride = Math.Clamp(Stride, 1, 16);
				_candEA = Energy(_candA, stride);
				_candEB = (_mode == StereoCorrelationMode.Mono) ? 0.0 : Energy(_candB, stride);
			}

			private static double Energy(float[] x, int stride)
			{
				double e = 0.0;
				for (int i = 0; i < x.Length; i += stride)
				{
					double v = x[i];
					e += v * v;
				}
				return e;
			}

			private static double Dot(float[] a, float[] b, int stride)
			{
				double dot = 0.0;
				for (int i = 0; i < a.Length; i += stride)
				{
					dot += (double) a[i] * b[i];
				}

				return dot;
			}
		}
	}
}
