using System;
using System.Buffers;
using System.Threading;
using System.Threading.Tasks;

namespace LPAP.Audio.Processing
{
	public static partial class AudioProcessor
	{
		/// <summary>
		/// "Elastique-like" HQ time-stretch engine (native C#):
		/// - Hybrid: WSOLA (transients) + Phase Vocoder HQ (tonal)
		/// - Transient detector + soft blend (per frame)
		/// - Peak-locked phase vocoder + proper OLA normalization
		/// - Non-blocking (Task.Run), CPU parallel where safe
		///
		/// NOTE: Not zplane Elastique. This is an original hybrid engine inspired by common best practices.
		///
		/// factor: 1.0 unchanged, <1 shorter, >1 longer
		/// </summary>
		public static Task<AudioObj> TimeStretchAsync_V6_ElastiqueLikeHQ(
	AudioObj obj,
	double factor = 1.0,
	int chunkSize = 2048,          // was chunkSize
	float overlap = 0.75f,         // was overlap

	int wsolaWindow = 0,           // 0 = auto
	int wsolaSearch = 0,           // 0 = auto
	int wsolaStep = 0,             // 0 = auto

	double transientStrength = 3.0,
	int peakLockRadiusBins = 2,
	bool multiResolution = true,
	float normalizeRms = 0f,
	int maxWorkers = 0,
	IProgress<double>? progress = null,
	CancellationToken ct = default)
		{
			if (obj == null) throw new ArgumentNullException(nameof(obj));
			if (obj.Data == null || obj.Data.Length == 0) return Task.FromResult(obj);
			if (obj.SampleRate <= 0 || obj.Channels <= 0) return Task.FromResult(obj);

			factor = Math.Clamp(factor, 0.05, 8.0);
			// --- PV main params (renamed) ---
			int N1 = NextPow2(Math.Clamp(chunkSize, 256, 1 << 15));
			overlap = Math.Clamp(overlap, 0.25f, 0.9375f);
			int hopA1 = Math.Max(1, (int) Math.Round(N1 * (1.0 - overlap)));
			double hopS1 = hopA1 * factor;

			// --- Auto WSOLA defaults if 0 ---
			// window: half of PV window is a good general default
			int autoWSWin = Math.Max(256, N1 / 2);
			autoWSWin = NextPow2(autoWSWin);

			wsolaWindow = wsolaWindow > 0 ? wsolaWindow : autoWSWin;
			wsolaWindow = NextPow2(Math.Clamp(wsolaWindow, 256, 1 << 14));

			wsolaSearch = wsolaSearch > 0 ? wsolaSearch : (wsolaWindow / 2);
			wsolaSearch = Math.Clamp(wsolaSearch, 64, 1 << 15);

			// step: ~32 candidates across search radius (balanced quality/speed)
			wsolaStep = wsolaStep > 0 ? wsolaStep : Math.Max(1, wsolaSearch / 32);
			wsolaStep = Math.Clamp(wsolaStep, 1, 256);

			// (optional) if you want step aligned:
			if ((wsolaStep & 1) == 1 && wsolaStep > 1) wsolaStep--; // make even

			int ch = obj.Channels;
			int inFrames = obj.Data.Length / ch;
			if (inFrames < 64) return Task.FromResult(obj);

			int workers = maxWorkers <= 0 ? Environment.ProcessorCount : Math.Max(1, maxWorkers);

			// Multi-res PV (smaller window for highs/transients-ish, bigger for lows/tonal body)
			int N2 = NextPow2(Math.Clamp(N1 / 2, 256, 1 << 14));
			int hopA2 = Math.Max(1, (int) Math.Round(N2 * (1.0 - overlap)));
			double hopS2 = hopA2 * factor;

			// WSOLA params
			int wWin = NextPow2(Math.Clamp(wsolaWindow, 256, 1 << 14));
			int wSearch = Math.Clamp(wsolaSearch, 64, 1 << 15);
			int wStep = Math.Clamp(wsolaStep, 1, 256);

			peakLockRadiusBins = Math.Clamp(peakLockRadiusBins, 0, 8);
			transientStrength = transientStrength < 0 ? 0 : transientStrength;

			// Output estimate
			int outFramesEst = Math.Max(Math.Max(N1, wWin) + 16, (int) Math.Round(inFrames * factor) + Math.Max(N1, wWin) + 16);
			var outData = new float[outFramesEst * ch];

			return Task.Run(() =>
			{
				ct.ThrowIfCancellationRequested();

				var par = new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = workers };
				int totalProgSteps = Math.Max(1, (inFrames - Math.Max(N1, wWin)) / Math.Max(1, hopA1));
				int progDone = 0;

				Parallel.For(0, ch, par, channel =>
				{
					ct.ThrowIfCancellationRequested();

					// ----- Shared windows -----
					var winPV1 = ArrayPool<float>.Shared.Rent(N1);
					var winPV1Pow = ArrayPool<float>.Shared.Rent(N1);
					MakeHann(winPV1, winPV1Pow, N1);

					float[]? winPV2 = null;
					float[]? winPV2Pow = null;
					if (multiResolution)
					{
						winPV2 = ArrayPool<float>.Shared.Rent(N2);
						winPV2Pow = ArrayPool<float>.Shared.Rent(N2);
						MakeHann(winPV2, winPV2Pow, N2);
					}

					var winWS = ArrayPool<float>.Shared.Rent(wWin);
					var winWSPow = ArrayPool<float>.Shared.Rent(wWin);
					MakeHann(winWS, winWSPow, wWin);

					// Per-channel output + norm (then write to interleaved outData)
					var outCh = new float[outFramesEst];
					var outNorm = new float[outFramesEst];

					// ----- Build transient map (lightweight) -----
					// We compute energy per analysis hop and detect spikes.
					// Then we turn it into a smooth blend weight [0..1] (1 = use WSOLA more).
					var blend = BuildTransientBlendMap(
						obj.Data, inFrames, ch, channel,
						N2, hopA2,
						transientStrength,
						ct);

					// ----- Run PV (tonal) -----
					PhaseVocoderHQ(
						obj.Data, inFrames, ch, channel,
						outCh, outNorm, outFramesEst,
						N1, hopA1, hopS1,
						winPV1, winPV1Pow,
						peakLockRadiusBins,
						blend, // blend used only for optional phase reset weighting
						progress != null && channel == 0 ? () =>
						{
							int d = Interlocked.Increment(ref progDone);
							if ((d & 31) == 0) progress!.Report(Math.Clamp((double) d / totalProgSteps, 0, 1));
						}
					: null,
						ct);

					// Multi-res PV add (high-band-ish refinement)
					if (multiResolution && winPV2 != null && winPV2Pow != null)
					{
						var outCh2 = new float[outFramesEst];
						var outNorm2 = new float[outFramesEst];

						PhaseVocoderHQ(
							obj.Data, inFrames, ch, channel,
							outCh2, outNorm2, outFramesEst,
							N2, hopA2, hopS2,
							winPV2, winPV2Pow,
							peakLockRadiusBins,
							blend,
							null,
							ct);

						// Mix: emphasize smaller window where blend suggests more transient-ish content.
						// We do a gentle static mix too (keeps highs more stable).
						for (int i = 0; i < outFramesEst; i++)
						{
							float b = (blend != null && i < blend.Length) ? blend[i] : 0f; // 0 tonal, 1 transient
							float a = 0.25f + 0.35f * b; // more small-window on transients
							outCh[i] = outCh[i] * (1f - a) + outCh2[i] * a;
							outNorm[i] = outNorm[i] * (1f - a) + outNorm2[i] * a;
						}
					}

					// ----- Run WSOLA (transients) -----
					var outWS = new float[outFramesEst];
					var outWSNorm = new float[outFramesEst];

					WSOLA(
						obj.Data, inFrames, ch, channel,
						outWS, outWSNorm, outFramesEst,
						factor,
						wWin, winWS, winWSPow,
						wSearch, wStep,
						workers,
						ct);

					// ----- Hybrid blend (per frame) -----
					for (int i = 0; i < outFramesEst; i++)
					{
						float b = (blend != null && i < blend.Length) ? blend[i] : 0f; // 0 tonal -> PV; 1 transient -> WSOLA
																					   // equal-power crossfade
						float wp = (float) Math.Cos(0.5 * Math.PI * b);
						float ww = (float) Math.Sin(0.5 * Math.PI * b);

						float y = outCh[i] * wp + outWS[i] * ww;
						float n = outNorm[i] * wp + outWSNorm[i] * ww;

						outCh[i] = y;
						outNorm[i] = n;
					}

					// ----- Normalize OLA -----
					for (int i = 0; i < outFramesEst; i++)
					{
						float n = outNorm[i];
						if (n > 1e-12f) outCh[i] /= n;
					}

					// Write to interleaved output
					for (int f = 0; f < outFramesEst; f++)
					{
						outData[f * ch + channel] = outCh[f];
					}

					// Return pooled windows
					ArrayPool<float>.Shared.Return(winPV1);
					ArrayPool<float>.Shared.Return(winPV1Pow);
					ArrayPool<float>.Shared.Return(winWS);
					ArrayPool<float>.Shared.Return(winWSPow);
					if (winPV2 != null) ArrayPool<float>.Shared.Return(winPV2);
					if (winPV2Pow != null) ArrayPool<float>.Shared.Return(winPV2Pow);
				});

				// Trim tail
				int lastNonZero = outData.Length - 1;
				while (lastNonZero > 0 && Math.Abs(outData[lastNonZero]) < 1e-9) lastNonZero--;
				int outLen = Math.Max((lastNonZero + 1 + (ch - 1)) / ch, 0) * ch;
				outLen = Math.Clamp(outLen, ch * 64, outData.Length);

				var final = new float[outLen];
				Array.Copy(outData, final, final.Length);

				// Optional RMS normalize
				if (normalizeRms > 0)
				{
					double sumSq = 0.0;
					for (int i = 0; i < final.Length; i++) sumSq += (double) final[i] * final[i];
					double rms = Math.Sqrt(sumSq / Math.Max(1, final.Length));
					if (rms > 1e-9)
					{
						double g = normalizeRms / rms;
						for (int i = 0; i < final.Length; i++) final[i] = (float) (final[i] * g);
					}
				}

				obj.Data = final;
				obj.StretchFactor = factor;
				if (obj.BeatsPerMinute > 1e-3) obj.BeatsPerMinute = obj.BeatsPerMinute / factor;
				obj.DataChanged();
				progress?.Report(1.0);
				return obj;

			}, ct);
		}

