using LPAP.Audio;
using ManagedCuda;
using ManagedCuda.BasicTypes;
using ManagedCuda.VectorTypes;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using static ManagedCuda.DriverAPINativeMethods;

namespace LPAP.Cuda
{
    public class CudaService
    {
        private readonly Lock _initLock = new();

        private GpuStats? _gpuStats;

        public string KernelsPath { get; internal set; } = "";
        public int DeviceIndex { get; private set; } = -1;
        public Dictionary<CUdevice, string> AvailableDevices { get; private set; } = [];

        internal PrimaryContext? Context { get; private set; }
        internal CudaRegister? Register { get; private set; }
        internal CudaFourier? Fourier { get; private set; }
        internal CudaCompiler? Compiler { get; private set; }
        internal CudaLauncher? Launcher { get; private set; }

        public bool Initialized => this.Context != null && this.Register != null && this.Compiler != null && this.Launcher != null;

        public bool CompileAllOnInitialize { get; set; } = true;
        public bool LogCompilationOutput { get; set; }

        public CudaService(string deviceName = "RTX")
        {
            CudaLog.Info("Initializing CudaService...");
            this.KernelsPath = ResolveRepoKernelsPathOrFallback();
            this.KernelsPath = this.PrepareKernelDirectory(this.KernelsPath); // ensures CU/PTX/Logs exist
            CudaLog.Info("Using KernelsPath", this.KernelsPath);
            this.ConfigureLogging();
            this.AvailableDevices = this.GetAvailableDevices();

            CudaLog.Info($"Available CUDA devices: {this.AvailableDevices.Count}");

            if (this.AvailableDevices.Count == 0 || string.IsNullOrWhiteSpace(deviceName))
            {
                CudaLog.Warn("No devices available or device name is empty.");
                return;
            }

            if (!this.Initialize(deviceName))
            {
                CudaLog.Warn("CUDA device auto-initialization skipped", deviceName);
            }
        }

        public bool Initialize(int index)
        {
            lock (this._initLock)
            {
                if (this.AvailableDevices.Count == 0)
                {
                    this.AvailableDevices = this.GetAvailableDevices();
                    CudaLog.Info($"Refreshed available devices: {this.AvailableDevices.Count}");
                }

                if (index < 0 || index >= this.AvailableDevices.Count)
                {
                    CudaLog.Warn("CUDA device index out of range", index.ToString());
                    return false;
                }

                if (this.Initialized && this.DeviceIndex == index)
                {
                    CudaLog.Info("Device already initialized.");
                    return true;
                }

                this.ResetRuntime();

                var selected = this.AvailableDevices.ElementAt(index);
                try
                {
                    CudaLog.Info($"Initializing device at index {index}: {selected.Value}");
                    this.Context = new PrimaryContext(selected.Key);
                    this.Context.SetCurrent();

                    this.Register = new CudaRegister(this.Context);
                    this.Fourier = new CudaFourier(this.Context, this.Register);
                    this.Compiler = new CudaCompiler(this.Context, this.KernelsPath, this.CompileAllOnInitialize, this.LogCompilationOutput);
                    this.Launcher = new CudaLauncher(this.Context, this.Register, this.Fourier, this.Compiler);

                    this.DeviceIndex = index;

                    try
                    {
                        this._gpuStats?.Dispose();
                        this._gpuStats = new GpuStats(index);
                    }
                    catch (Exception ex)
                    {
                        CudaLog.Warn("Failed to init GpuStats", ex.Message);
                        this._gpuStats = null;
                    }

                    CudaLog.Info("Initialized CUDA device", selected.Value);
                    return true;
                }
                catch (Exception ex)
                {
                    CudaLog.Error("Failed to initialize CUDA runtime", ex.Message);
                    this.ResetRuntime();
                    return false;
                }
            }
        }

        public bool Initialize(string deviceName)
        {
            if (string.IsNullOrWhiteSpace(deviceName))
            {
                CudaLog.Warn("Device name is empty or null.");
                return false;
            }

            var index = this.GetDeviceIndex(deviceName);
            if (!index.HasValue)
            {
                CudaLog.Warn("CUDA device not found", deviceName);
                return false;
            }

            return this.Initialize(index.Value);
        }

        public Dictionary<CUdevice, string> GetAvailableDevices()
        {
            Dictionary<CUdevice, string> devices = [];
            try
            {
                int count = CudaContext.GetDeviceCount();
                CudaLog.Info($"Detected {count} CUDA devices.");
                for (int i = 0; i < count; i++)
                {
                    devices[new CUdevice(i)] = CudaContext.GetDeviceName(i);
                }

                if (devices.Count == 0)
                {
                    CudaLog.Warn("No CUDA capable devices detected");
                }
            }
            catch (Exception ex)
            {
                CudaLog.Error("Failed to enumerate CUDA devices", ex.Message);
            }

            this.AvailableDevices = devices;
            return devices;
        }

        public int? GetDeviceIndex(string deviceName)
        {
            if (string.IsNullOrWhiteSpace(deviceName) || this.AvailableDevices.Count == 0)
            {
                return null;
            }

            int idx = 0;
            foreach (var kvp in this.AvailableDevices)
            {
                if (kvp.Value.Contains(deviceName, StringComparison.OrdinalIgnoreCase))
                {
                    return idx;
                }

                idx++;
            }

            return null;
        }

        public void Dispose()
        {
            lock (this._initLock)
            {
                this.ResetRuntime();
            }
        }

        private string PrepareKernelDirectory(string? candidate)
        {
            string target = string.IsNullOrWhiteSpace(candidate)
                ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Kernels")
                : candidate;

            try
            {
                target = Path.GetFullPath(target);
                Directory.CreateDirectory(target);
                Directory.CreateDirectory(Path.Combine(target, "CU"));
                Directory.CreateDirectory(Path.Combine(target, "PTX"));
                Directory.CreateDirectory(Path.Combine(target, "Logs"));
            }
            catch (Exception ex)
            {
                CudaLog.Error("Failed to prepare kernel directories", ex.Message);
            }

            return target;
        }

        private void ConfigureLogging()
        {
            try
            {
                string logDir = Path.Combine(this.KernelsPath, "Logs");
                Directory.CreateDirectory(logDir);

                if (string.IsNullOrWhiteSpace(CudaLog.LogFilePath))
                {
                    string logFile = Path.Combine(logDir, $"AsynCuda13_{DateTime.Now:yyyyMMdd_HHmmss}.log");
                    File.WriteAllText(logFile, $"AsynCuda13 runtime log ({DateTime.Now:O}){Environment.NewLine}");
                    CudaLog.LogFilePath = logFile;
                }
            }
            catch (Exception ex)
            {
                CudaLog.Warn("Failed to configure CUDA logging", ex.Message);
            }
        }

        private void ResetRuntime()
        {
            if (this.Launcher != null)
            {
                try
                {
                    this.Launcher.Dispose();
                }
                catch (Exception ex)
                {
                    CudaLog.Warn("Failed to dispose launcher", ex.Message);
                }

                this.Launcher = null;
            }

            if (this.Compiler != null)
            {
                try
                {
                    this.Compiler.Dispose();
                }
                catch (Exception ex)
                {
                    CudaLog.Warn("Failed to dispose compiler", ex.Message);
                }

                this.Compiler = null;
            }

            if (this.Fourier != null)
            {
                try
                {
                    this.Fourier.Dispose();
                }
                catch (Exception ex)
                {
                    CudaLog.Warn("Failed to dispose Fourier helper", ex.Message);
                }

                this.Fourier = null;
            }

            if (this.Register != null)
            {
                try
                {
                    this.Register.Dispose();
                }
                catch (Exception ex)
                {
                    CudaLog.Warn("Failed to dispose register", ex.Message);
                }

                this.Register = null;
            }

            if (this.Context != null)
            {
                try
                {
                    this.Context.Dispose();
                }
                catch (Exception ex)
                {
                    CudaLog.Warn("Failed to dispose CUDA context", ex.Message);
                }

                this.Context = null;
            }

            if (this._gpuStats != null)
            {
                try { this._gpuStats.Dispose(); }
                catch (Exception ex) { CudaLog.Warn("Failed to dispose GpuStats", ex.Message); }
                this._gpuStats = null;
            }

            this.DeviceIndex = -1;
        }


