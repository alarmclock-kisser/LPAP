using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace LPAP.Onnx.Demucs
{
	public sealed class DemucsService
	{
		private readonly DemucsModel _model;
		public DemucsModel Model => this._model;

		private readonly Lock _logLock = new();
		private readonly SynchronizationContext? _sync;
		public readonly BindingList<string> LogLines = [];
		public int MaxLogLines { get; set; } = 4000;
		public bool EnableTimingLogs { get; set; } = true;

		public DemucsOptions Options { get; }

		public DemucsService(DemucsModel model)
			: this(model, model?.Options ?? new DemucsOptions())
		{
		}

		public DemucsService(DemucsModel model, DemucsOptions options)
		{
			this._model = model ?? throw new ArgumentNullException(nameof(model));
			this.Options = options ?? throw new ArgumentNullException(nameof(options));
			this._sync = SynchronizationContext.Current;

			// Init-Logging: sofort nach Service-Erstellung
			try
			{
				this.Log($"Init: Demucs model loaded. ModelPath='{this.Options.ModelPath}', Input='{this._model.InputName}', Output='{this._model.OutputName}'");
				this.Log($"Init: Expected SR={this.Options.ExpectedSampleRate}, Channels={this._model.ChannelsWanted}, Stems={this._model.StemsWanted}, FixedT={(this._model.FixedInputFrames > 0 ? this._model.FixedInputFrames.ToString() : "dynamic")}");
				this.Log($"Init: ONNX runtime PreferCuda={this._model.OnnxOptions.PreferCuda}, DeviceId={this._model.OnnxOptions.DeviceId}, Workers={this._model.OnnxOptions.WorkerCount}");
			}
			catch { }
		}



		public void Log(string msg)
		{
			string line = $"[{DateTime.Now:HH:mm:ss.fff}] {msg}";
			void add()
			{
				lock (this._logLock)
				{
					this.LogLines.Add(line);
					while (this.LogLines.Count > this.MaxLogLines)
						this.LogLines.RemoveAt(0);
				}
			}

			if (this._sync != null) this._sync.Post(_ => add(), null);
			else add();
		}

		private IDisposable TimeScope(string name)
		{
			if (!this.EnableTimingLogs) return DummyDisposable.Instance;
			var sw = Stopwatch.StartNew();
			this.Log($"▶ {name}...");
			return new ActionDisposable(() =>
			{
				sw.Stop();
				this.Log($"✓ {name} in {sw.Elapsed.TotalMilliseconds:0.0} ms");
			});
		}

		private sealed class ActionDisposable(Action a) : IDisposable { public void Dispose() => a(); }
		private sealed class DummyDisposable : IDisposable { public static readonly DummyDisposable Instance = new(); public void Dispose() { } }

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
			if (channels != 2)
				throw new InvalidOperationException("This implementation expects stereo input.");

			int totalFrames = inputInterleaved.Length / channels;
			if (totalFrames <= 0)
				return Array.Empty<float[]>();

			int segT =
				this._model.FixedInputFrames <= 0
					? (this.Options.FixedInputFrames > 0
						? this.Options.FixedInputFrames
						: Math.Max(1, (int) Math.Round(sampleRate * 6.0)))
					: this._model.FixedInputFrames;

			if (segT <= 0)
			{
				throw new InvalidOperationException("Model segment length (FixedInputFrames) is unknown/invalid.");
			}

			// 50% overlap (good default)
			int overlap = segT / 2;
			int step = segT - overlap;
			if (step <= 0)
			{
				step = segT;
			}

			// Output buffers [stem][interleaved]
			int stems = this._model.StemsWanted;
			var outStems = new float[stems][];
			for (int s = 0; s < stems; s++)
			{
				outStems[s] = new float[totalFrames * channels];
			}

			// Weight sum for proper overlap-add normalization (per output sample)
			var weightSum = new float[totalFrames * channels];

			// Window for stitching in time domain (not the ISTFT window)
			var w = BuildHann(segT);

			// Build explicit segment start positions to ensure consistent counting
			var starts = new System.Collections.Generic.List<int>();
			if (totalFrames <= segT)
			{
				starts.Add(0);
			}
			else
			{
				for (int start = 0; start + segT <= totalFrames; start += step)
				{
					starts.Add(start);
				}
				// Ensure tail coverage if remainder exists and not already aligned
				int lastStart = starts.Count > 0 ? starts[^1] : 0;
				int desiredTailStart = Math.Max(0, totalFrames - segT);
				if (desiredTailStart > lastStart)
				{
					starts.Add(desiredTailStart);
				}
			}

			int segCount = starts.Count;
			int segIndex = 0;
			this.Log($"Demucs: starting separation (SR={sampleRate}, CH={channels}, Frames={totalFrames}, SegT={segT}, Overlap={overlap}, Segments={segCount})");

			foreach (int startFrame in starts)
			{
				ct.ThrowIfCancellationRequested();
				int framesThis = Math.Min(segT, totalFrames - startFrame);
				var seg = new float[segT * channels];
				var src = inputInterleaved.Span;

				for (int t = 0; t < framesThis; t++)
				{
					int srcBase = (startFrame + t) * channels;
					int dstBase = t * channels;
					seg[dstBase + 0] = src[srcBase + 0];
					seg[dstBase + 1] = src[srcBase + 1];
				}

				// Throttle segment logs to avoid flooding the UI
				if (segIndex % 50 == 0)
					this.Log($"Segment {segIndex + 1}/{segCount} (startFrame={startFrame}, framesThis={framesThis})");

				float[][] segStems = await this._model
					.SeparateAsync(seg, sampleRate, channels, progress: null, ct)
					.ConfigureAwait(false);

				// Accumulate weights for this segment once (same for all stems)
				for (int t = 0; t < framesThis; t++)
				{
					float ww = w[t];
					int dstBase = (startFrame + t) * channels;

					weightSum[dstBase + 0] += ww;
					weightSum[dstBase + 1] += ww;
				}

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
				progress?.Report(segIndex / (float)segCount);
			}

			// Normalize by accumulated window weights (proper overlap-add)
			// NOTE: weightSum is identical for all stems, so we normalize each stem with the same divisor.
			for (int i = 0; i < weightSum.Length; i++)
			{
				float wsum = weightSum[i];
				if (wsum > 1e-6f)
				{
					float inv = 1f / wsum;
					for (int s = 0; s < stems; s++)
					{
						outStems[s][i] *= inv;
					}
				}
				else
				{
					// Optional safety: if no weight accumulated (shouldn't happen except maybe degenerate cases),
					// force silence to avoid NaNs/garbage.
					for (int s = 0; s < stems; s++)
					{
						outStems[s][i] = 0f;
					}
				}
			}

			this.Log($"Demucs: separation finished. Processed {segIndex} segment(s)");
			return outStems;
		}


		private static float[] BuildHann(int n)
		{
			var w = new float[n];
			if (n <= 1) { if (n == 1) { w[0] = 1f; } return w; }
			for (int i = 0; i < n; i++)
			{
				w[i] = 0.5f - 0.5f * MathF.Cos(2f * MathF.PI * i / (n - 1));
			}

			return w;
		}
	}
}
