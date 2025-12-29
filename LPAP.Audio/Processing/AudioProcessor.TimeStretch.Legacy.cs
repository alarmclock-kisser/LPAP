using MathNet.Numerics.IntegralTransforms;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace LPAP.Audio.Processing
{
    public static partial class AudioProcessor
    {
        /// <summary>
        /// Thread-pool-freundliche Variante: nutzt einen eigenen, begrenzten Worker-Pool (dedicated threads),
        /// damit High-Priority Playback / UI nicht verhungern.
        /// maxWorkers: 0 => CPUCount, sonst clamp 1..CPUCount.
        /// progress: 0.0 .. 1.0
        ///
        /// NOTE: public API ohne async-keyword (Task-only). Core läuft intern async.
        /// </summary>
        public static Task<AudioObj> TimeStretchMostThreadsAsync(
            AudioObj obj,
            int chunkSize = 16384,
            float overlap = 0.5f,
            double factor = 1.0,
            bool keepData = false,
            float normalize = 1.0f,
            int maxWorkers = 0,
            IProgress<double>? progress = null,
            CancellationToken ct = default)
        {
            // Keine async Signatur – nur weiterreichen.
            return TimeStretchMostThreadsCoreAsync(obj, chunkSize, overlap, factor, keepData, normalize, maxWorkers, progress, ct);
        }

        private static async Task<AudioObj> TimeStretchMostThreadsCoreAsync(
            AudioObj obj,
            int chunkSize,
            float overlap,
            double factor,
            bool keepData,
            float normalize,
            int maxWorkers,
            IProgress<double>? progress,
            CancellationToken ct)
        {
            if (obj == null) throw new ArgumentNullException(nameof(obj));
            if (chunkSize <= 0) throw new ArgumentOutOfRangeException(nameof(chunkSize));
            if (overlap < 0f || overlap >= 1f) throw new ArgumentOutOfRangeException(nameof(overlap));
            if (factor <= 0) throw new ArgumentOutOfRangeException(nameof(factor));

            int cpuCount = Environment.ProcessorCount;
            int workers = maxWorkers <= 0 ? cpuCount : Math.Clamp(maxWorkers, 1, cpuCount);

            float[] backupData = obj.Data;
            int sampleRate = obj.SampleRate;
            int overlapSize = obj.OverlapSize;

            var sw = Stopwatch.StartNew();

            // Weights for global progress (sum = 1.0)
            const double wChunk = 0.05;
            const double wFft = 0.25;
            const double wStretch = 0.30;
            const double wIfft = 0.25;
            const double wAgg = 0.10;
            const double wNorm = 0.05;

            double pChunk = 0, pFft = 0, pStretch = 0, pIfft = 0, pAgg = 0, pNorm = 0;
            void Report()
            {
                double total =
                    pChunk * wChunk +
                    pFft * wFft +
                    pStretch * wStretch +
                    pIfft * wIfft +
                    pAgg * wAgg +
                    pNorm * wNorm;

                if (total < 0) total = 0;
                if (total > 1) total = 1;
                progress?.Report(total);
            }

            try
            {
                ct.ThrowIfCancellationRequested();

                // 1) Chunking
                var chunks = await obj.GetChunksAsync(chunkSize, overlap, keepData).ConfigureAwait(false);

                // wichtig: materialisieren, damit Count/Index billig ist
                if (chunks == null)
                {
                    obj.Data = backupData;
                    progress?.Report(1.0);
                    return obj;
                }

                var chunkList = chunks as IReadOnlyList<float[]> ?? (chunks is List<float[]> l ? l : new List<float[]>(chunks));
                int n = chunkList.Count;

                if (n == 0)
                {
                    obj.Data = backupData;
                    progress?.Report(1.0);
                    return obj;
                }

                pChunk = 1.0; Report();
                obj["chunk"] = sw.Elapsed.TotalMilliseconds;
                sw.Restart();

                // 2) FFT
                var fftChunks = new Complex[n][];
                using (var pool = new FixedWorkerPool(workers, ThreadPriority.BelowNormal, "TS-FFT"))
                {
                    int done = 0;
                    var tasks = new Task[n];

                    for (int i = 0; i < n; i++)
                    {
                        int idx = i;
                        tasks[i] = pool.Run(() =>
                        {
                            ct.ThrowIfCancellationRequested();
                            fftChunks[idx] = FourierTransformForward(chunkList[idx]);
                            int d = Interlocked.Increment(ref done);
                            pFft = (double) d / n;
                            Report();
                        }, ct);
                    }

                    await Task.WhenAll(tasks).ConfigureAwait(false);
                }

                obj["fft"] = sw.Elapsed.TotalMilliseconds;
                sw.Restart();

                // 3) Stretch
                var stretchChunks = new Complex[n][];
                using (var pool = new FixedWorkerPool(workers, ThreadPriority.BelowNormal, "TS-Stretch"))
                {
                    int done = 0;
                    var tasks = new Task[n];

                    for (int i = 0; i < n; i++)
                    {
                        int idx = i;
                        tasks[i] = pool.Run(() =>
                        {
                            ct.ThrowIfCancellationRequested();
                            stretchChunks[idx] = StretchChunk(fftChunks[idx], chunkSize, overlapSize, sampleRate, factor);
                            int d = Interlocked.Increment(ref done);
                            pStretch = (double) d / n;
                            Report();
                        }, ct);
                    }

                    await Task.WhenAll(tasks).ConfigureAwait(false);
                }

                obj["stretch"] = sw.Elapsed.TotalMilliseconds;
                sw.Restart();

                // 4) IFFT
                var ifftChunks = new float[n][];
                using (var pool = new FixedWorkerPool(workers, ThreadPriority.BelowNormal, "TS-IFFT"))
                {
                    int done = 0;
                    var tasks = new Task[n];

                    for (int i = 0; i < n; i++)
                    {
                        int idx = i;
                        tasks[i] = pool.Run(() =>
                        {
                            ct.ThrowIfCancellationRequested();
                            ifftChunks[idx] = FourierTransformInverse(stretchChunks[idx]);
                            int d = Interlocked.Increment(ref done);
                            pIfft = (double) d / n;
                            Report();
                        }, ct);
                    }

                    await Task.WhenAll(tasks).ConfigureAwait(false);
                }

                obj["ifft"] = sw.Elapsed.TotalMilliseconds;
                sw.Restart();

                // 5) Aggregate
                ct.ThrowIfCancellationRequested();
                await obj.AggregateStretchedChunksAsync(ifftChunks, factor).ConfigureAwait(false);
                pAgg = 1.0; Report();

                if (obj.Data.LongLength <= 0)
                {
                    obj.Data = backupData;
                    progress?.Report(1.0);
                    return obj;
                }

                obj["aggregate"] = sw.Elapsed.TotalMilliseconds;
                sw.Restart();

                // 6) Normalize
                if (normalize > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    await obj.NormalizeAsync(normalize).ConfigureAwait(false);
                }

                pNorm = 1.0; Report();
                obj["normalize"] = sw.Elapsed.TotalMilliseconds;

                obj.StretchFactor = factor;

                progress?.Report(1.0);
                return obj;
            }
            catch (OperationCanceledException)
            {
                obj.Data = backupData;
                throw;
            }
            catch
            {
                obj.Data = backupData;
                throw;
            }
        }

        private static Complex[] FourierTransformForward(float[] samples)
        {
            var complexSamples = new Complex[samples.Length];
            for (int i = 0; i < samples.Length; i++)
            {
                complexSamples[i] = new Complex(samples[i], 0.0);
            }

            Fourier.Forward(complexSamples, FourierOptions.Matlab);
            return complexSamples;
        }

        private static float[] FourierTransformInverse(Complex[] samples)
        {
            Fourier.Inverse(samples, FourierOptions.Matlab);

            var outSamples = new float[samples.Length];
            for (int i = 0; i < samples.Length; i++)
            {
                outSamples[i] = (float) samples[i].Real;
            }

            return outSamples;
        }

        private static Complex[] StretchChunk(Complex[] samples, int chunkSize, int overlapSize, int sampleRate, double factor)
        {
            int hopIn = chunkSize - overlapSize;
            int totalBins = chunkSize;
            int totalChunks = samples.Length / chunkSize;

            var output = new Complex[samples.Length];

            for (int chunk = 0; chunk < totalChunks; chunk++)
            {
                int chunkBase = chunk * chunkSize;
                int prevBase = (chunk > 0) ? (chunk - 1) * chunkSize : chunkBase;

                for (int bin = 0; bin < totalBins; bin++)
                {
                    int idx = chunkBase + bin;
                    int prevIdx = prevBase + bin;

                    if (chunk == 0)
                    {
                        output[idx] = samples[idx];
                        continue;
                    }

                    Complex cur = samples[idx];
                    Complex prev = samples[prevIdx];

                    float phaseCur = (float) Math.Atan2(cur.Imaginary, cur.Real);
                    float phasePrev = (float) Math.Atan2(prev.Imaginary, prev.Real);
                    float mag = (float) Math.Sqrt(cur.Real * cur.Real + cur.Imaginary * cur.Imaginary);

                    float freqPerBin = (float) sampleRate / chunkSize;
                    float expectedPhaseAdv = 2.0f * (float) Math.PI * freqPerBin * bin * hopIn / sampleRate;

                    float deltaPhase = phaseCur - phasePrev;
                    float delta = deltaPhase - expectedPhaseAdv;

                    delta = (float) ((delta + Math.PI) % (2.0f * Math.PI) - Math.PI);

                    float phaseOut = phasePrev + expectedPhaseAdv + (float) (delta * factor);

                    output[idx] = new Complex(mag * Math.Cos(phaseOut), mag * Math.Sin(phaseOut));
                }
            }

            return output;
        }

        private sealed class FixedWorkerPool : IDisposable
        {
            private readonly BlockingCollection<WorkItem> _queue = new();
            private readonly Thread[] _threads;

            private readonly struct WorkItem
            {
                public readonly Action Action;
                public readonly TaskCompletionSource<bool> Tcs;
                public readonly CancellationToken Ct;

                public WorkItem(Action action, TaskCompletionSource<bool> tcs, CancellationToken ct)
                {
                    Action = action;
                    Tcs = tcs;
                    Ct = ct;
                }
            }

            public FixedWorkerPool(int workerCount, ThreadPriority threadPriority, string threadNamePrefix)
            {
                if (workerCount <= 0) throw new ArgumentOutOfRangeException(nameof(workerCount));

                _threads = new Thread[workerCount];
                for (int i = 0; i < workerCount; i++)
                {
                    int idx = i;
                    _threads[i] = new Thread(WorkerLoop)
                    {
                        IsBackground = true,
                        Priority = threadPriority,
                        Name = $"{threadNamePrefix}-{idx}"
                    };
                    _threads[i].Start();
                }
            }

            public Task Run(Action action, CancellationToken ct)
            {
                if (action == null) throw new ArgumentNullException(nameof(action));

                var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

                if (ct.IsCancellationRequested)
                {
                    tcs.TrySetCanceled(ct);
                    return tcs.Task;
                }

                _queue.Add(new WorkItem(action, tcs, ct));
                return tcs.Task;
            }

            private void WorkerLoop()
            {
                foreach (var item in _queue.GetConsumingEnumerable())
                {
                    if (item.Ct.IsCancellationRequested)
                    {
                        item.Tcs.TrySetCanceled(item.Ct);
                        continue;
                    }

                    try
                    {
                        item.Action();
                        item.Tcs.TrySetResult(true);
                    }
                    catch (OperationCanceledException oce)
                    {
                        item.Tcs.TrySetCanceled(oce.CancellationToken);
                    }
                    catch (Exception ex)
                    {
                        item.Tcs.TrySetException(ex);
                    }
                }
            }

            public void Dispose()
            {
                _queue.CompleteAdding();
                foreach (var t in _threads)
                {
                    try { t.Join(); } catch { /* ignore */ }
                }
                _queue.Dispose();
            }
        }
    }
}
