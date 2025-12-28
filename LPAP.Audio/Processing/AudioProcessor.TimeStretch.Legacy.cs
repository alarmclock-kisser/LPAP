using MathNet.Numerics.IntegralTransforms;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace LPAP.Audio.Processing
{
    public static partial class AudioProcessor
    {
        /// <summary>
        /// Legacy PPFFT time stretcher (ported from CpuExecutioner) with strict worker limiting.
        /// NOTE: This method is NOT async (no await/async keyword) but returns Task&lt;AudioObj&gt; like your other stretchers.
        /// maxWorkers: 0 = use all threads (Environment.ProcessorCount). Values &lt; 0 treated as 0. Values &gt; CPU count are clamped.
        /// </summary>
        public static Task<AudioObj> TimeStretchParallel_V0_Legacy(
            AudioObj obj,
            double factor = 1.0,
            int chunkSize = 16384,
            float overlap = 0.5f,
            bool keepData = false,
            float normalize = 1.0f,
            int maxWorkers = 0,
            IProgress<double>? progress = null)
        {
            // Keep method non-async but still non-blocking for UI: do the work on one background task.
            return Task.Run(() =>
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

                // Validate/normalize params
                overlap = Math.Clamp(overlap, 0.05f, 0.95f);
                factor = Math.Clamp(factor, 0.05, 20.0);

                int cpu = Math.Max(1, Environment.ProcessorCount);
                if (maxWorkers < 0) maxWorkers = 0;

                // 0 => all threads
                int workers = (maxWorkers == 0)
                    ? cpu
                    : Math.Clamp(maxWorkers, 1, cpu);

                float[] backupData = obj.Data;
                int sampleRate = obj.SampleRate;
                int overlapSize = obj.OverlapSize;

                try
                {
                    // Chunking (repo schema; sync wait)
                    var chunkEnumerable = obj.GetChunksAsync(chunkSize, overlap, keepData, workers)
                        .GetAwaiter().GetResult();

                    var chunks = chunkEnumerable as IList<float[]> ?? chunkEnumerable.ToList();
                    if (chunks.Count == 0)
                    {
                        obj.Data = backupData;
                        return obj;
                    }

                    var tracker = CreateTracker(progress, chunks.Count, includeNormalize: normalize > 0);
                    tracker?.ReportWork(chunks.Count); // chunking stage

                    // Stage 1: FFT (worker-limited)
                    Complex[][] fftChunks = new Complex[chunks.Count][];
                    {
                        var po = new ParallelOptions { MaxDegreeOfParallelism = workers };
                        Parallel.For(0, chunks.Count, po, i =>
                        {
                            fftChunks[i] = FourierTransformForward_Sync(chunks[i]);
                            tracker?.ReportWork(1);
                        });
                    }

                    // Stage 2: Stretch in frequency domain (worker-limited)
                    Complex[][] stretchChunks = new Complex[fftChunks.Length][];
                    {
                        var po = new ParallelOptions { MaxDegreeOfParallelism = workers };
                        Parallel.For(0, fftChunks.Length, po, i =>
                        {
                            stretchChunks[i] = StretchChunk_Sync(fftChunks[i], chunkSize, overlapSize, sampleRate, factor);
                            tracker?.ReportWork(1);
                        });
                    }

                    // Stage 3: IFFT (worker-limited)
                    float[][] ifftChunks = new float[stretchChunks.Length][];
                    {
                        var po = new ParallelOptions { MaxDegreeOfParallelism = workers };
                        Parallel.For(0, stretchChunks.Length, po, i =>
                        {
                            ifftChunks[i] = FourierTransformInverse_Sync(stretchChunks[i]);
                            tracker?.ReportWork(1);
                        });
                    }

                    // Stage 4: Aggregate back (repo schema; sync wait)
                    obj.StretchFactor = factor;
                    obj.AggregateStretchedChunksAsync(ifftChunks, factor, workers)
                        .GetAwaiter().GetResult();
                    tracker?.ReportWork(chunks.Count);

                    if (obj.Data == null || obj.Data.LongLength <= 0)
                    {
                        obj.Data = backupData;
                        return obj;
                    }

                    // BPM adjust (match your other stretchers)
                    if (obj.BeatsPerMinute > 0.0f && factor > 0.0)
                    {
                        obj.BeatsPerMinute = (float) (obj.BeatsPerMinute / factor);
                    }

                    // Stage 5: Normalize (optional; repo schema; sync wait)
                    if (normalize > 0.0f)
                    {
                        obj.NormalizeAsync(normalize, workers)
                            .GetAwaiter().GetResult();
                        tracker?.ReportWork(chunks.Count);
                    }

                    tracker?.Complete();
                    progress?.Report(1.0);

                    return obj;
                }
                catch
                {
                    // safety: restore original data if anything explodes
                    obj.Data = backupData;
                    throw;
                }
            });
        }

        // -----------------------------------------------------------------------------------------
        // Legacy helpers (SYNC on purpose so Parallel.For controls concurrency)
        // -----------------------------------------------------------------------------------------

        private static Complex[] FourierTransformForward_Sync(float[] samples)
        {
            var complexSamples = new Complex[samples.Length];
            for (int i = 0; i < samples.Length; i++)
            {
                complexSamples[i] = new Complex(samples[i], 0.0);
            }

            Fourier.Forward(complexSamples, FourierOptions.Matlab);
            return complexSamples;
        }

        private static float[] FourierTransformInverse_Sync(Complex[] samples)
        {
            Fourier.Inverse(samples, FourierOptions.Matlab);

            var dst = new float[samples.Length];
            for (int i = 0; i < samples.Length; i++)
            {
                dst[i] = (float) samples[i].Real;
            }

            return dst;
        }

        private static Complex[] StretchChunk_Sync(Complex[] samples, int chunkSize, int overlapSize, int sampleRate, double factor)
        {
            // Direct port of CpuExecutioner.StretchChunkAsync algorithm (but sync).
            int hopIn = chunkSize - overlapSize;
            _ = (int) (hopIn * factor + 0.5); // hopOut computed in old code but not used in its loop

            int totalBins = chunkSize;
            int totalChunks = samples.Length / chunkSize;

            var output = new Complex[samples.Length];

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

            return output;
        }
    }
}
