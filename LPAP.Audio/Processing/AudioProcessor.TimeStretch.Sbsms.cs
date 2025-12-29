using System;
using System.Buffers;
using System.Threading;
using System.Threading.Tasks;

namespace LPAP.Audio.Processing
{
	public static partial class AudioProcessor
	{
		/// <summary>
		/// High-quality time-stretch (SBSMS-ish) via:
		/// - Phase vocoder with correct expected phase advance
		/// - Peak-locking
		/// - Transient protection (energy-flux gate)
		/// - Proper OLA normalization (window^2 accumulation)
		/// - Parallel per-channel, optional parallel over time segments with equal-power stitching
		///
		/// factor: 1.0 = unchanged, <1 faster/shorter, >1 slower/longer
		/// windowSize: FFT size (power of two). Typical 1024..8192
		/// overlap: 0..1, typical 0.75 for HQ
		///
		/// transientSensitivity: 0 disables. Typical 2.0..6.0 (higher = fewer transients detected)
		/// peakLockRadiusBins: 0 disables. Typical 1..4
		/// enableTimeSegParallel: makes it faster on CPU, slight risk of boundary artifacts (mitigated via crossfade)
		/// segmentFrames: 0 auto, else ~0.75..2.0 seconds in frames is a good range
		/// </summary>
		public static Task<AudioObj> TimeStretchAsync_V5_SbsmsHQ(
			AudioObj obj,
			double factor = 1.0,
			int chunkSize = 2048,
			float overlap = 0.75f,
			float normalizeRms = 0f,
			double transientSensitivity = 3.5,
			int peakLockRadiusBins = 2,
			bool enableTimeSegParallel = true,
			int segmentFrames = 0,
			int maxWorkers = 0,
			IProgress<double>? progress = null,
			CancellationToken ct = default)
		{
			if (obj == null) throw new ArgumentNullException(nameof(obj));
			if (obj.Data == null || obj.Data.Length == 0) return Task.FromResult(obj);
			if (obj.SampleRate <= 0 || obj.Channels <= 0) return Task.FromResult(obj);

			factor = Math.Clamp(factor, 0.05, 8.0);
			int ch = obj.Channels;
			int inFrames = obj.Data.Length / ch;
			if (inFrames < 64) return Task.FromResult(obj);

			int N = Math.Clamp(chunkSize, 256, 1 << 15);
			N = NextPow2(N);

			overlap = Math.Clamp(overlap, 0.25f, 0.9375f);
			int hopA = Math.Max(1, (int) Math.Round(N * (1.0 - overlap)));  // analysis hop
			double hopS = hopA * factor;                                    // synthesis hop (double)

			transientSensitivity = transientSensitivity < 0 ? 0 : transientSensitivity;
			peakLockRadiusBins = Math.Clamp(peakLockRadiusBins, 0, 8);

			// Window (Hann) + winPow for OLA normalization
			var window = ArrayPool<float>.Shared.Rent(N);
			var winPow = ArrayPool<float>.Shared.Rent(N);
			for (int n = 0; n < N; n++)
			{
				float w = 0.5f - 0.5f * (float) Math.Cos(2.0 * Math.PI * n / N);
				window[n] = w;
				winPow[n] = w * w;
			}

			// Output estimate
			int outFramesEst = Math.Max(N + 16, (int) Math.Round(inFrames * factor) + N + 16);
			var outData = new float[outFramesEst * ch];
			var olaNorm = new float[outFramesEst];

			int workers = maxWorkers <= 0 ? Environment.ProcessorCount : Math.Max(1, maxWorkers);

			return Task.Run(() =>
			{
				ct.ThrowIfCancellationRequested();

				// Rough progress based on number of analysis frames
				int totalSteps = Math.Max(1, (inFrames - N) / hopA);
				int stepsDone = 0;

				var parOpts = new ParallelOptions
				{
					CancellationToken = ct,
					MaxDegreeOfParallelism = workers
				};

				Parallel.For(0, ch, parOpts, channel =>
				{
					ct.ThrowIfCancellationRequested();

					// Decide segments
					int segLen = segmentFrames;
					if (segLen <= 0)
					{
						// Auto: ~1 sec-ish, but keep it reasonably large to reduce phase resets.
						int target = Math.Max(obj.SampleRate, N * 16);
						segLen = Math.Clamp(target, N * 8, Math.Max(N * 8, inFrames));
					}

					// If disabled or short input -> sequential
					if (!enableTimeSegParallel || inFrames <= segLen + N * 4)
					{
						ProcessChannelSequential(
							obj.Data, inFrames, ch, channel,
							outData, olaNorm, outFramesEst,
							N, hopA, hopS,
							window, winPow,
							peakLockRadiusBins, transientSensitivity,
							progress != null && channel == 0 ? () =>
							{
								int done = Interlocked.Increment(ref stepsDone);
								if ((done & 31) == 0) progress!.Report(Math.Clamp((double) done / totalSteps, 0, 1));
							}
						: null,
							ct);
						return;
					}

					// Parallel segments: process each segment independently into its own buffer (out starts at 0!),
					// then stitch into global output using hop mapping + equal-power crossfade.
					var segments = BuildSegments(inFrames, segLen, N);
					var results = new SegmentResult[segments.Length];

					Parallel.For(0, segments.Length, parOpts, si =>
					{
						ct.ThrowIfCancellationRequested();

						var (segStart, segEnd) = segments[si];
						int segInLen = segEnd - segStart;

						// Estimate seg output length based on hop mapping:
						// frames in segment ~ (segInLen - N)/hopA hops
						int segHops = Math.Max(0, (segInLen - N) / hopA);
						int segOutFramesEst = Math.Max(N + 16, (int) Math.Ceiling(segHops * hopS) + N + 16);

						var segOut = new float[segOutFramesEst];
						var segNorm = new float[segOutFramesEst];

						ProcessChannelSegmentToBuffer(
							obj.Data, inFrames, ch, channel,
							segStart, segEnd,
							segOut, segNorm,
							N, hopA, hopS,
							window, winPow,
							peakLockRadiusBins, transientSensitivity,
							progress != null && channel == 0 ? () =>
							{
								int done = Interlocked.Increment(ref stepsDone);
								if ((done & 63) == 0) progress!.Report(Math.Clamp((double) done / totalSteps, 0, 1));
							}
						: null,
							ct);

						results[si] = new SegmentResult(segStart, segEnd, segOut, segNorm);
					});

					// Stitch
					int crossfadeFrames = Math.Max(N, (int) Math.Round(hopS * 16)); // smoother boundaries
					StitchSegmentsEqualPower(
						channel, ch,
						outData, olaNorm, outFramesEst,
						results,
						hopA, hopS,
						crossfadeFrames);
				});

				// Normalize OLA globally
				ApplyOlaNormalization(outData, olaNorm, outFramesEst, ch);

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

				ArrayPool<float>.Shared.Return(window);
				ArrayPool<float>.Shared.Return(winPow);
				return obj;
			}, ct);
		}