		// ------------------------------
		// Transient blend map
		// ------------------------------

		/// <summary>
		/// Returns per-output-frame blend weight in [0..1] where 1 means "use WSOLA".
		/// We compute energy spikes in analysis domain and project into output time by factor.
		/// </summary>
		private static float[] BuildTransientBlendMap(
			float[] interleaved, int inFrames, int channels, int channel,
			int analysisWin, int hopA,
			double transientStrength,
			CancellationToken ct)
		{
			// If disabled -> all tonal
			if (transientStrength <= 0) return new float[Math.Max(1, inFrames)];

			int steps = Math.Max(1, (inFrames - analysisWin) / hopA);
			var energies = new double[steps];

			// Energy per frame
			int pos = 0;
			for (int s = 0; s < steps; s++)
			{
				ct.ThrowIfCancellationRequested();
				double e = 0.0;
				for (int i = 0; i < analysisWin; i++)
				{
					float x = interleaved[(pos + i) * channels + channel];
					e += x * x;
				}
				energies[s] = e / analysisWin;
				pos += hopA;
			}

			// Detect spikes by ratio against smoothed energy
			var transient = new float[steps];
			double smooth = energies[0] + 1e-12;
			for (int s = 0; s < steps; s++)
			{
				ct.ThrowIfCancellationRequested();
				smooth = 0.92 * smooth + 0.08 * energies[s];
				double ratio = energies[s] / (smooth + 1e-12);

				// map ratio -> weight
				// threshold-ish at transientStrength: ratio >= transientStrength => strong transient
				double w = (ratio - transientStrength) / Math.Max(1e-9, transientStrength * 0.75);
				w = Math.Clamp(w, 0.0, 1.0);
				transient[s] = (float) w;
			}

			// Smooth transient weights (avoid flicker)
			Smooth1D(transient, radius: 3);

			// Project to "frame" domain approx by hop indexing
			// We'll produce a per-output-frame blend later; here we just return per-sample-ish map length inFrames
			// using nearest hop region.
			var map = new float[Math.Max(1, inFrames)];
			for (int s = 0; s < steps; s++)
			{
				int start = s * hopA;
				int end = Math.Min(inFrames, start + hopA);
				float w = transient[s];
				for (int i = start; i < end; i++) map[i] = Math.Max(map[i], w);
			}

			// Spread transient influence a bit (pre/post transient)
			Spread(map, radius: analysisWin / 8);
			Smooth1D(map, radius: analysisWin / 32);

			return map;
		}

