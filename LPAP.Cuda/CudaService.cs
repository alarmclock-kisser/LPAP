using LPAP.Audio;
using ManagedCuda;
using ManagedCuda.BasicTypes;
using ManagedCuda.VectorTypes;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace LPAP.Cuda
{
    public class CudaService
    {
        private readonly Lock _initLock = new();

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

            this.DeviceIndex = -1;
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
                    di = di.Parent;

                string candidate = Path.Combine(di.FullName, "LPAP.Cuda", "Kernels");
                if (Directory.Exists(candidate))
                    return Path.GetFullPath(candidate);

                // 2) Walk upwards and search for "LPAP.Cuda\Kernels"
                di = new DirectoryInfo(exeDir);
                while (di != null)
                {
                    candidate = Path.Combine(di.FullName, "LPAP.Cuda", "Kernels");
                    if (Directory.Exists(candidate))
                        return Path.GetFullPath(candidate);

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

        public IEnumerable<string?> GetKernels(string? filter = null, bool showUncompiled = false, bool filePaths = false)
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
                        var name = Path.GetFileNameWithoutExtension(f);
                        return f.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                               name.Contains(filter, StringComparison.OrdinalIgnoreCase);
                    })
                    .ToList();
            }

            if (filePaths)
            {
                return files;
            }

            return files
                .Select(Path.GetFileNameWithoutExtension)
                .Where(n => !string.IsNullOrWhiteSpace(n))
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

        public async Task<double?> GetGpuLoadInPercentAsync(int? deviceIndex = null)
        {
            deviceIndex ??= this.DeviceIndex;
            if (!this.Initialized || deviceIndex.Value < 0)
            {
                return null;
            }

            double? load = 0.0;
            try
            {
                load = await GpuStats.GetGpuLoadAsync(deviceIndex.Value) * 100.0;
            }
            catch (Exception ex)
            {
                CudaLog.Warn("Failed to get GPU load", ex.Message);
            }

            return load;
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

                        await this.Register.PushDataAsync<float>(audio.Data).ConfigureAwait(false);
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

        private static void Configure1D(ManagedCuda.CudaKernel kernel, long elementCount)
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
    }



    public enum VramStats
    {
        Total,
        Free,
        Used
    }
}
