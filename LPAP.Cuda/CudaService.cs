using ManagedCuda;
using ManagedCuda.BasicTypes;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LPAP.Cuda
{
    public class CudaService
    {
        private readonly Lock _initLock = new();

        public string KernelsPath { get; internal set; } = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Kernels");
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
            this.KernelsPath = this.PrepareKernelDirectory(this.KernelsPath);
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

        public IEnumerable<string> GetKernels(string? ilter = null, bool showUncompiled = false, bool filePaths = false)
        {
            if (this.Compiler == null)
            {
                return [];
            }

            List<string> kernels = [];
            if (showUncompiled)
            {
                kernels = this.Compiler.GetCuFiles(this.KernelsPath);
            }
            else
            {
                kernels = this.Compiler.GetPtxFiles(this.KernelsPath);
            }

            if (!filePaths)
            {
                return kernels.Select(k => Path.GetFileNameWithoutExtension(k)).ToList();
            }

            if (!string.IsNullOrWhiteSpace(ilter))
            {
                kernels = kernels.Where(k => k.Contains(ilter, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            return kernels;
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
    }

    public enum VramStats
    {
        Total,
        Free,
        Used
    }
}