		private static void Smooth1D(float[] a, int radius)
		{
			if (radius <= 0 || a.Length < 3) return;
			var tmp = new float[a.Length];
			for (int i = 0; i < a.Length; i++)
			{
				int lo = Math.Max(0, i - radius);
				int hi = Math.Min(a.Length - 1, i + radius);
				float sum = 0f;
				int n = 0;
				for (int j = lo; j <= hi; j++) { sum += a[j]; n++; }
				tmp[i] = sum / Math.Max(1, n);
			}
			Array.Copy(tmp, a, a.Length);
		}

		private static void Spread(float[] a, int radius)
		{
			if (radius <= 0 || a.Length < 3) return;
			for (int i = 0; i < a.Length; i++)
			{
				float v = a[i];
				if (v <= 0.001f) continue;
				int lo = Math.Max(0, i - radius);
				int hi = Math.Min(a.Length - 1, i + radius);
				for (int j = lo; j <= hi; j++)
					a[j] = Math.Max(a[j], v * 0.85f);
			}
		}

		// ------------------------------
		// WSOLA (transients)
		// ------------------------------

		private static void WSOLA(
	float[] interleaved, int inFrames, int channels, int channel,
	float[] outCh, float[] outNorm, int outFramesEst,
	double factor,
	int win, float[] window, float[] winPow,
	int searchRadius, int searchStep,
	int workers,
	CancellationToken ct)
		{
			// If input too short for WSOLA window: nothing to do
			if (inFrames <= 0 || win <= 0 || inFrames < win)
				return;

			// WSOLA hop (50% overlap)
			int hop = win / 2;
			if (hop < 1) hop = 1;

			// Advance input by hop/factor per output hop
			double inStep = hop / factor;

			int outPos = 0;
			double inCenter = 0.0;

			// Best match start (in frames)
			int bestIn = 0;

			// Precompute max valid start index so (start + win) <= inFrames always holds
			int maxStart = inFrames - win;
			if (maxStart < 0) return;

			// Safety: keep bestIn valid
			bestIn = Math.Clamp(bestIn, 0, maxStart);

			while (outPos + win <= outFramesEst)
			{
				ct.ThrowIfCancellationRequested();

				// Target (frame index)
				int target = (int) Math.Round(inCenter);
				target = Math.Clamp(target, 0, maxStart);

				// Candidate search range in valid starts
				int start = Math.Max(0, target - searchRadius);
				int end = Math.Min(maxStart, target + searchRadius);

				// If we have no room to search, just use target
				int found;
				if (end <= start)
				{
					found = start;
				}
				else
				{
					found = FindBestCorrelation(
						interleaved, inFrames, channels, channel,
						refFrameStart: bestIn,
						candStart: start,
						candEnd: end,
						win: win,
						step: searchStep,
						workers: workers,
						ct: ct);
				}

				// HARD CLAMP (this is the important part)
				bestIn = Math.Clamp(found, 0, maxStart);

				// One more guard before reading
				if (bestIn + win > inFrames)
					break;

				// OLA this frame into output
				for (int i = 0; i < win; i++)
				{
					int dst = outPos + i;
					if ((uint) dst >= (uint) outFramesEst) break;

					// Now guaranteed in-bounds:
					int srcFrame = bestIn + i;
					int srcIdx = srcFrame * channels + channel;
					float x = interleaved[srcIdx];

					float w = window[i];
					outCh[dst] += x * w;
					outNorm[dst] += winPow[i];
				}

				outPos += hop;
				inCenter += inStep;

				// Stop if next target would exceed feasible input range by too much
				if (inCenter > maxStart)
				{
					// we still might want one last frame; you can break earlier for speed
					// break;
				}
			}
		}


