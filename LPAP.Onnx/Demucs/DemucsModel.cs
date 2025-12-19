using LPAP.Onnx.Demucs;
using LPAP.Onnx.Runtime;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace LPAP.Demucs
{
    public sealed class DemucsModel : IDisposable
    {
        public InferenceSession Session { get; }

        public string InputName { get; }
        public string OutputName { get; }

        // Fixed input length in frames (T) the model expects, e.g. 343980.
        // 0 means: unknown -> try infer from input metadata; if still 0 caller must choose a fallback.
        public int FixedInputFrames { get; private set; } = 0;

        // How many stems we return, and how many channels the model expects.
        public int StemsWanted { get; }
        public int ChannelsWanted { get; }

        // Options used to create / interpret the model.
        public DemucsOptions Options { get; }
        public OnnxOptions OnnxOptions { get; }

        public DemucsModel(DemucsOptions demucsOptions, OnnxOptions onnxOptions)
        {
            Options = demucsOptions ?? throw new ArgumentNullException(nameof(demucsOptions));
            OnnxOptions = onnxOptions ?? throw new ArgumentNullException(nameof(onnxOptions));

            Session = CreateSessionInternal(Options, OnnxOptions);

            // Names: prefer explicit options, else take first metadata key.
            InputName = !string.IsNullOrWhiteSpace(Options.InputName)
                ? Options.InputName!
                : Session.InputMetadata.Keys.First();

            OutputName = ResolveOutputName(Session, Options);

            // Defaults for the rest of the pipeline.
            ChannelsWanted = Options.ExpectedChannels > 0 ? Options.ExpectedChannels : 2;
            StemsWanted = Options.StemCount > 0 ? Options.StemCount : 4;

            FixedInputFrames = Options.FixedInputFrames > 0 ? Options.FixedInputFrames : 0;
            InitializeFromSessionMetadata();
        }

        public DemucsModel(InferenceSession session, DemucsOptions demucsOptions, OnnxOptions onnxOptions)
        {
            Session = session ?? throw new ArgumentNullException(nameof(session));
            Options = demucsOptions ?? throw new ArgumentNullException(nameof(demucsOptions));
            OnnxOptions = onnxOptions ?? throw new ArgumentNullException(nameof(onnxOptions));

            InputName = !string.IsNullOrWhiteSpace(Options.InputName)
                ? Options.InputName!
                : Session.InputMetadata.Keys.First();

            OutputName = ResolveOutputName(Session, Options);

            ChannelsWanted = Options.ExpectedChannels > 0 ? Options.ExpectedChannels : 2;
            StemsWanted = Options.StemCount > 0 ? Options.StemCount : 4;

            FixedInputFrames = Options.FixedInputFrames > 0 ? Options.FixedInputFrames : 0;
            InitializeFromSessionMetadata();
        }

        private static InferenceSession CreateSessionInternal(DemucsOptions demucsOptions, OnnxOptions onnxOptions)
        {
            var path = DemucsOptions.ResolveModelPath(demucsOptions.ModelPath);
            var so = OnnxSessionFactory.CreateSessionOptions(onnxOptions);
            return new InferenceSession(path, so);
        }

        private static string ResolveOutputName(InferenceSession session, DemucsOptions options)
        {
            if (!string.IsNullOrWhiteSpace(options.OutputName))
            {
                return options.OutputName!;
            }

            string? waveformCandidate = null;
            string? fallback = null;

            foreach (KeyValuePair<string, NodeMetadata> entry in session.OutputMetadata)
            {
                fallback ??= entry.Key;

                var dims = entry.Value.Dimensions;
                if (dims is null || dims.Length == 0)
                {
                    continue;
                }

                if (LooksLikeWaveformOutput(dims, options))
                {
                    return entry.Key;
                }

                if (dims.Length is 3 or 4)
                {
                    waveformCandidate ??= entry.Key;
                }
            }

            return waveformCandidate ?? fallback ?? session.OutputMetadata.Keys.First();
        }

        private static bool LooksLikeWaveformOutput(IReadOnlyList<int> dims, DemucsOptions options)
        {
            if (dims.Count == 4)
            {
                bool stemsMatch = dims[1] <= 0 || dims[1] == options.StemCount;
                bool channelsMatch = dims[2] <= 0 || dims[2] == options.ExpectedChannels;
                return stemsMatch && channelsMatch;
            }

            if (dims.Count == 3)
            {
                bool stemsMatch = dims[0] <= 0 || dims[0] == options.StemCount;
                bool channelsMatch = dims[1] <= 0 || dims[1] == options.ExpectedChannels;
                return stemsMatch && channelsMatch;
            }

            return false;
        }

        private void InitializeFromSessionMetadata()
        {
            // Infer FixedInputFrames from input shape if not configured.
            // Expecting input: [1, C, T] where T can be fixed or dynamic (-1).
            if (FixedInputFrames == 0)
            {
                try
                {
                    var first = Session.InputMetadata.FirstOrDefault();
                    if (!string.IsNullOrWhiteSpace(first.Key))
                    {
                        var dims = first.Value.Dimensions;
                        if (dims is { Length: >= 3 })
                        {
                            long t = dims[2];
                            if (t > 0 && t <= int.MaxValue)
                            {
                                FixedInputFrames = (int) t;
                            }
                        }
                    }
                }
                catch
                {
                    // Ignore metadata issues; caller will fall back.
                }
            }
        }

        public void Dispose() => Session.Dispose();

        public Task<float[][]> SeparateAsync(
            ReadOnlyMemory<float> interleavedInput,
            int sampleRate,
            int channels,
            IProgress<float>? progress = null,
            CancellationToken ct = default)
        {
            // ORT Run is sync; keep UI thread clean.
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
                throw new ArgumentOutOfRangeException(nameof(channels));

            if (channels != ChannelsWanted)
                throw new InvalidOperationException($"DemucsModel: channels={channels} but model configured for ChannelsWanted={ChannelsWanted}.");

            if (FixedInputFrames <= 0)
                throw new InvalidOperationException("DemucsModel: FixedInputFrames is 0. Configure DemucsOptions.FixedInputFrames or use a model with fixed T in metadata.");

            // 1) Build planar input [1, C, T]
            float[] inputPlanar = BuildPlanarInputFixedT(interleavedInput.Span, channels, FixedInputFrames, out float inputPeak);

            var inputTensor = new DenseTensor<float>(inputPlanar, new[] { 1, channels, FixedInputFrames });
            List<NamedOnnxValue> modelInputs = BuildModelInputs(inputTensor, inputPlanar, channels);

            try
            {
                using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results = Session.Run(modelInputs);

                DisposableNamedOnnxValue? outVal = results.FirstOrDefault(r => string.Equals(r.Name, OutputName, StringComparison.Ordinal));
                outVal ??= results.First();

                Tensor<float> t = outVal.AsTensor<float>();
                int[] dims = GetDimsArray(t);

                (float mnRaw, float mxRaw) = ApproxMinMax(t);
                Console.WriteLine($"Demucs: Raw out name='{outVal.Name}', dims={string.Join("x", dims)}, len={t.Length}, min={mnRaw:0.0000}, max={mxRaw:0.0000}");

                // 3) Convert to planar waveform [S, C, T]
                float[] planar = ConvertToWaveformPlanar(t, dims, StemsWanted, channels, FixedInputFrames, Options);

                // 4) Optional: DC removal
                RemoveDcOffsetInPlace(planar, StemsWanted, channels, FixedInputFrames);

                // 5) Split planar -> interleaved stems
                float[][] stems = new float[StemsWanted][];
                for (int s = 0; s < StemsWanted; s++)
                {
                    stems[s] = PlanarStemToInterleaved(planar, s, StemsWanted, channels, FixedInputFrames);
                    (float mn, float mx) = ApproxMinMax(stems[s]);
                    Console.WriteLine($"Demucs: Stem[{s}] interleaved len={stems[s].Length}, min={mn:0.0000}, max={mx:0.0000}");
                }

                return stems;
            }
            finally
            {
                
            }
        }

        private static float[] ConvertToWaveformPlanar(Tensor<float> tf, int[] dims, int stemsWanted, int channelsWanted, int targetFrames, DemucsOptions options)
        {
            // Accept common waveform outputs:
            // [S, C, T] or [1, S, C, T] or 5D complex TF (your case).
            if (dims.Length == 4 && dims[0] == 1)
            {
                // [1, S, C, T]
                int S = dims[1], C = dims[2], T = dims[3];
                if (T <= 0) throw new InvalidOperationException("Demucs: invalid T.");
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
                // Example: [1, 6, 4, 2048, 336] => complex TF that needs ISTFT.
                int B = dims[0], Src = dims[1], ChLike = dims[2], FreqOrWin = dims[3], Frames = dims[4];

                Console.WriteLine($"Demucs: Output is not waveform tensor. dims={string.Join("x", dims)}. Attempting 5D ISTFT reconstruction.");

                if (B != 1)
                    Console.WriteLine($"Demucs: 5D decode expects batch=1, got {B}. Using batch 0 only.");

                if (ChLike == 4 && FreqOrWin > 0)
                {
                    return Convert5DComplexTfToWaveformPlanar_Istft(tf, Src, ChLike, FreqOrWin, Frames, stemsWanted, channelsWanted, targetFrames, options);
                }

                // Fallback (not your case, but safe)
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
                        planar[dstBase + i] = tf[0, srcS, srcC, i];
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
                        planar[dstBase + i] = tf[srcS, srcC, i];
                }
            }

            return planar;
        }

        // dims [1, Src, 4, NFFT, Frames], 4=(L.re,L.im,R.re,R.im)
        private static float[] Convert5DComplexTfToWaveformPlanar_Istft(
            Tensor<float> tf,
            int src,
            int chLike,
            int freqBins,
            int frameCount,
            int stemsWanted,
            int channelsWanted,
            int targetFrames,
            DemucsOptions options)
        {
            if (channelsWanted != 2)
                throw new InvalidOperationException("ISTFT decoder currently expects stereo (channelsWanted=2).");

            if (chLike < 4)
                throw new InvalidOperationException($"Demucs: Expected at least 4 planes for complex stereo output, but received {chLike}.");

            int configuredFft = Math.Max(0, options?.HybridStftFftSize ?? 0);
            int nFft = configuredFft > 0 ? configuredFft : freqBins;
            if (nFft < freqBins)
            {
                nFft = NextPowerOfTwo(freqBins);
            }

            bool treatAsOneSided = freqBins <= (nFft / 2) + 1;
            int hop = DeriveHopLength(frameCount, nFft, targetFrames, options?.HybridStftHopLength ?? 0);
            int olaLen = Math.Max(targetFrames, (frameCount - 1) * hop + nFft);
            var window = BuildHannWindow(nFft);

            var planar = new float[stemsWanted * channelsWanted * targetFrames];

            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1)
            };

            Exception? parallelError = null;

            Parallel.For(0, stemsWanted * channelsWanted, parallelOptions, (workIndex, state) =>
            {
                try
                {
                    int stemIndex = workIndex / channelsWanted;
                    int channelIndex = workIndex % channelsWanted;

                    int srcStem = Math.Min(stemIndex, src - 1);
                    int stereoIdx = Math.Min(channelIndex, 1);
                    int reIdx = stereoIdx == 0 ? 0 : 2;
                    int imIdx = stereoIdx == 0 ? 1 : 3;

                    var spec = new Complex[nFft];
                    var time = new Complex[nFft];
                    var ola = new float[olaLen];
                    var norm = new float[olaLen];

                    for (int frame = 0; frame < frameCount; frame++)
                    {
                        int outOffset = frame * hop;

                        FillSpectrumSlice(tf, spec, nFft, freqBins, treatAsOneSided, srcStem, reIdx, imIdx, frame);

                        Array.Copy(spec, time, nFft);
                        Fft.InverseInPlace(time);

                        for (int n = 0; n < nFft; n++)
                        {
                            float w = window[n];
                            float v = (float) time[n].Real;

                            int pos = outOffset + n;
                            if ((uint) pos >= (uint) olaLen)
                            {
                                continue;
                            }

                            float weighted = v * w;
                            ola[pos] += weighted;
                            norm[pos] += w * w;
                        }
                    }

                    for (int i = 0; i < olaLen; i++)
                    {
                        float d = norm[i];
                        if (d > 1e-9f)
                        {
                            ola[i] /= d;
                        }
                    }

                    int srcStart = 0;
                    if ((options?.HybridStftCenter ?? true) && olaLen > targetFrames)
                    {
                        srcStart = (olaLen - targetFrames) / 2;
                    }

                    int dstBase = (stemIndex * channelsWanted + channelIndex) * targetFrames;
                    int copy = Math.Min(targetFrames, Math.Max(0, olaLen - srcStart));
                    if (copy > 0)
                    {
                        Array.Copy(ola, srcStart, planar, dstBase, copy);
                    }

                    if (copy < targetFrames)
                    {
                        Array.Clear(planar, dstBase + copy, targetFrames - copy);
                    }
                }
                catch (Exception ex)
                {
                    parallelError = ex;
                    state.Stop();
                }
            });

            if (parallelError is not null)
            {
                throw parallelError;
            }

            (float mn, float mx) = ApproxMinMax(planar);
            Console.WriteLine($"Demucs: ISTFT planar len={planar.Length}, min={mn:0.000000}, max={mx:0.000000} (nFft={nFft}, hop={hop}, frames={frameCount}, target={targetFrames})");

            return planar;
        }

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
                            if ((uint) pos >= (uint) olaLen) continue;

                            ola[pos] += v * w;
                            norm[pos] += w * w;
                        }
                    }

                    for (int i = 0; i < olaLen; i++)
                    {
                        float d = norm[i];
                        if (d > 1e-12f) ola[i] /= d;
                    }

                    int srcStart = 0;
                    if (olaLen > targetFrames)
                        srcStart = (olaLen - targetFrames) / 2;

                    int dstBase = (s * channelsWanted + c) * targetFrames;
                    int copy = Math.Min(targetFrames, olaLen - srcStart);
                    Array.Copy(ola, srcStart, planar, dstBase, copy);
                }
            }

            return planar;
        }

        private static void FillSpectrumSlice(
            Tensor<float> tf,
            Complex[] spec,
            int nFft,
            int freqBins,
            bool onesided,
            int stemIndex,
            int reIdx,
            int imIdx,
            int frame)
        {
            Array.Clear(spec, 0, spec.Length);

            if (!onesided)
            {
                int copy = Math.Min(freqBins, nFft);
                for (int k = 0; k < copy; k++)
                {
                    float re = tf[0, stemIndex, reIdx, k, frame];
                    float im = tf[0, stemIndex, imIdx, k, frame];
                    spec[k] = new Complex(re, im);
                }
                return;
            }

            int positiveBins = Math.Min(freqBins, (nFft / 2) + 1);
            for (int k = 0; k < positiveBins; k++)
            {
                float re = tf[0, stemIndex, reIdx, k, frame];
                float im = tf[0, stemIndex, imIdx, k, frame];
                spec[k] = new Complex(re, im);
            }

            for (int k = 1; k < positiveBins - 1 && k < nFft; k++)
            {
                spec[nFft - k] = Complex.Conjugate(spec[k]);
            }
        }

        private static int DeriveHopLength(int frameCount, int nFft, int targetFrames, int configuredHop)
        {
            if (configuredHop > 0)
            {
                return configuredHop;
            }

            if (frameCount > 1 && targetFrames > 0)
            {
                int derived = (int) Math.Round((double) Math.Max(targetFrames - nFft, 0) / Math.Max(frameCount - 1, 1));
                if (derived > 0)
                {
                    return derived;
                }
            }

            return Math.Max(1, nFft / 4);
        }

        private static int NextPowerOfTwo(int value)
        {
            if (value <= 0)
            {
                return 1;
            }

            value--;
            value |= value >> 1;
            value |= value >> 2;
            value |= value >> 4;
            value |= value >> 8;
            value |= value >> 16;
            value++;
            return value;
        }
 
         private static float[] BuildPlanarInputFixedT(ReadOnlySpan<float> interleaved, int channels, int fixedT, out float inputPeak)
         {
            int framesIn = interleaved.Length / channels;
            if (framesIn <= 0)
                throw new InvalidOperationException("No samples.");

            inputPeak = 0f;
            int scanStep = Math.Max(1, interleaved.Length / 131072);
            for (int i = 0; i < interleaved.Length; i += scanStep)
            {
                float a = MathF.Abs(interleaved[i]);
                if (a > inputPeak) inputPeak = a;
            }
            if (inputPeak <= 0f) inputPeak = 1f;

            float scale = (inputPeak > 1.2f) ? (1f / inputPeak) : 1f;

            var planar = new float[channels * fixedT];

            int copyFrames = Math.Min(framesIn, fixedT);
            int srcStart = 0;
            if (framesIn > fixedT)
                srcStart = (framesIn - fixedT) / 2;

            for (int t = 0; t < copyFrames; t++)
            {
                int srcFrame = srcStart + t;
                int srcBase = srcFrame * channels;

                for (int c = 0; c < channels; c++)
                    planar[c * fixedT + t] = interleaved[srcBase + c] * scale;
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
                        sum += planar[baseIdx + i];

                    float mean = (float) (sum / frames);

                    if (MathF.Abs(mean) < 1e-6f)
                        continue;

                    for (int i = 0; i < frames; i++)
                        planar[baseIdx + i] -= mean;
                }
            }
        }

        private static float[] BuildHannWindow(int n)
        {
            var w = new float[n];
            if (n <= 1)
            {
                if (n == 1) w[0] = 1f;
                return w;
            }

            for (int i = 0; i < n; i++)
                w[i] = 0.5f - 0.5f * MathF.Cos(2f * MathF.PI * i / (n - 1));

            return w;
        }

        private static int[] GetDimsArray(Tensor<float> tf)
        {
            var dims = tf.Dimensions;
            var arr = new int[dims.Length];
            for (int i = 0; i < arr.Length; i++)
                arr[i] = dims[i];
            return arr;
        }

        private static (float min, float max) ApproxMinMax(Tensor<float> tf)
        {
            float min = float.MaxValue;
            float max = float.MinValue;

            long step = Math.Max(1, tf.Length / 16384);
            int idx = 0;

            foreach (var v in tf)
            {
                if ((idx++ % step) != 0) continue;
                if (v < min) min = v;
                if (v > max) max = v;
            }

            if (min == float.MaxValue) min = 0;
            if (max == float.MinValue) max = 0;
            return (min, max);
        }

        private static (float min, float max) ApproxMinMax(float[] data)
        {
            float min = float.MaxValue, max = float.MinValue;
            int step = Math.Max(1, data.Length / 16384);
            for (int i = 0; i < data.Length; i += step)
            {
                float v = data[i];
                if (v < min) min = v;
                if (v > max) max = v;
            }
            if (min == float.MaxValue) min = 0;
            if (max == float.MinValue) max = 0;
            return (min, max);
        }

        private static bool IsPowerOfTwo(int n) => n > 0 && (n & (n - 1)) == 0;

        private static class Fft
        {
            public static void ForwardInPlace(Complex[] buffer) => Transform(buffer, inverse: false);
            public static void InverseInPlace(Complex[] buffer) => Transform(buffer, inverse: true);

            private static void Transform(Complex[] buffer, bool inverse)
            {
                int n = buffer.Length;
                if (n == 1) return;
                if ((n & (n - 1)) != 0)
                    throw new ArgumentException("FFT size must be power of two.");

                BitReverse(buffer);

                for (int len = 2; len <= n; len <<= 1)
                {
                    double ang = (inverse ? +2.0 : -2.0) * Math.PI / len;
                    Complex wLen = new(Math.Cos(ang), Math.Sin(ang));

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

                if (inverse)
                {
                    double invN = 1.0 / n;
                    for (int i = 0; i < n; i++)
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
                    while ((j & bit) != 0) j ^= bit;
                    j ^= bit;

                    if (i < j)
                        (a[i], a[j]) = (a[j], a[i]);
                }
            }
        }

        private List<NamedOnnxValue> BuildModelInputs(
            DenseTensor<float> waveformTensor,
            float[] waveformPlanar,
            int channels)
        {
            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor(InputName, waveformTensor)
            };

            foreach (KeyValuePair<string, NodeMetadata> entry in Session.InputMetadata)
            {
                if (string.Equals(entry.Key, InputName, StringComparison.Ordinal))
                    continue;

                if (entry.Value.ElementType != typeof(float))
                    throw new InvalidOperationException($"DemucsModel: Input '{entry.Key}' vom Typ '{entry.Value.ElementType}' wird nicht unterstützt.");

                Tensor<float> supplemental = CreateSupplementalInputTensor(entry.Key, entry.Value, waveformPlanar, channels);
                inputs.Add(NamedOnnxValue.CreateFromTensor(entry.Key, supplemental));
            }

            return inputs;
        }

        private Tensor<float> CreateSupplementalInputTensor(
            string inputName,
            NodeMetadata metadata,
            float[] waveformPlanar,
            int channels)
        {
            if (string.Equals(inputName, "onnx::ReduceMean_1", StringComparison.Ordinal))
                return BuildHybridSpectrogramInput(metadata, waveformPlanar, channels);

            throw new InvalidOperationException($"DemucsModel: Für das zusätzliche Eingabe-Tensor '{inputName}' existiert keine Adapter-Implementierung.");
        }

        private DenseTensor<float> BuildHybridSpectrogramInput(
            NodeMetadata metadata,
            float[] waveformPlanar,
            int channels)
        {
            int[] dims = metadata.Dimensions ?? throw new InvalidOperationException("DemucsModel: Zusatz-Eingabe hat keine statischen Dimensionen.");
            if (dims.Length != 4 || dims[0] != 1 || dims[1] != 4)
                throw new InvalidOperationException($"DemucsModel: Erwartete STFT-Dims [1,4,nFft,Frames], erhalten [{string.Join(",", dims)}].");

            if (channels != 2 || !IsPowerOfTwo(dims[2]) || dims[3] <= 0)
                throw new InvalidOperationException("DemucsModel: STFT-Adapter benötigt Stereo und eine 2er-Potenz für nFft.");

            int nFft = dims[2];
            int frames = dims[3];
            int hop = nFft / 2;

            var tensor = new DenseTensor<float>(new[] { 1, 4, nFft, frames });
            float[] window = BuildHannWindow(nFft);
            var buffer = new Complex[nFft];

            for (int frame = 0; frame < frames; frame++)
            {
                int frameOffset = frame * hop;

                FillStftBuffer(waveformPlanar, channel: 0, frameOffset, nFft, window, buffer);
                Fft.ForwardInPlace(buffer);
                for (int k = 0; k < nFft; k++)
                {
                    tensor[0, 0, k, frame] = (float) buffer[k].Real;
                    tensor[0, 1, k, frame] = (float) buffer[k].Imaginary;
                }

                FillStftBuffer(waveformPlanar, channel: 1, frameOffset, nFft, window, buffer);
                Fft.ForwardInPlace(buffer);
                for (int k = 0; k < nFft; k++)
                {
                    tensor[0, 2, k, frame] = (float) buffer[k].Real;
                    tensor[0, 3, k, frame] = (float) buffer[k].Imaginary;
                }
            }

            return tensor;
        }

        private void FillStftBuffer(
            float[] planar,
            int channel,
            int frameOffset,
            int nFft,
            float[] window,
            Complex[] buffer)
        {
            int channelBase = channel * FixedInputFrames;

            for (int i = 0; i < nFft; i++)
            {
                int sampleIndex = frameOffset + i;
                float sample = (uint) sampleIndex < (uint) FixedInputFrames
                    ? planar[channelBase + sampleIndex]
                    : 0f;

                buffer[i] = new Complex(sample * window[i], 0d);
            }
        }
    }


}