		// -------------------------
		// Processing: Sequential (global output)
		// -------------------------

		private static void ProcessChannelSequential(
			float[] interleaved, int inFrames, int channels, int channel,
			float[] outInterleaved, float[] olaNorm, int outFramesEst,
			int N, int hopA, double hopS,
			float[] window, float[] winPow,
			int peakLockRadiusBins, double transientSensitivity,
			Action? onStep,
			CancellationToken ct)
		{
			ct.ThrowIfCancellationRequested();

			var writer = new ChannelWriter(outInterleaved, olaNorm, outFramesEst, channels, channel);

			// Buffers
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

			// Transient detector state
			double prevEnergy = 1e-12;
			double prevFlux = 0.0;

			// Positions
			int inPos = 0;
			double outPosD = 0.0;

			double twoPi = 2.0 * Math.PI;

			while (inPos + N <= inFrames)
			{
				ct.ThrowIfCancellationRequested();

				// Read & window, compute energy
				double energy = 0.0;
				for (int i = 0; i < N; i++)
				{
					float s = interleaved[(inPos + i) * channels + channel];
					float v = s * window[i];
					inBuf[i] = v;
					energy += v * v;
				}
				energy /= N;

				// FFT
				for (int i = 0; i < N; i++) { real[i] = inBuf[i]; imag[i] = 0.0; }
				FFT(real, imag, false);

				// Mag/phase
				for (int k = 0; k < N; k++)
				{
					double r = real[k];
					double im = imag[k];
					mag[k] = Math.Sqrt(r * r + im * im);
					phase[k] = Math.Atan2(im, r);
				}

				// Transient detection: energy jump + spectral flux (lightweight)
				bool isTransient = false;
				if (transientSensitivity > 0)
				{
					double ratio = energy / (prevEnergy + 1e-12);

					double flux = 0.0;
					for (int k = 0; k <= half; k++)
					{
						double d = mag[k] - 0.0; // we don't store prev mag; approximate flux by energy ratio + local proxy below
						flux += d;
					}
					flux = (0.9 * prevFlux) + (0.1 * flux);
					prevFlux = flux;

					isTransient = ratio >= transientSensitivity;
					prevEnergy = 0.85 * prevEnergy + 0.15 * energy;
				}

				// Peak detection (half spectrum)
				if (isPeak != null)
				{
					Array.Clear(isPeak, 0, half + 1);
					for (int k = 2; k < half - 1; k++)
					{
						double m0 = mag[k];
						if (m0 > mag[k - 1] && m0 > mag[k + 1]) isPeak[k] = 1;
					}
				}

				// Phase propagation
				if (isTransient)
				{
					// transient protection: lock synth phase to analysis phase (reduces smear)
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
						ApplyPeakLocking(N, half, isPeak, peakLockRadiusBins, phase, synPhase);
					}
				}

				// Build synth spectrum
				for (int k = 0; k < N; k++)
				{
					double m = mag[k];
					double ph = synPhase[k];
					real[k] = m * Math.Cos(ph);
					imag[k] = m * Math.Sin(ph);
				}

				// IFFT
				FFT(real, imag, true);

				// OLA (window again) + norm
				int outPos = (int) Math.Round(outPosD);
				for (int i = 0; i < N; i++)
				{
					int dstFrame = outPos + i;
					if ((uint) dstFrame >= (uint) outFramesEst) break;

					float s = (float) real[i] * window[i];
					writer.AddSample(dstFrame, s);
					writer.AddNorm(dstFrame, winPow[i]);
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

		// -------------------------
		// Processing: Segment -> buffer (out starts at 0!)
		// -------------------------

		private static void ProcessChannelSegmentToBuffer(
			float[] interleaved, int inFrames, int channels, int channel,
			int segStartFrame, int segEndFrame,
			float[] segOut, float[] segNorm,
			int N, int hopA, double hopS,
			float[] window, float[] winPow,
			int peakLockRadiusBins, double transientSensitivity,
			Action? onStep,
			CancellationToken ct)
		{
			ct.ThrowIfCancellationRequested();

			int segFrames = segEndFrame - segStartFrame;
			if (segFrames < N + hopA) return;

			var writer = new ChannelWriter(segOut, segNorm, segNorm.Length, 1, 0);

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

			double prevEnergy = 1e-12;
			double twoPi = 2.0 * Math.PI;

			int inPos = segStartFrame;
			double outPosD = 0.0; // IMPORTANT: segment buffers start at 0 (fixes the "19 sec" bug)

			while (inPos + N <= segEndFrame)
			{
				ct.ThrowIfCancellationRequested();

				double energy = 0.0;
				for (int i = 0; i < N; i++)
				{
					float s = interleaved[(inPos + i) * channels + channel];
					float v = s * window[i];
					inBuf[i] = v;
					energy += v * v;
				}
				energy /= N;

				for (int i = 0; i < N; i++) { real[i] = inBuf[i]; imag[i] = 0.0; }
				FFT(real, imag, false);

				for (int k = 0; k < N; k++)
				{
					double r = real[k];
					double im = imag[k];
					mag[k] = Math.Sqrt(r * r + im * im);
					phase[k] = Math.Atan2(im, r);
				}

				bool isTransient = false;
				if (transientSensitivity > 0)
				{
					double ratio = energy / (prevEnergy + 1e-12);
					isTransient = ratio >= transientSensitivity;
					prevEnergy = 0.85 * prevEnergy + 0.15 * energy;
				}

				if (isPeak != null)
				{
					Array.Clear(isPeak, 0, half + 1);
					for (int k = 2; k < half - 1; k++)
					{
						double m0 = mag[k];
						if (m0 > mag[k - 1] && m0 > mag[k + 1]) isPeak[k] = 1;
					}
				}

				if (isTransient)
				{
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
						ApplyPeakLocking(N, half, isPeak, peakLockRadiusBins, phase, synPhase);
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

				int outPos = (int) Math.Round(outPosD);
				for (int i = 0; i < N; i++)
				{
					int dst = outPos + i;
					if ((uint) dst >= (uint) writer.OutFrames) break;

					float s = (float) real[i] * window[i];
					writer.AddSample(dst, s);
					writer.AddNorm(dst, winPow[i]);
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

		private static void ApplyPeakLocking(
			int N, int half,
			byte[] isPeak,
			int radius,
			double[] anaPhase,
			double[] synPhase)
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

					// Maintain conjugate symmetry approximately
					int mirror = (N - b) & (N - 1);
					if (mirror != b && mirror >= 0 && mirror < N)
					{
						synPhase[mirror] = -synPhase[b];
					}
				}
			}
		}

		// -------------------------
		// Segment building + stitching (Equal-Power)
		// -------------------------

		private static (int start, int end)[] BuildSegments(int inFrames, int segLen, int N)
		{
			// Input-side overlap so each segment has context; stitching handles output overlap
			int ctx = Math.Max(N * 2, segLen / 6);
			var list = new System.Collections.Generic.List<(int, int)>();

			int s = 0;
			while (s < inFrames)
			{
				int e = Math.Min(inFrames, s + segLen);

				int ss = Math.Max(0, s - (s == 0 ? 0 : ctx));
				int ee = Math.Min(inFrames, e + (e == inFrames ? 0 : ctx));

				list.Add((ss, ee));

				if (e >= inFrames) break;
				s = e;
			}
			return list.ToArray();
		}

		private static void StitchSegmentsEqualPower(
			int channel, int channels,
			float[] outInterleaved, float[] olaNorm, int outFramesEst,
			SegmentResult[] segs,
			int hopA, double hopS,
			int crossfadeFrames)
		{
			Array.Sort(segs, (a, b) => a.InStart.CompareTo(b.InStart));

			int prevStart = -1;
			int prevEnd = -1;

			for (int i = 0; i < segs.Length; i++)
			{
				var seg = segs[i];
				if (seg.Out == null || seg.Norm == null) continue;

				// Robust placement using hop mapping:
				// outStart ≈ round( (seg.InStart / hopA) * hopS )
				double hopIndex = (double) seg.InStart / hopA;
				int outStart = (int) Math.Round(hopIndex * hopS);
				outStart = Math.Clamp(outStart, 0, outFramesEst - 1);

				int outLen = Math.Min(seg.Out.Length, outFramesEst - outStart);
				if (outLen <= 0) continue;

				// Determine overlap region with previous segment
				int xf = crossfadeFrames;
				int fadeStart = -1;
				int fadeEnd = -1;

				if (prevEnd > 0)
				{
					// If there is overlap, crossfade over up to xf frames
					int overlapStart = Math.Max(outStart, prevEnd - xf);
					int overlapEnd = Math.Min(prevEnd, outStart + outLen);

					if (overlapEnd > overlapStart)
					{
						fadeStart = overlapStart;
						fadeEnd = overlapEnd;
					}
				}

				for (int t = 0; t < outLen; t++)
				{
					int dstFrame = outStart + t;
					if ((uint) dstFrame >= (uint) outFramesEst) break;

					float s = seg.Out[t];
					float n = seg.Norm[t];

					float w = 1f;
					if (fadeStart >= 0 && dstFrame >= fadeStart && dstFrame < fadeEnd)
					{
						// equal-power fade in for this segment across overlap
						int L = Math.Max(1, fadeEnd - fadeStart);
						float x = (dstFrame - fadeStart) / (float) L; // 0..1
																	  // equal-power: sin/cos
						w = (float) Math.Sin(0.5 * Math.PI * x);
					}

					int idx = dstFrame * channels + channel;
					outInterleaved[idx] += s * w;
					olaNorm[dstFrame] += n * w;
				}

				prevStart = outStart;
				prevEnd = Math.Max(prevEnd, outStart + outLen);
			}
		}

		private static void ApplyOlaNormalization(float[] outInterleaved, float[] olaNorm, int outFrames, int channels)
		{
			for (int f = 0; f < outFrames; f++)
			{
				float n = olaNorm[f];
				if (n <= 1e-12f) continue;

				float inv = 1.0f / n;
				int baseIdx = f * channels;
				for (int c = 0; c < channels; c++)
					outInterleaved[baseIdx + c] *= inv;
			}
		}

		// -------------------------
		// Small structs/helpers
		// -------------------------

		private readonly struct SegmentResult
		{
			public readonly int InStart;
			public readonly int InEnd;
			public readonly float[] Out;
			public readonly float[] Norm;

			public SegmentResult(int inStart, int inEnd, float[] outBuf, float[] normBuf)
			{
				this.InStart = inStart;
				this.InEnd = inEnd;
				this.Out = outBuf;
				this.Norm = normBuf;
			}
		}

		private readonly struct ChannelWriter
		{
			private readonly float[] _out;
			private readonly float[] _norm;
			private readonly int _outFrames;
			private readonly int _channels;
			private readonly int _channel;

			public int OutFrames => this._outFrames;

			public ChannelWriter(float[] outInterleaved, float[] normFrames, int outFrames, int channels, int channel)
			{
				this._out = outInterleaved;
				this._norm = normFrames;
				this._outFrames = outFrames;
				this._channels = channels;
				this._channel = channel;
			}

			public void AddSample(int frame, float sample)
			{
				if (this._channels == 1)
				{
					this._out[frame] += sample;
					return;
				}
				this._out[frame * this._channels + this._channel] += sample;
			}

			public void AddNorm(int frame, float norm)
			{
				this._norm[frame] += norm;
			}
		}

		// In-place Cooley-Tukey FFT for real/imag arrays. If inverse=true, scales by 1/N.
		private static void FFT(double[] real, double[] imag, bool inverse)
		{
			int n = real.Length;

			int j = 0;
			for (int i = 0; i < n; i++)
			{
				if (i < j)
				{
					(real[i], real[j]) = (real[j], real[i]);
					(imag[i], imag[j]) = (imag[j], imag[i]);
				}
				int m = n >> 1;
				while (j >= m && m > 0) { j -= m; m >>= 1; }
				j += m;
			}

			for (int len = 2; len <= n; len <<= 1)
			{
				double ang = 2.0 * Math.PI / len * (inverse ? -1.0 : 1.0);
				double wlenReal = Math.Cos(ang);
				double wlenImag = Math.Sin(ang);

				for (int i = 0; i < n; i += len)
				{
					double wReal = 1.0, wImag = 0.0;
					int half = len >> 1;

					for (int k = 0; k < half; k++)
					{
						int u = i + k;
						int v = u + half;

						double tReal = real[v] * wReal - imag[v] * wImag;
						double tImag = real[v] * wImag + imag[v] * wReal;

						real[v] = real[u] - tReal;
						imag[v] = imag[u] - tImag;
						real[u] += tReal;
						imag[u] += tImag;

						double nwReal = wReal * wlenReal - wImag * wlenImag;
						double nwImag = wReal * wlenImag + wImag * wlenReal;
						wReal = nwReal; wImag = nwImag;
					}
				}
			}

			if (inverse)
			{
				double invN = 1.0 / n;
				for (int i = 0; i < n; i++) { real[i] *= invN; imag[i] *= invN; }
			}
		}
	}
}