		private static int FindBestCorrelation(
	float[] interleaved, int inFrames, int channels, int channel,
	int refFrameStart,
	int candStart, int candEnd,
	int win, int step,
	int workers,
	CancellationToken ct)
		{
			// Valid start range for any window
			int maxStart = inFrames - win;
			if (maxStart < 0) return 0;

			candStart = Math.Clamp(candStart, 0, maxStart);
			candEnd = Math.Clamp(candEnd, 0, maxStart);

			if (candEnd <= candStart)
				return candStart;

			if (step <= 0) step = 1;

			int best = candStart;
			double bestScore = double.NegativeInfinity;

			int count = ((candEnd - candStart) / step) + 1;
			if (count <= 1) return candStart;

			object gate = new object();

			Parallel.For(0, count, new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = workers }, idx =>
			{
				ct.ThrowIfCancellationRequested();

				int c = candStart + idx * step;
				if (c > candEnd) return;

				// extra safety
				if (c < 0 || c > maxStart) return;
				if (refFrameStart < 0 || refFrameStart > maxStart) return;

				double score = NCC(interleaved, channels, channel, refFrameStart, c, win);

				lock (gate)
				{
					if (score > bestScore)
					{
						bestScore = score;
						best = c;
					}
				}
			});

			return Math.Clamp(best, 0, maxStart);
		}


		// Normalized cross-correlation between two windows
		private static double NCC(float[] interleaved, int channels, int channel, int aStart, int bStart, int win)
		{
			double sumA = 0, sumB = 0, sumAA = 0, sumBB = 0, sumAB = 0;

			int idxA = aStart * channels + channel;
			int idxB = bStart * channels + channel;

			for (int i = 0; i < win; i++)
			{
				float a = interleaved[idxA];
				float b = interleaved[idxB];
				idxA += channels;
				idxB += channels;

				sumA += a;
				sumB += b;
				sumAA += a * a;
				sumBB += b * b;
				sumAB += a * b;
			}

			double n = win;
			double num = sumAB - (sumA * sumB / n);
			double denA = sumAA - (sumA * sumA / n);
			double denB = sumBB - (sumB * sumB / n);
			double den = Math.Sqrt(Math.Max(1e-18, denA * denB));
			return num / den;
		}

		// ------------------------------
		// Phase Vocoder HQ (tonal)
		// ------------------------------

		private static void PhaseVocoderHQ(
			float[] interleaved, int inFrames, int channels, int channel,
			float[] outCh, float[] outNorm, int outFramesEst,
			int N, int hopA, double hopS,
			float[] window, float[] winPow,
			int peakLockRadiusBins,
			float[] blendMap, // used for soft phase reset on transients (optional)
			Action? onStep,
			CancellationToken ct)
		{
			var inBuf = ArrayPool<float>.Shared.Rent(N);
			var real = ArrayPool<double>.Shared.Rent(N);
			var imag = ArrayPool<double>.Shared.Rent(N);

			var prevPhase = ArrayPool<double>.Shared.Rent(N);
			var synPhase = ArrayPool<double>.Shared.Rent(N);
			var mag = ArrayPool<double>.Shared.Rent(N);
			var phase = ArrayPool<double>.Shared.Rent(N);

			int half = N / 2;
			var isPeak = peakLockRadiusBins > 0 ? ArrayPool<byte>.Shared.Rent(half + 1) : null;

			Array.Clear(prevPhase, 0, N);
			Array.Clear(synPhase, 0, N);

			double twoPi = 2.0 * Math.PI;

			int inPos = 0;
			double outPosD = 0.0;

			while (inPos + N <= inFrames)
			{
				ct.ThrowIfCancellationRequested();

				for (int i = 0; i < N; i++)
				{
					float x = interleaved[(inPos + i) * channels + channel];
					inBuf[i] = x * window[i];
				}

				for (int i = 0; i < N; i++) { real[i] = inBuf[i]; imag[i] = 0.0; }
				FFT(real, imag, false);

				for (int k = 0; k < N; k++)
				{
					double r = real[k];
					double im = imag[k];
					mag[k] = Math.Sqrt(r * r + im * im);
					phase[k] = Math.Atan2(im, r);
				}

				// peak detect
				if (isPeak != null)
				{
					Array.Clear(isPeak, 0, half + 1);
					for (int k = 2; k < half - 1; k++)
					{
						double m0 = mag[k];
						if (m0 > mag[k - 1] && m0 > mag[k + 1]) isPeak[k] = 1;
					}
				}

				// transient weighting (soft phase reset)
				int outPosI = (int) Math.Round(outPosD);
				float transient = 0f;
				if (blendMap != null && blendMap.Length > 0)
				{
					// blendMap is in input-time; map to current input position
					int idx = Math.Clamp(inPos, 0, blendMap.Length - 1);
					transient = blendMap[idx];
				}

				if (transient > 0.6f)
				{
					// hard-ish reset on strong transients
					for (int k = 0; k < N; k++) synPhase[k] = phase[k];
				}
				else
				{
					for (int k = 0; k < N; k++)
					{
						double omega = twoPi * k / N;
						double expected = omega * hopA;

						double delta = phase[k] - prevPhase[k] - expected;
						delta -= twoPi * Math.Round(delta / twoPi);

						double trueFreq = omega + (delta / hopA);
						synPhase[k] += trueFreq * hopS;
					}

					if (isPeak != null && peakLockRadiusBins > 0)
					{
						ApplyPeakLockingNew(N, half, isPeak, peakLockRadiusBins, phase, synPhase);
					}

					// soft blend to analysis phase on mild transient
					if (transient > 0.05f)
					{
						double t = transient; // 0..1
						for (int k = 0; k < N; k++)
						{
							double a = phase[k];
							double s = synPhase[k];
							double d = a - s;
							d -= twoPi * Math.Round(d / twoPi);
							synPhase[k] = s + d * t;
						}
					}
				}

				for (int k = 0; k < N; k++)
				{
					double m = mag[k];
					double ph = synPhase[k];
					real[k] = m * Math.Cos(ph);
					imag[k] = m * Math.Sin(ph);
				}

				FFT(real, imag, true);

				for (int i = 0; i < N; i++)
				{
					int dst = outPosI + i;
					if ((uint) dst >= (uint) outFramesEst) break;
					float s = (float) real[i] * window[i];
					outCh[dst] += s;
					outNorm[dst] += winPow[i];
				}

				Buffer.BlockCopy(phase, 0, prevPhase, 0, N * sizeof(double));

				inPos += hopA;
				outPosD += hopS;

				onStep?.Invoke();
			}

			ArrayPool<float>.Shared.Return(inBuf);
			ArrayPool<double>.Shared.Return(real);
			ArrayPool<double>.Shared.Return(imag);
			ArrayPool<double>.Shared.Return(prevPhase);
			ArrayPool<double>.Shared.Return(synPhase);
			ArrayPool<double>.Shared.Return(mag);
			ArrayPool<double>.Shared.Return(phase);
			if (isPeak != null) ArrayPool<byte>.Shared.Return(isPeak);
		}

		private static void ApplyPeakLockingNew(int N, int half, byte[] isPeak, int radius, double[] anaPhase, double[] synPhase)
		{
			double twoPi = 2.0 * Math.PI;

			for (int pk = 2; pk < half - 1; pk++)
			{
				if (isPeak[pk] == 0) continue;

				int left = Math.Max(0, pk - radius);
				int right = Math.Min(half, pk + radius);

				double baseSyn = synPhase[pk];
				double baseAna = anaPhase[pk];

				for (int b = left; b <= right; b++)
				{
					if (b == pk) continue;
					double rel = anaPhase[b] - baseAna;
					rel -= twoPi * Math.Round(rel / twoPi);
					synPhase[b] = baseSyn + rel;

					int mirror = (N - b) & (N - 1);
					if (mirror != b && mirror >= 0 && mirror < N)
						synPhase[mirror] = -synPhase[b];
				}
			}
		}

		// ------------------------------
		// Utils
		// ------------------------------

		private static void MakeHann(float[] window, float[] winPow, int n)
		{
			for (int i = 0; i < n; i++)
			{
				float w = 0.5f - 0.5f * (float) Math.Cos(2.0 * Math.PI * i / n);
				window[i] = w;
				winPow[i] = w * w;
			}
		}

		
	}
}