        public GpuStats.ProcessingSession? GetLastGpuProcessingSessionOrDefault()
        {
            return this._gpuStats?.LastSessionOrDefault();
        }

        public IReadOnlyList<GpuStats.ProcessingSession> GetGpuProcessingSessionsSnapshot()
        {
            var gs = this._gpuStats;
            if (gs is null)
            {
                return [];
            }

            // ProcessingSessions ist eine List, daher Snapshot zurückgeben
            lock (gs.ProcessingSessions)
            {
                return gs.ProcessingSessions.ToArray();
            }
        }





        // UI
        public IEnumerable<IntPtr> GetPointersAllocated()
        {
            if (this.Register == null)
            {
                return [];
            }

            return this.Register.Memory.Select(m => m.IndexPointer);
        }

        private static string ResolveRepoKernelsPathOrFallback()
        {
            try
            {
                // 1) Preferred: 4x up from exe folder -> LPAP.Cuda\Kernels
                string exeDir = AppDomain.CurrentDomain.BaseDirectory;
                var di = new DirectoryInfo(exeDir);

                for (int i = 0; i < 4 && di.Parent != null; i++)
                {
                    di = di.Parent;
                }

                string candidate = Path.Combine(di.FullName, "LPAP.Cuda", "Kernels");
                if (Directory.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }

                // 2) Walk upwards and search for "LPAP.Cuda\Kernels"
                di = new DirectoryInfo(exeDir);
                while (di != null)
                {
                    candidate = Path.Combine(di.FullName, "LPAP.Cuda", "Kernels");
                    if (Directory.Exists(candidate))
                    {
                        return Path.GetFullPath(candidate);
                    }

                    di = di.Parent;
                }
            }
            catch
            {
                // ignore
            }

            // 3) Fallback (old behavior)
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Kernels");
        }

        public IEnumerable<string> GetKernels(string? filter = null, bool showUncompiled = false, bool filePaths = false)
        {
            if (this.Compiler == null)
            {
                return [];
            }

            List<string> files = showUncompiled
                ? this.Compiler.GetCuFiles()
                : this.Compiler.GetPtxFiles();

            if (files.Count == 0)
            {
                return [];
            }

            // Apply filter BEFORE returning and apply it to the "display name" as well.
            if (!string.IsNullOrWhiteSpace(filter))
            {
                files = files
                    .Where(f =>
                    {
                        // filter against both full path and kernel name
                        var name = Path.GetFileNameWithoutExtension(f) ?? string.Empty;
                        return (f?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false) ||
                               name.Contains(filter, StringComparison.OrdinalIgnoreCase);
                    })
                    .ToList();
            }

            if (filePaths)
            {
                // Nulls → string.Empty
                return files.Select(f => f ?? string.Empty).ToList();
            }

            return files
                .Select(f => Path.GetFileNameWithoutExtension(f) ?? string.Empty)
                .Where(n => n.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public Dictionary<string, Type>? GetKernelArguments(string? kernelName)
        {
            if (this.Compiler == null || string.IsNullOrWhiteSpace(kernelName))
            {
                return null;
            }

            return this.Compiler.GetArguments(kernelName);
        }

        public long GetMemoryInBytes(VramStats vramStat = VramStats.Total)
        {
            long vram = 0;
            if (this.Register == null)
            {
                return vram;
            }

            vram = vramStat switch
            {
                VramStats.Total => this.Register.GetTotalMemory(),
                VramStats.Free => this.Register.GetTotalFreeMemory(),
                VramStats.Used => this.Register.GetTotalMemory() - this.Register.GetTotalFreeMemory(),
                _ => 0,
            };

            return vram;
        }

        public Task<double?> GetGpuLoadInPercentAsync(int? deviceIndex = null)
        {
            deviceIndex ??= this.DeviceIndex;

            if (deviceIndex.Value < 0)
            {
                return Task.FromResult<double?>(null);
            }

            // Prefer running monitor instance
            if (this._gpuStats != null && this.DeviceIndex == deviceIndex.Value)
            {
                double pct = this._gpuStats.CurrentLoad01 * 100.0;
                return Task.FromResult<double?>(pct);
            }

            // Fallback: create a short-lived sampler
            try
            {
                using var tmp = new GpuStats(deviceIndex.Value);
                return Task.Run(async () =>
                {
                    await Task.Delay(120).ConfigureAwait(false);
                    return (double?) (tmp.CurrentLoad01 * 100.0);
                });
            }
            catch (Exception ex)
            {
                CudaLog.Warn("Failed to get GPU load", ex.Message);
                return Task.FromResult<double?>(null);
            }
        }

        public IEnumerable<string> GetDeviceInfo(bool identifier = true)
        {
            List<string> identifiers =
                [
                    "Device ID: ",
                    "Device Name: ",
                    "Compute Capability: ",
                    "Total Memory (MB): ",
                    "Multiprocessor Count: ",
                    "Clock Rate (MHz): ",
                    "Memory Clock Rate (MHz): ",
                    "Memory Bus Width (bits): ",
                    "Cache Size (KB): ",
                    "Shared Memory per Block (KB): ",
                    "Warp Size: ",
                    "Max Threads per Block: ",
                    "Max Threads Dimension: ",
                    "Max Grid Size: "
                ];

            List<string> info = [];
            if (this.Context == null)
            {
                return identifiers.Select(id => id + "N/A").ToList();
            }

            var devProps = this.Context.GetDeviceInfo();
            info.Add(this.Context.DeviceId.ToString());
            info.Add(this.Context.GetDeviceName());
            info.Add($"{devProps.ComputeCapability.Major}.{devProps.ComputeCapability.Minor}");
            info.Add((devProps.TotalGlobalMemory / (1024 * 1024)).ToString());
            info.Add(devProps.MultiProcessorCount.ToString());
            info.Add((devProps.ClockRate / 1000).ToString());
            info.Add((devProps.MemoryClockRate / 1000).ToString());
            info.Add(devProps.GlobalMemoryBusWidth.ToString());
            info.Add((devProps.L2CacheSize / 1024).ToString());
            info.Add((devProps.SharedMemoryPerBlock / 1024).ToString());
            info.Add(devProps.WarpSize.ToString());
            info.Add(devProps.MaxThreadsPerBlock.ToString());
            info.Add($"{devProps.MaxBlockDim.x}, {devProps.MaxBlockDim.y}, {devProps.MaxBlockDim.z}");
            info.Add($"{devProps.MaxGridDim.x}, {devProps.MaxGridDim.y}, {devProps.MaxGridDim.z}");

            if (identifier)
            {
                return identifiers.Zip(info, (id, val) => id + val).ToList();
            }
            else
            {
                return info;
            }
        }

        public string GetKernelExecutionType(string kernelName)
        {
            // In-Place, Out-of-Place, GetValue, GetData
            if (string.IsNullOrWhiteSpace(kernelName) || this.Compiler == null)
            {
                return "Unknown";
            }

            try
            {
                var args = this.Compiler.GetArguments(kernelName);
                if (args == null || args.Count == 0)
                {
                    return "Unknown";
                }

                int ptrCount = args.Values.Count(t => t.IsPointer);
                if (ptrCount <= 0)
                {
                    return "Unknown";
                }

                if (ptrCount == 1)
                {
                    return "In-Place";
                }

                // Heuristics: If there are more than 2 pointers, assume OutBuffer variant
                if (ptrCount > 2)
                {
                    // Prefer GetData when more than 2 pointers
                    return "GetData";
                }

                // If pointer base types differ, it's likely an OutBuffer (GetData/Value)
                var pointerBaseTypes = args.Values.Where(t => t.IsPointer).Select(t => t.GetElementType()).Distinct().ToList();
                if (pointerBaseTypes.Count > 1)
                {
                    // Try to distinguish value vs data via name hints
                    string name = kernelName.ToLowerInvariant();
                    if (name.Contains("value") || name.Contains("scalar") || name.Contains("stat"))
                    {
                        return "GetValue";
                    }
                    return "GetData";
                }

                return "Out-of-Place";
            }
            catch
            {
                return "Unknown";
            }
        }

        public bool GetKernelNeedFourierTransform(string kernelName)
        {
            // In-Place, Out-of-Place, GetValue, GetData
            if (string.IsNullOrWhiteSpace(kernelName) || this.Compiler == null)
            {
                return false;
            }

            try
            {
                var args = this.Compiler.GetArguments(kernelName);
                if (args == null || args.Count == 0)
                {
                    return false;
                }

                // Sammle Pointer-Argumente in stabiler Reihenfolge (Dictionary behält Insertion-Order)
                var ptrArgs = args
                    .Where(kv => kv.Value?.IsPointer == true)
                    .ToList();

                if (ptrArgs.Count <= 0)
                {
                    return false;
                }

                // Wähle bevorzugt den Eingabe-Pointer anhand des Namens, sonst nimm den ersten
                static bool LooksLikeInput(string name)
                {
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        return false;
                    }

                    name = name.ToLowerInvariant();
                    return name.Contains("in") || name.Contains("input") || name.Contains("src");
                }

                var firstPtr = ptrArgs.FirstOrDefault(kv => LooksLikeInput(kv.Key));
                if (EqualityComparer<KeyValuePair<string, Type>>.Default.Equals(firstPtr, default))
                {
                    firstPtr = ptrArgs[0];
                }

                // Prüfe auf float2*
                var elemType = firstPtr.Value.GetElementType();
                if (elemType == typeof(ManagedCuda.VectorTypes.float2))
                {
                    return true;
                }

                // Optional: Wenn nicht eindeutig, prüfe alle Pointer-Args (falls Kernel ausschließlich im Frequenzbereich arbeitet)
                if (ptrArgs.Any(kv => kv.Value.GetElementType() == typeof(ManagedCuda.VectorTypes.float2)))
                {
                    return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        public object GetDefaultArgValue(Type type, string name, AudioObj? audio)
        {
            object val = 0;

            if (type == typeof(int))
            {
                // Length
                if (name.Contains("len", StringComparison.OrdinalIgnoreCase) || name.Contains("count", StringComparison.OrdinalIgnoreCase))
                {
                    val = audio?.LengthSamples ?? 0;
                }
                else if (name.Contains("chunk"))
                {
                    val = audio?.ChunkSize ?? 1024;
                }
                else if (name.Contains("sample") || name.Contains("rate", StringComparison.OrdinalIgnoreCase))
                {
                    val = audio?.Channels ?? 44100;
                }
                else if (name.Contains("cha"))
                {
                    val = audio?.Channels ?? 1;
                }
            }
            if (type == typeof(long))
            {
                // Length
                if (name.Contains("len", StringComparison.OrdinalIgnoreCase) || name.Contains("count", StringComparison.OrdinalIgnoreCase))
                {
                    val = audio?.LengthSamples ?? 0;
                }
            }
            if (type == typeof(float))
            {
                // Length
                if (name.Contains("bpm", StringComparison.OrdinalIgnoreCase) || name.Contains("beats", StringComparison.OrdinalIgnoreCase))
                {
                    val = (float) (audio?.BeatsPerMinute ?? 120);
                }
                if (name.Contains("amp", StringComparison.OrdinalIgnoreCase))
                {
                    val = 1.0f;
                }
            }
            if (type == typeof(double))
            {
                if (name.Contains("factor", StringComparison.OrdinalIgnoreCase) || name.Contains("stretch", StringComparison.OrdinalIgnoreCase))
                {
                    val = audio?.StretchFactor ?? 1.0;
                }
            }

            return val;
        }


        public async Task<IntPtr?> ExecuteCufftAsync(AudioObj audio, int chunkSize = 0, float overlap = 0f, bool asMany = false, IProgress<double>? progress = null)
        {
            // Verify CUDA online
            if (!this.Initialized || this.Register == null || this.Fourier == null || this.Context == null)
            {
                CudaLog.Warn("CUDA not initialized! CUFFT execution aborted.");
                return null;
            }

            Stopwatch sw = Stopwatch.StartNew();
            // If already in frequency domain, IFFT + Pull
            if (audio.Form == "c" && audio.Pointer != IntPtr.Zero)
            {
                var ifftPtr = asMany ? await this.Fourier.PerformIfftManyAsync(audio.Pointer, false, progress) : await this.Fourier.PerformIfftAsync(audio.Pointer, false, progress);
                audio.Pointer = ifftPtr;
                audio.Form = "f";
                audio["__cudaIfft_ms"] = sw.Elapsed.TotalMilliseconds;

                var mem = this.Register[ifftPtr];
                if (mem == null)
                {
                    CudaLog.Error("Failed to perform IFFT on audio data.");
                    return null;
                }

                if (mem.ElementType == typeof(float))
                {
                    if (mem.Count == 1)
                    {
                        audio.Data = await this.Register.PullDataAsync<float>(ifftPtr);
                    }
                    else
                    {
                        var chunks = await this.Register.PullChunksAsync<float>(ifftPtr);
                        await audio.AggregateStretchedChunksAsync(chunks);
                    }
                    ifftPtr = IntPtr.Zero;
                    await audio.NormalizeAsync(1);
                }
                else
                {
                    CudaLog.Error("IFFT output memory type is not float.");
                    return null;
                }

                CudaLog.Info("Performed IFFT on audio data.");

                return ifftPtr;
            }
            else if (audio.Form == "f")
            {
                CudaMem? mem = null;
                if (audio.Pointer == IntPtr.Zero && audio.Data.LongLength > 0)
                {
                    if (chunkSize <= 0)
                    {
                        mem = await this.Register.PushDataAsync(audio.Data);
                        audio.Data = [];
                    }
                    else
                    {
                        var chunks = await audio.GetChunksAsync(chunkSize, overlap, 0, true);
                        mem = await this.Register.PushChunksAsync(chunks);
                    }
                }
                else if (audio.Pointer != IntPtr.Zero)
                {
                    mem = this.Register[audio.Pointer];
                }
                if (mem == null)
                {
                    CudaLog.Error("Failed to allocate memory for audio data.");
                    return null;
                }

                var fftPtr = asMany ? await this.Fourier.PerformFftManyAsync(mem.IndexPointer, false, progress) : await this.Fourier.PerformFftAsync(mem.IndexPointer, false, progress);
                audio.Pointer = fftPtr;
                audio.Form = "c";
                audio["__cudaFft_ms"] = sw.Elapsed.TotalMilliseconds;

                mem = this.Register[fftPtr];
                if (mem == null)
                {
                    CudaLog.Error("Failed to perform FFT on audio data.");
                    return null;
                }

                CudaLog.Info("Performed FFT on audio data.");

                return fftPtr;
            }
            else
            {
                CudaLog.Warn("AudioObj is neither in time nor frequency domain. CUFFT execution aborted.");
                return null;
            }
        }


        public async Task<AudioObj?> ExecuteAudioKernelAutoAsync(
    AudioObj audio,
    string kernelName,
    int chunkSize = 0,
    float overlap = 0f,
    double? stretchFactor = null,
    Dictionary<string, object>? arguments = null,
    bool workingCopy = false,
    IProgress<double>? progress = null,
    CancellationToken ct = default)
        {
            // Verify CUDA online
            if (!this.Initialized || this.Register == null || this.Compiler == null || this.Launcher == null || this.Context == null || this.Fourier == null)
            {
                CudaLog.Warn("CUDA not initialized! Kernel execution aborted.", kernelName);
                return null;
            }

            // Ensure context on this thread (important when called from threadpool)
            this.Context.SetCurrent();

            // Verify kernel args
            var argDefs = this.Compiler.GetArguments(kernelName);
            if (argDefs == null || argDefs.Count == 0)
            {
                CudaLog.Warn("Kernel argument parsing failed (no args).", kernelName);
                return null;
            }

            audio = workingCopy ? await audio.CloneAsync(true, ct).ConfigureAwait(false) : audio;

            // Local helper: run an in-place float kernel (float* data, int n) over all chunks and sync.
            async Task<bool> RunFloatInplacePerChunkKernelAsync(
                string kName,
                CudaMem m,
                IProgress<double>? localProgress,
                double phaseStart,
                double phaseSpan)
            {
                // Must be float time-domain
                if (m.ElementType != typeof(float))
                {
                    CudaLog.Warn($"Skip '{kName}' because mem.ElementType is {m.ElementType.Name} (expected float).", kernelName);
                    return false;
                }

                var k = this.Compiler.LoadKernel(kName);
                if (k == null)
                {
                    CudaLog.Warn($"Optional kernel '{kName}' not found/failed to load. Skipping.", kernelName);
                    return false;
                }

                var stream = this.Register.GetStream();
                if (stream == null)
                {
                    CudaLog.Warn($"Failed to get CUDA stream for '{kName}'. Skipping.", kernelName);
                    return false;
                }

                int total = m.DevicePointers.Length;
                for (int i = 0; i < total; i++)
                {
                    ct.ThrowIfCancellationRequested();

                    var inPtr = m.DevicePointers[i];

                    // kernel args: (float* data, int n)
                    // Provide scalar "n" by name.
                    var argStrings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["n"] = m.Lengths[i].ToInt64().ToString(System.Globalization.CultureInfo.InvariantCulture),
                    };

                    object[] mergedArgs = this.Launcher.MergeGenericKernelArgumentsDynamic(
                        kName,
                        inputBuffer: inPtr,
                        outputBuffer: null,
                        backfeedBuffer: null,
                        arguments: argStrings);

                    if (mergedArgs.Length == 0)
                    {
                        continue;
                    }

                    Configure1D(k, m.Lengths[i].ToInt64());
                    k.RunAsync(stream.Stream, mergedArgs);

                    if (localProgress != null)
                    {
                        double pLinear = (i + 1) / (double) total;
                        localProgress.Report(phaseStart + Math.Clamp(pLinear, 0.0, 1.0) * phaseSpan);
                    }
                }

                // Wait until done (sync on same thread to keep context)
                stream.Synchronize();
                return true;
            }

            // 1) Chunking + Push
            CudaMem? mem = null;
            var sw = Stopwatch.StartNew();
            long originalSamplesCount = audio.LengthSamples;

            if (chunkSize <= 0)
            {
                mem = await this.Register.PushDataAsync(audio.Data).ConfigureAwait(false);
                if (!workingCopy)
                {
                    audio.Data = [];
                }
            }
            else
            {
                var chunks = await audio.GetChunksAsync(chunkSize, overlap, 0, workingCopy).ConfigureAwait(false);
                mem = await this.Register.PushChunksAsync(chunks).ConfigureAwait(false);
            }

            if (mem == null)
            {
                CudaLog.Error("Failed to allocate memory for audio data.", kernelName);
                return null;
            }

            audio["__cudaPush_ms"] = sw.Elapsed.TotalMilliseconds;
            CudaLog.Info("Pushed audio data to GPU. (" + sw.Elapsed.TotalMilliseconds.ToString("F1") + " ms)", kernelName);
            sw.Stop();

            // 2) Optionally CuFFT (+ Windowing before FFT)
            bool didFft = false;
            if (this.GetKernelNeedFourierTransform(kernelName))
            {
                // Phase 1: 0..0.33
                // split Phase 1:
                // - pre-window: 0..0.08
                // - FFT:        0.08..0.33
                IProgress<double>? phase1Progress = progress != null
                    ? new Progress<double>(p => progress.Report(Math.Clamp(p, 0.0, 0.33)))
                    : null;

                // Pre-window only makes sense in time-domain float before FFT (and only for chunked or any overlap usage).
                // If single buffer (no chunking), it still helps (Hann on full signal), but you probably want it for chunking.
                if (mem.ElementType == typeof(float))
                {
                    await RunFloatInplacePerChunkKernelAsync(
                        kName: "window_sqrthann_01",
                        m: mem,
                        localProgress: phase1Progress,
                        phaseStart: 0.0,
                        phaseSpan: 0.08).ConfigureAwait(false);
                }

                // FFT progress mapped into 0.08..0.33
                IProgress<double>? fftProgress = progress != null
                    ? new Progress<double>(p => progress.Report(0.08 + Math.Clamp(p, 0.0, 1.0) * (0.33 - 0.08)))
                    : null;

                sw.Restart();
                var fftPtr = await this.Fourier.PerformFftManyAsync(mem.IndexPointer, false, fftProgress).ConfigureAwait(false);
                mem = this.Register[fftPtr];

                if (mem == null)
                {
                    CudaLog.Error("Failed to perform FFT on audio data.", kernelName);
                    return null;
                }

                sw.Stop();
                audio["__cudaFft_ms"] = sw.Elapsed.TotalMilliseconds;
                CudaLog.Info("Performed FFT on audio data. (" + sw.Elapsed.TotalMilliseconds.ToString("F1") + " ms)", kernelName);
                didFft = true;
            }

            // 2.1) Verify mem Type is suitable for kernel's first pointer arg
            var ptrArgs = argDefs
                .Select(kv => (Name: kv.Key, Type: kv.Value))
                .Where(t => t.Type.IsPointer)
                .ToList();

            if (ptrArgs.Count == 0)
            {
                CudaLog.Error("Kernel has no pointer arguments; cannot bind audio data.", kernelName);
                return null;
            }

            // input element type must match mem.ElementType
            var inElemType = ptrArgs[0].Type.GetElementType();
            if (inElemType != mem.ElementType)
            {
                CudaLog.Error($"Kernel input pointer type mismatch: {inElemType?.FullName} /=/ {mem.ElementType?.FullName}", kernelName);
                return null;
            }

            // Heuristik Backfeed:
            // - 3 Pointer => definitiv Backfeed
            // - 2 Pointer => Backfeed wenn 2. Pointer-Name nach prev/phase/state/feed/back aussieht
            bool backfeedRequested =
                ptrArgs.Count >= 3 ||
                (ptrArgs.Count == 2 && IsBackfeedName(ptrArgs[1].Name));

            // output needed?
            // - 3 Pointer: input + output + backfeed
            // - 2 Pointer: wenn backfeed => in-place+backfeed, sonst in/out
            bool outputNeeded =
                (ptrArgs.Count >= 3) ||
                (ptrArgs.Count == 2 && !backfeedRequested);

            // 3) Allocate output buffer (only if we truly need it)
            CudaMem? outMem = null;
            if (outputNeeded)
            {
                var outElemType = ptrArgs.Count >= 2 ? ptrArgs[1].Type.GetElementType() : null;
                if (outElemType == null)
                {
                    CudaLog.Error("Kernel output pointer arg could not be resolved.", kernelName);
                    return null;
                }

                sw.Restart();

                if (mem.Count <= 1)
                {
                    var method = typeof(CudaRegister).GetMethod("AllocateSingleAsync")?.MakeGenericMethod(outElemType);
                    var task = (Task<CudaMem?>?) method?.Invoke(this.Register, [(IntPtr) mem.IndexLength]);
                    outMem = task != null ? await task.ConfigureAwait(false) : null;
                }
                else
                {
                    var method = typeof(CudaRegister).GetMethod("AllocateGroupAsync")?.MakeGenericMethod(outElemType);
                    var task = (Task<CudaMem?>?) method?.Invoke(this.Register, [mem.Lengths]);
                    outMem = task != null ? await task.ConfigureAwait(false) : null;
                }

                if (outMem == null)
                {
                    CudaLog.Error("Failed to allocate output buffer for audio kernel.", kernelName);
                    return null;
                }

                sw.Stop();
                audio["__cudaAllocOut_ms"] = sw.Elapsed.TotalMilliseconds;
                CudaLog.Info("Allocated output buffer for audio kernel. (" + sw.Elapsed.TotalMilliseconds.ToString("F1") + " ms)", kernelName);
            }

            // 3.1) Allocate / initialize backfeed buffer (state) if requested
            CudaMem? backMem = null;
            if (backfeedRequested)
            {
                int backPtrIndex = (ptrArgs.Count >= 3) ? 2 : 1;
                var backElemType = ptrArgs[backPtrIndex].Type.GetElementType();
                if (backElemType == null)
                {
                    CudaLog.Error("Backfeed pointer arg element type could not be resolved.", kernelName);
                    return null;
                }

                if (backElemType != typeof(ManagedCuda.VectorTypes.float2))
                {
                    CudaLog.Warn($"Backfeed requested, but backfeed element type is {backElemType.FullName}. Auto-init uses zeros only.", kernelName);
                }

                long stateLen = mem.Count > 0 ? mem.Lengths[0].ToInt64() : mem.IndexLength.ToInt64();
                if (stateLen <= 0)
                {
                    stateLen = chunkSize > 0 ? chunkSize : 0;
                }
                if (stateLen <= 0)
                {
                    CudaLog.Error("Cannot determine backfeed/state length.", kernelName);
                    return null;
                }

                if (backElemType == typeof(ManagedCuda.VectorTypes.float2))
                {
                    var zeros = new ManagedCuda.VectorTypes.float2[stateLen];
                    backMem = await this.Register.PushDataAsync(zeros).ConfigureAwait(false);
                }
                else if (backElemType == typeof(float))
                {
                    var zeros = new float[stateLen];
                    backMem = await this.Register.PushDataAsync(zeros).ConfigureAwait(false);
                }
                else
                {
                    var zeros = new byte[stateLen];
                    backMem = await this.Register.PushDataAsync(zeros).ConfigureAwait(false);
                }

                if (backMem == null)
                {
                    CudaLog.Error("Failed to allocate/init backfeed buffer.", kernelName);
                    return null;
                }
            }

            // 4.0) Load kernel
            var kernel = this.Compiler.LoadKernel(kernelName);
            if (kernel == null)
            {
                CudaLog.Error("Failed to load kernel for audio processing.", kernelName);
                return null;
            }

            // 4) Launch kernel on each chunk
            sw.Restart();

            Func<double, double> mapKernelProgress = didFft
                ? p => 0.33 + Math.Clamp(p, 0.0, 1.0) * 0.33
                : p => Math.Clamp(p, 0.0, 1.0);

            Dictionary<string, string>? argStrings = arguments != null ? ToStringArgDict(arguments) : null;

            int totalChunks = mem.DevicePointers.Length;
            int completed = 0;

            // If backfeed/state is used, FORCE sequential execution (state depends on previous chunk).
            bool forceSequential = backfeedRequested;

            // streams
            var streamList = await this.Register.GetManyStreamsAsync(maxCount: 0).ConfigureAwait(false);
            var streams = streamList?.ToArray();

            // choose a stream for sequential/stateful mode
            var seqStream = this.Register.GetStream();
            if (seqStream == null)
            {
                CudaLog.Error("Failed to get CUDA stream for kernel execution.", kernelName);
                return null;
            }

            if (forceSequential || streams == null || streams.Length == 0)
            {
                for (int i = 0; i < totalChunks; i++)
                {
                    ct.ThrowIfCancellationRequested();

                    CUdeviceptr inPtr = mem.DevicePointers[i];
                    CUdeviceptr? outPtr = outMem != null && outMem.DevicePointers.Length > i ? outMem.DevicePointers[i] : null;
                    CUdeviceptr? backPtr = backMem != null ? backMem.DevicePointers[0] : null; // ONE shared state

                    object[] mergedArgs = this.Launcher.MergeGenericKernelArgumentsDynamic(
                        kernelName,
                        inputBuffer: inPtr,
                        outputBuffer: outPtr,
                        backfeedBuffer: backPtr,
                        arguments: argStrings);

                    if (mergedArgs.Length == 0)
                    {
                        continue;
                    }

                    Configure1D(kernel, mem.Lengths[i].ToInt64());
                    kernel.RunAsync(seqStream.Stream, mergedArgs);

                    if (progress != null)
                    {
                        double pLinear = (i + 1) / (double) totalChunks;
                        progress.Report(mapKernelProgress(pLinear));
                    }
                }

                seqStream.Synchronize();
            }
            else
            {
                // Parallel only if not stateful
                int S = streams.Length;
                var tasks = new List<Task>(S);

                for (int s = 0; s < S; s++)
                {
                    int streamIndex = s;
                    tasks.Add(Task.Run(async () =>
                    {
                        this.Context.SetCurrent();

                        var stream = streams[streamIndex];

                        for (int i = streamIndex; i < totalChunks; i += S)
                        {
                            ct.ThrowIfCancellationRequested();

                            CUdeviceptr inPtr = mem.DevicePointers[i];
                            CUdeviceptr? outPtr = outMem != null && outMem.DevicePointers.Length > i ? outMem.DevicePointers[i] : null;

                            object[] mergedArgs = this.Launcher.MergeGenericKernelArgumentsDynamic(
                                kernelName,
                                inputBuffer: inPtr,
                                outputBuffer: outPtr,
                                backfeedBuffer: null,
                                arguments: argStrings);

                            if (mergedArgs.Length == 0)
                            {
                                continue;
                            }

                            Configure1D(kernel, mem.Lengths[i].ToInt64());
                            kernel.RunAsync(stream.Stream, mergedArgs);

                            await Task.Yield();

                            int done = Interlocked.Increment(ref completed);
                            if (progress != null)
                            {
                                double pLinear = done / (double) totalChunks;
                                progress.Report(mapKernelProgress(pLinear));
                            }
                        }

                        // wait this stream
                        stream.Synchronize();
                    }, ct));
                }

                await Task.WhenAll(tasks).ConfigureAwait(false);
            }

            sw.Stop();
            audio["__cudaKernel_ms"] = sw.Elapsed.TotalMilliseconds;
            CudaLog.Info("Executed audio kernel on GPU. (" + sw.Elapsed.TotalMilliseconds.ToString("F1") + " ms)", kernelName);

            // Switch mem to output if out-of-place
            if (outMem != null)
            {
                try
                {
                    long freed = this.Register.FreeMemory(mem);
                    CudaLog.Info("Freed old input buffer after kernel execution. (" + freed.ToString("N0") + " bytes)", kernelName);
                    mem = outMem;
                }
                catch (Exception ex)
                {
                    CudaLog.Warn("Failed to dispose old input buffer after kernel execution.", ex.Message);
                }
            }

            // 5) Optionally Inverse CuFFT (+ post-scale + post-window)
            if (didFft && mem.ElementType == typeof(ManagedCuda.VectorTypes.float2))
            {
                // Phase 3: 0.66..1.0
                // split Phase 3:
                // - IFFT:       0.66..0.94
                // - scale:      0.94..0.97
                // - post-window 0.97..1.00
                IProgress<double>? ifftProgress = progress != null
                    ? new Progress<double>(p => progress.Report(0.66 + Math.Clamp(p, 0.0, 1.0) * (0.94 - 0.66)))
                    : null;

                sw.Restart();
                var ifftPtr = await this.Fourier.PerformIfftManyAsync(mem.IndexPointer, false, ifftProgress).ConfigureAwait(false);
                mem = this.Register[ifftPtr];
                if (mem == null)
                {
                    CudaLog.Error("Failed to perform inverse FFT on audio data.", kernelName);
                    return null;
                }

                sw.Stop();
                audio["__cudaIfft_ms"] = sw.Elapsed.TotalMilliseconds;
                CudaLog.Info("Performed I-FFT on audio data. (" + sw.Elapsed.TotalMilliseconds.ToString("F1") + " ms)", kernelName);

                // After IFFT we should be back in float time-domain:
                // Apply CUFFT scaling (1/N) and synthesis window (sqrt-hann)
                IProgress<double>? phase3Progress = progress != null
                    ? new Progress<double>(p => progress.Report(Math.Clamp(p, 0.66, 1.0)))
                    : null;

                // scale
                await RunFloatInplacePerChunkKernelAsync(
                    kName: "ifft_scale_01",
                    m: mem,
                    localProgress: phase3Progress,
                    phaseStart: 0.94,
                    phaseSpan: 0.03).ConfigureAwait(false);

                // post-window
                await RunFloatInplacePerChunkKernelAsync(
                    kName: "window_sqrthann_01",
                    m: mem,
                    localProgress: phase3Progress,
                    phaseStart: 0.97,
                    phaseSpan: 0.03).ConfigureAwait(false);
            }

            // 5.1) Verify mem Type is float for audio output
            if (mem.ElementType != typeof(float))
            {
                CudaLog.Error("Audio output data is not of type float! Cannot pull back to AudioObj.", kernelName);
                return null;
            }

            // 6) Pull
            sw.Restart();
            if (mem.Count <= 1)
            {
                var data = await this.Register.PullDataAsync<float>(mem.IndexPointer).ConfigureAwait(false);
                audio.Data = data;
            }
            else
            {
                var chunks = await this.Register.PullChunksAsync<float>(mem.IndexPointer).ConfigureAwait(false);
                if (stretchFactor.HasValue)
                {
                    audio.StretchFactor = stretchFactor.Value;
                    audio.BeatsPerMinute = audio.BeatsPerMinute / stretchFactor.Value;
                }

                await audio.AggregateStretchedChunksAsync(chunks).ConfigureAwait(false);
            }
            sw.Stop();

            audio["__cudaPull_ms"] = sw.Elapsed.TotalMilliseconds;
            CudaLog.Info("Pulled audio data from GPU. (" + sw.Elapsed.TotalMilliseconds.ToString("F1") + " ms)", kernelName);

            // 7) Normalize (optional)
            if (didFft)
            {
                await audio.NormalizeAsync(1.0f).ConfigureAwait(false);
            }

            // cleanup backfeed mem if we allocated it
            if (backMem != null)
            {
                try { this.Register.FreeMemory(backMem); } catch { /* ignore */ }
            }

            progress?.Report(1.0);
            return audio;

            static bool IsBackfeedName(string name)
            {
                if (string.IsNullOrWhiteSpace(name))
                {
                    return false;
                }

                name = name.Trim();
                return name.Contains("prev", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("phase", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("state", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("feed", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("back", StringComparison.OrdinalIgnoreCase);
            }
        }


        private static void Configure1D(CudaKernel kernel, long elementCount)
        {
            if (kernel == null)
            {
                throw new ArgumentNullException(nameof(kernel));
            }

            const int block = 256;
            long grid = (elementCount + block - 1) / block;
            if (grid <= 0)
            {
                grid = 1;
            }

            kernel.BlockDimensions = new dim3(block, 1, 1);
            kernel.GridDimensions = new dim3((uint) Math.Min(grid, int.MaxValue), 1, 1);
        }


        public async Task ExecuteAudioKernelInPlaceAsync(
            AudioObj audio,
            string kernelName,
            int chunkSize,
            float overlap,
            Dictionary<string, object>? arguments = null,
            CancellationToken ct = default)
        {
            _ = await this.ExecuteAudioKernelCoreAsync<float>(
                audio: audio,
                kernelName: kernelName,
                chunkSize: chunkSize,
                overlap: overlap,
                mode: AudioKernelMode.InPlace,
                resultKind: AudioKernelResultKind.None,
                arguments: arguments,
                ct: ct).ConfigureAwait(false);
        }

        public async Task<AudioObj?> ExecuteAudioKernelOutOfPlaceAsync(
            AudioObj audio,
            string kernelName,
            int chunkSize,
            float overlap,
            Dictionary<string, object>? arguments = null,
            CancellationToken ct = default)
        {
            var outData = await this.ExecuteAudioKernelCoreAsync<float>(
                audio: audio,
                kernelName: kernelName,
                chunkSize: chunkSize,
                overlap: overlap,
                mode: AudioKernelMode.OutOfPlace,
                resultKind: AudioKernelResultKind.Data,
                arguments: arguments,
                ct: ct).ConfigureAwait(false);

            if (outData is not { Length: > 0 })
            {
                return null;
            }

            // NOTE: adjust if your AudioObj clone/copy API differs.
            AudioObj clone;
            try
            {
                clone = await audio.CloneAsync().ConfigureAwait(false);
            }
            catch
            {
                // fallback: if you don't have CloneAsync, replace with your own copy ctor / factory
                clone = audio;
            }

            clone.Data = outData;
            return clone;
        }

        public async Task<T?> ExecuteAudioKernelGetValueAsync<T>(
            AudioObj audio,
            string kernelName,
            int chunkSize,
            float overlap,
            Dictionary<string, object>? arguments = null,
            CancellationToken ct = default) where T : unmanaged
        {
            var data = await this.ExecuteAudioKernelCoreAsync<T>(
                audio: audio,
                kernelName: kernelName,
                chunkSize: chunkSize,
                overlap: overlap,
                mode: AudioKernelMode.OutBuffer,
                resultKind: AudioKernelResultKind.Value,
                arguments: arguments,
                ct: ct).ConfigureAwait(false);

            if (data is null || data.Length == 0)
            {
                return null;
            }

            return data[0];
        }

        public async Task<T[]?> ExecuteAudioKernelGetDataAsync<T>(
            AudioObj audio,
            string kernelName,
            int chunkSize,
            float overlap,
            Dictionary<string, object>? arguments = null,
            CancellationToken ct = default) where T : unmanaged
        {
            var data = await this.ExecuteAudioKernelCoreAsync<T>(
                audio: audio,
                kernelName: kernelName,
                chunkSize: chunkSize,
                overlap: overlap,
                mode: AudioKernelMode.OutBuffer,
                resultKind: AudioKernelResultKind.Data,
                arguments: arguments,
                ct: ct).ConfigureAwait(false);

            return data;
        }

        // ---------------------------
        // CORE ENGINE
        // ---------------------------

        private enum AudioKernelMode
        {
            InPlace,
            OutOfPlace,   // same length output
            OutBuffer     // output element count may differ (GetValue/GetData)
        }

        private enum AudioKernelResultKind
        {
            None,
            Value,
            Data
        }

        private async Task<T[]?> ExecuteAudioKernelCoreAsync<T>(
            AudioObj audio,
            string kernelName,
            int chunkSize,
            float overlap,
            AudioKernelMode mode,
            AudioKernelResultKind resultKind,
            Dictionary<string, object>? arguments,
            CancellationToken ct) where T : unmanaged
        {
            if (!this.Initialized || this.Register == null || this.Compiler == null || this.Launcher == null || this.Context == null)
            {
                CudaLog.Warn("CUDA not initialized; kernel execution aborted.", kernelName);
                return null;
            }

            if (audio is null || audio.Data is null || audio.Data.Length == 0)
            {
                CudaLog.Warn("Audio is null/empty; kernel execution aborted.", kernelName);
                return null;
            }

            if (string.IsNullOrWhiteSpace(kernelName))
            {
                CudaLog.Warn("Kernel name is empty; kernel execution aborted.");
                return null;
            }

            // chunkSize rules
            chunkSize = Math.Max(0, chunkSize);
            if (chunkSize > 0 && !IsPowerOfTwo(chunkSize))
            {
                CudaLog.Warn("chunkSize must be 2^n (or 0 for no chunking).", chunkSize.ToString());
                return null;
            }

            // overlap rules
            overlap = Clamp(overlap, 0.0f, 0.95f);

            // try load kernel (compile if needed)
            try
            {
                var k = this.Compiler.LoadKernel(kernelName, silent: false);
                if (k == null)
                {
                    CudaLog.Warn("Kernel could not be loaded.", kernelName);
                    return null;
                }
            }
            catch (Exception ex)
            {
                CudaLog.Error("Kernel load failed", ex.Message);
                return null;
            }

            var argDefs = this.Compiler.GetArguments(kernelName);
            if (argDefs == null || argDefs.Count == 0)
            {
                CudaLog.Warn("Kernel argument parsing failed (no args).", kernelName);
                return null;
            }

            // Convert args → string dict for your existing MergeGenericKernelArgumentsDynamic
            // (your launcher expects Dictionary<string,string> for scalar args)
            var userArgsString = ToStringArgDict(arguments);

            // figure out pointer-arg count
            int ptrCount = argDefs.Values.Count(t => t.IsPointer);
            if (ptrCount <= 0)
            {
                CudaLog.Warn("Kernel has no pointer args; not an audio kernel?", kernelName);
                return null;
            }

            bool wantsTwoBuffers =
                mode != AudioKernelMode.InPlace &&
                (ptrCount >= 2);

            // detect FFT hint (best-effort)
            bool wantsFft =
                ReadBool(arguments, "__fft", defaultValue: false) ||
                kernelName.Contains("fft", StringComparison.OrdinalIgnoreCase) ||
                kernelName.Contains("freq", StringComparison.OrdinalIgnoreCase) ||
                kernelName.Contains("spectrum", StringComparison.OrdinalIgnoreCase);

            // output size rules for OutBuffer
            long outElementCount = 0;
            if (mode == AudioKernelMode.OutBuffer)
            {
                outElementCount = ResolveOutputElementCount(arguments, audio);
                if (outElementCount <= 0)
                {
                    // GetValue defaults to 1
                    outElementCount = (resultKind == AudioKernelResultKind.Value) ? 1 : 0;
                }

                if (outElementCount <= 0)
                {
                    CudaLog.Warn("Output element count missing. Provide arguments[\"outputLength\"] / \"outCount\" / \"resultCount\".", kernelName);
                    return null;
                }
            }

            // Allow user to pass pre-existing CUDA pointers (your earlier question)
            // Special keys:
            //  "__inputPtr"  : IntPtr or CUdeviceptr
            //  "__outputPtr" : IntPtr or CUdeviceptr
            var inputPtrOverride = ReadDevicePtr(arguments, "__inputPtr");
            var outputPtrOverride = ReadDevicePtr(arguments, "__outputPtr");

            IntPtr inputIndexPtr = IntPtr.Zero;
            IntPtr outputIndexPtr = IntPtr.Zero;
            bool freeInput = false;
            bool freeOutput = false;

            try
            {
                // 1) Push input (unless overridden)
                if (inputPtrOverride.HasValue)
                {
                    // We don't own this pointer, and we can't map it to IndexPointer safely.
                    // So: we use it directly as CUdeviceptr, and do not free it.
                }
                else
                {
                    if (chunkSize <= 0)
                    {
                        var mem = await this.Register.AllocateSingleAsync<float>((IntPtr) audio.Data.LongLength).ConfigureAwait(false);
                        if (mem == null)
                        {
                            return null;
                        }

                        freeInput = true;
                        inputIndexPtr = mem.IndexPointer;

                        await this.Register.PushDataAsync(audio.Data).ConfigureAwait(false);
                    }
                    else
                    {
                        // chunking: allocate + push group (one buffer per chunk)
                        var chunks = BuildOverlappedChunks(audio.Data, chunkSize, overlap);
                        var lengths = chunks.Select(c => (IntPtr) c.Length).ToArray();

                        var mem = await this.Register.AllocateGroupAsync<float>(lengths).ConfigureAwait(false);
                        if (mem == null)
                        {
                            return null;
                        }

                        freeInput = true;
                        inputIndexPtr = mem.IndexPointer;

                        // Push chunks
                        await this.Register.PushChunksAsync(chunks).ConfigureAwait(false);
                    }
                }

                // 2) Optional FFT forward (only meaningful if we own an IndexPointer)
                if (wantsFft && this.Fourier != null && inputPtrOverride is null && inputIndexPtr != IntPtr.Zero)
                {
                    inputIndexPtr = await this.Fourier.PerformFftAsync(inputIndexPtr, keep: false).ConfigureAwait(false);
                    // after ForwardAsync with keep:false, the old input was freed by Fourier helper :contentReference[oaicite:2]{index=2}
                    freeInput = true;
                }

                // 3) Allocate output (if needed & not overridden)
                if (mode == AudioKernelMode.InPlace)
                {
                    // no output buffer
                }
                else
                {
                    if (outputPtrOverride.HasValue)
                    {
                        // don't allocate; don't free
                    }
                    else
                    {
                        if (mode == AudioKernelMode.OutOfPlace)
                        {
                            // same length as input audio
                            long len = audio.Data.LongLength;
                            if (chunkSize > 0)
                            {
                                // one output chunk per input chunk
                                var inMem = (inputIndexPtr != IntPtr.Zero) ? this.Register[inputIndexPtr] : null;
                                if (inMem == null)
                                {
                                    return null;
                                }

                                var outMem = await this.Register.AllocateGroupAsync<float>(inMem.Lengths).ConfigureAwait(false);
                                if (outMem == null)
                                {
                                    return null;
                                }

                                freeOutput = true;
                                outputIndexPtr = outMem.IndexPointer;
                            }
                            else
                            {
                                var outMem = await this.Register.AllocateSingleAsync<float>((IntPtr) len).ConfigureAwait(false);
                                if (outMem == null)
                                {
                                    return null;
                                }

                                freeOutput = true;
                                outputIndexPtr = outMem.IndexPointer;
                            }
                        }
                        else
                        {
                            // OutBuffer (GetValue/GetData)
                            var outMem = await this.Register.AllocateSingleAsync<T>((IntPtr) outElementCount).ConfigureAwait(false);
                            if (outMem == null)
                            {
                                return null;
                            }

                            freeOutput = true;
                            outputIndexPtr = outMem.IndexPointer;
                        }
                    }
                }

                // 4) Execute kernel
                // We delegate argument merging to your launcher’s MergeGenericKernelArgumentsDynamic
                // by passing input/output device ptr (first two CUdeviceptrs). :contentReference[oaicite:3]{index=3}

                var kernel = this.Compiler.Kernel;
                if (kernel == null)
                {
                    CudaLog.Warn("Kernel not available after load.", kernelName);
                    return null;
                }

                // Handle group execution (chunked)
                if (chunkSize > 0 && inputPtrOverride is null)
                {
                    var inMem = this.Register[inputIndexPtr];
                    if (inMem == null)
                    {
                        return null;
                    }

                    var outMem = (outputIndexPtr != IntPtr.Zero) ? this.Register[outputIndexPtr] : null;

                    // optional parallel chunk execution
                    bool parallelChunks = ReadBool(arguments, "__parallelChunks", defaultValue: false);

                    Func<int, Task> runChunk = async (i) =>
                    {
                        ct.ThrowIfCancellationRequested();

                        CUdeviceptr inPtr = new(inMem.Pointers[i]);

                        CUdeviceptr? outPtr = null;
                        if (mode != AudioKernelMode.InPlace)
                        {
                            if (outputPtrOverride.HasValue)
                            {
                                outPtr = outputPtrOverride.Value;
                            }
                            else if (outMem != null && outMem.Count > i)
                            {
                                outPtr = new CUdeviceptr(outMem.Pointers[i]);
                            }
                        }

                        object[] merged = this.Launcher.MergeGenericKernelArgumentsDynamic(
                            kernelName,
                            inputBuffer: inPtr,
                            outputBuffer: outPtr,
                            arguments: userArgsString);

                        if (merged.Length == 0)
                        {
                            return;
                        }

                        Configure1D(kernel, inMem.Lengths[i].ToInt64());
                        kernel.Run(merged);

                        await Task.CompletedTask;
                    };

                    if (parallelChunks)
                    {
                        var tasks = Enumerable.Range(0, inMem.Count).Select(runChunk).ToArray();
                        await Task.WhenAll(tasks).ConfigureAwait(false);
                    }
                    else
                    {
                        for (int i = 0; i < inMem.Count; i++)
                        {
                            await runChunk(i).ConfigureAwait(false);
                        }
                    }
                }
                else
                {
                    CUdeviceptr inPtr =
                        inputPtrOverride
                        ?? (inputIndexPtr != IntPtr.Zero && this.Register[inputIndexPtr] is { } mIn ? new CUdeviceptr(mIn.Pointers[0]) : default);

                    CUdeviceptr? outPtr = null;
                    if (mode != AudioKernelMode.InPlace)
                    {
                        outPtr =
                            outputPtrOverride
                            ?? (outputIndexPtr != IntPtr.Zero && this.Register[outputIndexPtr] is { } mOut ? new CUdeviceptr(mOut.Pointers[0]) : null);
                    }

                    object[] merged = this.Launcher.MergeGenericKernelArgumentsDynamic(
                        kernelName,
                        inputBuffer: inPtr,
                        outputBuffer: outPtr,
                        arguments: userArgsString);

                    if (merged.Length == 0)
                    {
                        return null;
                    }

                    long workCount =
                        mode == AudioKernelMode.OutBuffer ? outElementCount :
                        (chunkSize > 0 ? chunkSize : audio.Data.LongLength);

                    Configure1D(kernel, workCount);
                    kernel.Run(merged);
                }

                // 5) sync
                this.Context.Synchronize();

                // 6) Optional IFFT back (only if we own IndexPointer)
                if (wantsFft && this.Fourier != null && mode != AudioKernelMode.InPlace && outputPtrOverride is null && outputIndexPtr != IntPtr.Zero)
                {
                    outputIndexPtr = await this.Fourier.PerformIfftAsync(outputIndexPtr, keep: false).ConfigureAwait(false);
                    freeOutput = true;
                }

                // 7) Pull result
                if (mode == AudioKernelMode.InPlace)
                {
                    if (inputPtrOverride is not null)
                    {
                        return null; // can't pull from raw CUdeviceptr without IndexPointer mapping
                    }

                    // pull back into audio
                    var pulled = this.Register.PullData<float>(inputIndexPtr, keep: true);
                    if (pulled != null && pulled.Length == audio.Data.Length)
                    {
                        audio.Data = pulled;
                    }
                    return null;
                }

                if (mode == AudioKernelMode.OutOfPlace)
                {
                    if (outputPtrOverride is not null)
                    {
                        return null;
                    }

                    if (chunkSize <= 0)
                    {
                        var pulled = this.Register.PullData<float>(outputIndexPtr, keep: true);
                        return pulled as T[];
                    }
                    else
                    {
                        // Pull group chunks and overlap-add back to full length
                        var outMem = this.Register[outputIndexPtr];
                        if (outMem == null)
                        {
                            return null;
                        }

                        var chunks = new List<float[]>(outMem.Count);
                        for (int i = 0; i < outMem.Count; i++)
                        {
                            var c = this.Register.PullData<float>(outputIndexPtr, keep: true, groupIndex: i) ?? [];
                            chunks.Add(c);
                        }

                        var rebuilt = RebuildFromOverlappedChunks(chunks, chunkSize, overlap, audio.Data.Length);
                        return rebuilt as T[];
                    }
                }

                // OutBuffer
                if (outputPtrOverride is not null)
                {
                    return null;
                }

                var pulledT = this.Register.PullData<T>(outputIndexPtr, keep: true);
                return pulledT;
            }
            catch (OperationCanceledException)
            {
                CudaLog.Info("Kernel execution canceled.", kernelName);
                return null;
            }
            catch (Exception ex)
            {
                CudaLog.Error("Kernel execution failed", $"{kernelName}: {ex.Message}");
                return null;
            }
            finally
            {
                try
                {
                    if (freeInput && inputIndexPtr != IntPtr.Zero)
                    {
                        this.Register.FreeMemory(inputIndexPtr);
                    }
                }
                catch { /* ignore */ }

                try
                {
                    if (freeOutput && outputIndexPtr != IntPtr.Zero)
                    {
                        this.Register.FreeMemory(outputIndexPtr);
                    }
                }
                catch { /* ignore */ }
            }
        }

        // ---------------------------
        // HELPERS
        // ---------------------------

        private static bool IsPowerOfTwo(int x) => x > 0 && (x & (x - 1)) == 0;

        private static float Clamp(float v, float min, float max) => (v < min) ? min : (v > max) ? max : v;

        private static Dictionary<string, string>? ToStringArgDict(Dictionary<string, object>? args)
        {
            if (args == null || args.Count == 0)
            {
                return null;
            }

            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var (k, v) in args)
            {
                if (string.IsNullOrWhiteSpace(k))
                {
                    continue;
                }

                // skip internal keys used by this wrapper
                if (k.StartsWith("__", StringComparison.Ordinal))
                {
                    continue;
                }

                if (v == null)
                {
                    continue;
                }

                string s = v switch
                {
                    float f => f.ToString("R", CultureInfo.InvariantCulture),
                    double db => db.ToString("R", CultureInfo.InvariantCulture),
                    decimal m => m.ToString(CultureInfo.InvariantCulture),
                    bool b => b ? "1" : "0",
                    _ => Convert.ToString(v, CultureInfo.InvariantCulture) ?? ""
                };

                if (!string.IsNullOrWhiteSpace(s))
                {
                    d[k] = s;
                }
            }

            return d.Count > 0 ? d : null;
        }

        private static bool ReadBool(Dictionary<string, object>? args, string key, bool defaultValue)
        {
            if (args == null)
            {
                return defaultValue;
            }

            if (!args.TryGetValue(key, out var v) || v == null)
            {
                return defaultValue;
            }

            return v switch
            {
                bool b => b,
                int i => i != 0,
                long l => l != 0,
                string s when bool.TryParse(s, out var b) => b,
                string s when int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) => i != 0,
                _ => defaultValue
            };
        }

        private static CUdeviceptr? ReadDevicePtr(Dictionary<string, object>? args, string key)
        {
            if (args == null)
            {
                return null;
            }

            if (!args.TryGetValue(key, out var v) || v == null)
            {
                return null;
            }

            try
            {
                return v switch
                {
                    CUdeviceptr p => p,
                    IntPtr ip => new CUdeviceptr(ip),
                    long l => new CUdeviceptr(new IntPtr(l)),
                    ulong ul => new CUdeviceptr(new IntPtr(unchecked((long) ul))),
                    _ => null
                };
            }
            catch
            {
                return null;
            }
        }

        private static long ResolveOutputElementCount(Dictionary<string, object>? args, AudioObj audio)
        {
            if (args == null)
            {
                return 0;
            }

            static long AsLong(object? v)
            {
                if (v == null)
                {
                    return 0;
                }

                return v switch
                {
                    int i => i,
                    long l => l,
                    uint ui => ui,
                    ulong ul => unchecked((long) ul),
                    string s when long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l) => l,
                    _ => 0
                };
            }

            // common keys users will provide
            var candidates = new[]
            {
                "outputLength", "outLength", "outputCount", "outCount", "resultCount", "count", "n"
            };

            foreach (var k in candidates)
            {
                if (args.TryGetValue(k, out var v))
                {
                    var n = AsLong(v);
                    if (n > 0)
                    {
                        return n;
                    }
                }
            }

            // fallback: if they passed "length" but meant output length
            if (args.TryGetValue("length", out var lv))
            {
                var n = AsLong(lv);
                if (n > 0)
                {
                    return n;
                }
            }

            // last fallback: if it looks like "getdata" but user forgot, return audio length
            return 0;
        }

        private static List<float[]> BuildOverlappedChunks(float[] src, int chunkSize, float overlap)
        {
            int hop = Math.Max(1, (int) Math.Round(chunkSize * (1.0f - overlap)));
            var list = new List<float[]>();

            for (int start = 0; start < src.Length; start += hop)
            {
                var chunk = new float[chunkSize];
                int copy = Math.Min(chunkSize, src.Length - start);
                Array.Copy(src, start, chunk, 0, copy);
                list.Add(chunk);

                if (start + copy >= src.Length)
                {
                    break;
                }
            }

            return list;
        }

        private static float[] RebuildFromOverlappedChunks(IReadOnlyList<float[]> chunks, int chunkSize, float overlap, int targetLength)
        {
            int hop = Math.Max(1, (int) Math.Round(chunkSize * (1.0f - overlap)));
            var dst = new float[targetLength];

            int pos = 0;
            for (int i = 0; i < chunks.Count; i++)
            {
                var c = chunks[i];
                int copy = Math.Min(c.Length, targetLength - pos);
                if (copy <= 0)
                {
                    break;
                }

                for (int j = 0; j < copy; j++)
                {
                    // simple overlap-add (no windowing). You can add Hann window here later.
                    dst[pos + j] += c[j];
                }

                pos += hop;
                if (pos >= targetLength)
                {
                    break;
                }
            }

            return dst;
        }

        private static void Configure1DOld(CudaKernel kernel, long elementCount)
        {
            // basic sane defaults; your launcher has a smarter version internally, but it’s private :contentReference[oaicite:4]{index=4}
            int block = 256;
            long grid = (elementCount + block - 1) / block;
            if (grid <= 0)
            {
                grid = 1;
            }

            kernel.BlockDimensions = new dim3(block, 1, 1);
            kernel.GridDimensions = new dim3((uint) Math.Min(grid, int.MaxValue), 1, 1);
        }

        public float GetPowerUsageInWatts()
        {
            if (this._gpuStats == null)
            {
                return 0.0f;
            }

            return (float) (this._gpuStats.CurrentPowerWatts ?? 0);
        }
    }



    public enum VramStats
    {
        Total,
        Free,
        Used
    }
}
