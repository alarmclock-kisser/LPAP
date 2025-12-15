using ManagedCuda;
using ManagedCuda.BasicTypes;
using ManagedCuda.VectorTypes;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace LPAP.Cuda;

internal sealed class CudaLauncher : IDisposable
{
    private readonly PrimaryContext _ctx;
    private readonly CudaRegister _register;
    private readonly CudaFourier _fourier;
    private readonly CudaCompiler _compiler;

    internal CudaLauncher(PrimaryContext ctx, CudaRegister register, CudaFourier fourier, CudaCompiler compiler)
    {
        this._ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
        this._register = register ?? throw new ArgumentNullException(nameof(register));
        this._fourier = fourier ?? throw new ArgumentNullException(nameof(fourier));
        this._compiler = compiler ?? throw new ArgumentNullException(nameof(compiler));
    }

    private CudaKernel? Kernel => this._compiler.Kernel;

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    public IntPtr ExecuteTimeStretch(IntPtr indexPointer, string kernel, double factor, int chunkSize, int overlapSize, int sampleRate, int channels, bool keep = false)
    {
        this._compiler.LoadKernel(kernel, silent: true);
        if (this.Kernel == null)
        {
            CudaLog.Warn("Time stretch kernel not loaded", kernel);
            return indexPointer;
        }

        var mem = this._register[indexPointer];
        if (mem == null || mem.IndexPointer == IntPtr.Zero)
        {
            CudaLog.Warn("Time stretch input memory invalid");
            return IntPtr.Zero;
        }

        bool transformed = false;
        IntPtr fftPointer = mem.IndexPointer;
        if (mem.ElementType != typeof(float2))
        {
            fftPointer = this._fourier.PerformFft(indexPointer, keep);
            transformed = fftPointer != IntPtr.Zero;
        }

        if (fftPointer == IntPtr.Zero)
        {
            return indexPointer;
        }

        var fftMem = this._register[fftPointer];
        if (fftMem == null)
        {
            return indexPointer;
        }

        chunkSize = chunkSize > 0 ? chunkSize : (int)fftMem.IndexLength.ToInt64();
        bool merged = IsMerged2DVariant(kernel);
        bool perFrame = IsPerFrameVariant(kernel);

        try
        {
            if (merged)
            {
                fftPointer = this.ExecuteMergedTimeStretch(fftMem, chunkSize, overlapSize, sampleRate, factor);
            }
            else if (perFrame)
            {
                fftPointer = this.ExecutePerFrameTimeStretch(fftMem, chunkSize, overlapSize, sampleRate, channels, factor);
            }
            else
            {
                fftPointer = this.ExecuteMergedTimeStretch(fftMem, chunkSize, overlapSize, sampleRate, factor);
            }
        }
        catch (Exception ex)
        {
            CudaLog.Error("Time stretch execution failed", ex.Message);
            return indexPointer;
        }

        if (!transformed)
        {
            return fftPointer;
        }

        var resultPointer = this._fourier.PerformIfft(fftPointer, keep);
        return resultPointer == IntPtr.Zero ? indexPointer : resultPointer;
    }

    public Task<IntPtr> ExecuteTimeStretchAsync(IntPtr pointer, string kernel, double factor, int chunkSize, int overlapSize, int sampleRate, int channels, bool keep = false)
        => Task.Run(() => this.ExecuteTimeStretch(pointer, kernel, factor, chunkSize, overlapSize, sampleRate, channels, keep));

    public Task<IntPtr> ExecuteTimeStretchInterleavedAsync(IntPtr pointer, string kernel, double factor, int chunkSize, int overlapSize, int sampleRate, int channels, int maxStreams = 1, bool keep = false)
        => this.ExecuteTimeStretchAsync(pointer, kernel, factor, chunkSize, overlapSize, sampleRate, channels, keep);

    public IntPtr ExecuteGenericAudioKernel(IntPtr indexPointer, string kernel, int chunkSize, int overlapSize, int sampleRate, int channels = 0, Dictionary<string, object>? additionalArgs = null)
    {
        this._compiler.LoadKernel(kernel, silent: true);
        if (this.Kernel == null)
        {
            CudaLog.Warn("Generic kernel not loaded", kernel);
            return indexPointer;
        }

        var mem = this._register[indexPointer];
        if (mem == null)
        {
            return IntPtr.Zero;
        }

        var arguments = this.SortKernelParameters(kernel, indexPointer, chunkSize, overlapSize, sampleRate, channels, additionalArgs ?? []);
        if (arguments.Length == 0)
        {
            return indexPointer;
        }

        try
        {
            this.Kernel.Run(arguments);
        }
        catch (Exception ex)
        {
            CudaLog.Error("Generic kernel execution failed", ex.Message);
        }

        return indexPointer;
    }

    public Task<IntPtr> ExecuteGenericAudioKernelAsync(IntPtr pointer, string kernel, int chunkSize, int overlapSize, int sampleRate, int channels = 0, Dictionary<string, object>? additionalArgs = null)
        => Task.Run(() => this.ExecuteGenericAudioKernel(pointer, kernel, chunkSize, overlapSize, sampleRate, channels, additionalArgs));

    public TResult[] ExecuteGenericKernelSingle<TResult>(string kernelCodeOrName, object[]? inputData, string? inputDataType = "byte", long outputElementCount = 0, Dictionary<string, string>? arguments = null, int workDimensions = 1, bool freeInput = true, bool freeOutput = true) where TResult : unmanaged
    {
        if (string.IsNullOrWhiteSpace(kernelCodeOrName) || outputElementCount <= 0)
        {
            return [];
        }

        var kernel = this._compiler.CompileLoadKernelFromString(kernelCodeOrName);
        if (kernel == null)
        {
            return [];
        }

        CudaMem? inputMem = null;
        if (inputData is { Length: > 0 } && !string.IsNullOrWhiteSpace(inputDataType))
        {
            inputMem = this.PushGenericInput(inputData, inputDataType);
        }

        var outputMem = this._register.AllocateSingle<TResult>((IntPtr)outputElementCount);
        if (outputMem == null)
        {
            return [];
        }

        object[] mergedArgs = this.MergeGenericKernelArgumentsDynamic(kernelCodeOrName, inputMem?.DevicePointers.FirstOrDefault(), outputMem.DevicePointers.FirstOrDefault(), arguments);
        if (mergedArgs.Length == 0)
        {
            return [];
        }

        ConfigureKernelLaunch(kernel, outputElementCount, workDimensions);
        kernel.Run(mergedArgs);

        var data = this._register.PullData<TResult>(outputMem.IndexPointer, keep: true);

        if (inputMem != null && freeInput)
        {
            this._register.FreeMemory(inputMem);
        }
        if (outputMem != null && freeOutput)
        {
            this._register.FreeMemory(outputMem);
        }

        this._ctx.Synchronize();
        return data ?? [];
    }

    public Task<TResult[]> ExecuteGenericKernelSingleAsync<TResult>(string kernelCode, object[]? inputData, string? inputDataType = "byte", long outputElementCount = 0, Dictionary<string, string>? arguments = null, int workDimensions = 1) where TResult : unmanaged
        => Task.Run(() => this.ExecuteGenericKernelSingle<TResult>(kernelCode, inputData, inputDataType, outputElementCount, arguments, workDimensions));

    public async Task<byte[]?> ExecuteVisualizerFromFftAsync(
        string magnitudeVersion,
        string visualizerVersion,
        IntPtr fftIndexPointer,
        IntPtr magIndexPointer,
        IntPtr pixelIndexPointer,
        int numComplex,
        int width,
        int height,
        float timeSinceStart = 0f,
        Dictionary<string, string>? optionalArgs = null)
    {
        this._compiler.LoadKernel("magnitude" + magnitudeVersion, silent: true);
        var magKernel = this.Kernel;
        if (magKernel == null)
        {
            return null;
        }

        var fftMem = this._register[fftIndexPointer];
        var magMem = this._register[magIndexPointer];
        var pixelMem = this._register[pixelIndexPointer];
        if (fftMem == null || magMem == null || pixelMem == null)
        {
            return null;
        }

        ConfigureKernelLaunch(magKernel, numComplex, 1);
        object[] magArgs =
        [
            fftMem.DevicePointers[0],
            magMem.DevicePointers[0],
            numComplex
        ];
        magKernel.Run(magArgs);
        this._ctx.Synchronize();

        this._compiler.LoadKernel("visualizer" + visualizerVersion, silent: true);
        var visKernel = this.Kernel;
        if (visKernel == null)
        {
            return null;
        }

        var argDefs = this._compiler.GetArguments("visualizer" + visualizerVersion);
        Dictionary<string, string> visArgs = new(StringComparer.OrdinalIgnoreCase)
        {
            { argDefs.Keys.FirstOrDefault(k => k.Contains("num", StringComparison.OrdinalIgnoreCase)) ?? "num", numComplex.ToString() },
            { argDefs.Keys.FirstOrDefault(k => k.Contains("width", StringComparison.OrdinalIgnoreCase)) ?? "width", width.ToString() },
            { argDefs.Keys.FirstOrDefault(k => k.Contains("height", StringComparison.OrdinalIgnoreCase)) ?? "height", height.ToString() },
            { argDefs.Keys.FirstOrDefault(k => k.Contains("time", StringComparison.OrdinalIgnoreCase)) ?? "time", timeSinceStart.ToString(CultureInfo.InvariantCulture) }
        };

        if (optionalArgs != null)
        {
            foreach (var kv in optionalArgs)
            {
                visArgs[kv.Key] = kv.Value;
            }
        }

        var sortedArgs = this.MergeGenericKernelArgumentsDynamic("visualizer" + visualizerVersion, magMem.DevicePointers[0], pixelMem.DevicePointers[0], visArgs);
        visKernel.BlockDimensions = new dim3(16, 16, 1);
        visKernel.GridDimensions = new dim3((uint)((width + 15) / 16), (uint)((height + 15) / 16), 1);
        visKernel.Run(sortedArgs);

        this._ctx.Synchronize();
        return await this._register.PullDataAsync<byte>(pixelIndexPointer).ConfigureAwait(false);
    }

    private CudaMem? PushGenericInput(object[] inputData, string inputDataType)
    {
        try
        {
            return inputDataType.ToLowerInvariant() switch
            {
                "byte" or "bytes" or "uint8" => this._register.PushData((IEnumerable<byte>)inputData.Cast<byte>()),
                "int" or "int32" => this._register.PushData((IEnumerable<int>)inputData.Cast<int>()),
                "float" or "single" => this._register.PushData((IEnumerable<float>)inputData.Cast<float>()),
                "double" => this._register.PushData((IEnumerable<double>)inputData.Cast<double>()),
                _ => null
            };
        }
        catch
        {
            return null;
        }
    }

    private static void ConfigureKernelLaunch(CudaKernel kernel, long totalElements, int workDimensions)
    {
        switch (workDimensions)
        {
            case 1:
                kernel.BlockDimensions = new dim3(256, 1, 1);
                kernel.GridDimensions = new dim3((uint)((totalElements + 255) / 256), 1, 1);
                break;
            case 2:
                kernel.BlockDimensions = new dim3(16, 16, 1);
                uint width = (uint)Math.Ceiling(Math.Sqrt(totalElements));
                kernel.GridDimensions = new dim3((uint)((width + 15) / 16), (uint)((width + 15) / 16), 1);
                break;
            default:
                kernel.BlockDimensions = new dim3(8, 8, 8);
                uint edge = (uint)Math.Ceiling(Math.Pow(totalElements, 1.0 / 3.0));
                kernel.GridDimensions = new dim3((uint)((edge + 7) / 8), (uint)((edge + 7) / 8), (uint)((edge + 7) / 8));
                break;
        }
    }

    public object[] SortKernelParameters(string kernel, IntPtr dataPointer, int chunkSize, int overlapSize, int sampleRate, int channels, Dictionary<string, object> additionalArgs)
    {
        var cu = this._compiler.SourceFiles.FirstOrDefault(f => Path.GetFileNameWithoutExtension(f).Equals(kernel, StringComparison.OrdinalIgnoreCase));
        var args = this._compiler.GetArguments(cu ?? kernel, log: false);
        var mem = this._register[dataPointer];
        if (mem == null)
        {
            return [];
        }

        var sorted = new object[args.Count];
        for (int i = 0; i < args.Count; i++)
        {
            string name = args.ElementAt(i).Key.ToLowerInvariant();
            Type type = args.ElementAt(i).Value;

            if (type == typeof(CUdeviceptr))
            {
                sorted[i] = mem.DevicePointers.FirstOrDefault();
            }
            else if (type == typeof(int))
            {
                sorted[i] = name switch
                {
                    var n when n.Contains("chunk") => chunkSize,
                    var n when n.Contains("overlap") => overlapSize,
                    var n when n.Contains("sample") => sampleRate,
                    var n when n.Contains("chan") => channels,
                    _ => 0
                };
            }
            else if (additionalArgs.TryGetValue(args.ElementAt(i).Key, out var value))
            {
                sorted[i] = Convert.ChangeType(value, type, CultureInfo.InvariantCulture);
            }
            else
            {
                sorted[i] = Activator.CreateInstance(type) ?? 0;
            }
        }

        return sorted;
    }

    public object[] MergeGenericKernelArgumentsDynamic(string kernelCode, CUdeviceptr? inputBuffer = null, CUdeviceptr? outputBuffer = null, Dictionary<string, string>? arguments = null)
    {
        var argDefs = this._compiler.GetArguments(kernelCode);
        if (argDefs.Count == 0)
        {
            return [];
        }

        Dictionary<string, string> provided = arguments != null
            ? new Dictionary<string, string>(arguments, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var merged = new object[argDefs.Count];
        int pointerCount = 0;
        for (int i = 0; i < argDefs.Count; i++)
        {
            var (name, type) = argDefs.ElementAt(i);
            if (type == typeof(CUdeviceptr))
            {
                merged[i] = pointerCount switch
                {
                    0 when inputBuffer.HasValue => inputBuffer.Value,
                    1 when outputBuffer.HasValue => outputBuffer.Value,
                    _ => new CUdeviceptr()
                };
                pointerCount++;
                continue;
            }

            if (provided.TryGetValue(name, out var raw))
            {
                merged[i] = ParseScalar(type, raw);
            }
            else
            {
                merged[i] = type.IsValueType ? Activator.CreateInstance(type)! : 0;
            }
        }

        return merged;
    }

    private static object ParseScalar(Type type, string raw)
    {
        if (type == typeof(int) && int.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var i))
        {
            return i;
        }
        if (type == typeof(float) && float.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var f))
        {
            return f;
        }
        if (type == typeof(double) && double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var d))
        {
            return d;
        }
        if (type == typeof(long) && long.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var l))
        {
            return l;
        }
        if (type == typeof(uint) && uint.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var u))
        {
            return u;
        }
        if (type == typeof(bool))
        {
            if (raw == "0")
            {
                return false;
            }
            if (raw == "1")
            {
                return true;
            }
            return bool.TryParse(raw, out var b) && b;
        }

        return 0;
    }

    private static bool IsMerged2DVariant(string kernelName)
    {
        kernelName = kernelName.ToLowerInvariant();
        return kernelName.Contains("merged", StringComparison.Ordinal) || kernelName.Contains("double03", StringComparison.Ordinal);
    }

    private static bool IsPerFrameVariant(string kernelName)
    {
        kernelName = kernelName.ToLowerInvariant();
        return kernelName.Contains("complex", StringComparison.Ordinal) || kernelName.Contains("pvoc", StringComparison.Ordinal);
    }

    private IntPtr ExecuteMergedTimeStretch(CudaMem fftMem, int chunkSize, int overlapSize, int sampleRate, double factor)
    {
        var contiguousIn = this._register.AllocateSingle<float2>((IntPtr)((long)fftMem.Count * chunkSize));
        var contiguousOut = this._register.AllocateSingle<float2>((IntPtr)((long)fftMem.Count * chunkSize));
        if (contiguousIn == null || contiguousOut == null)
        {
            return fftMem.IndexPointer;
        }

        try
        {
            for (int i = 0; i < fftMem.Count; i++)
            {
                long byteSize = chunkSize * sizeof(float) * 2L;
                long offset = byteSize * i;
                CopyDeviceToDevice(new CUdeviceptr(contiguousIn.IndexPointer + offset), fftMem.DevicePointers[i], byteSize);
            }

            var kernel = this.Kernel!;
            kernel.BlockDimensions = new dim3(32, 4, 1);
            kernel.GridDimensions = new dim3((uint)((chunkSize + 31) / 32), (uint)((fftMem.Count + 3) / 4), 1);
            object[] args =
            [
                new CUdeviceptr(contiguousIn.IndexPointer),
                new CUdeviceptr(contiguousOut.IndexPointer),
                chunkSize,
                overlapSize,
                sampleRate,
                factor,
                fftMem.Count
            ];
            kernel.Run(args);

            for (int i = 0; i < fftMem.Count; i++)
            {
                long byteSize = chunkSize * sizeof(float) * 2L;
                long offset = byteSize * i;
                CopyDeviceToDevice(fftMem.DevicePointers[i], new CUdeviceptr(contiguousOut.IndexPointer + offset), byteSize);
            }
        }
        finally
        {
            this._register.FreeMemory(contiguousIn);
            this._register.FreeMemory(contiguousOut);
        }

        return fftMem.IndexPointer;
    }

    private IntPtr ExecutePerFrameTimeStretch(CudaMem fftMem, int chunkSize, int overlapSize, int sampleRate, int channels, double factor)
    {
        var kernel = this.Kernel!;
        kernel.BlockDimensions = new dim3(256, 1, 1);
        kernel.GridDimensions = new dim3((uint)((chunkSize * channels + 255) / 256), 1, 1);

        var temp = this._register.AllocateSingle<float2>((IntPtr)chunkSize);
        if (temp == null)
        {
            return fftMem.IndexPointer;
        }

        try
        {
            for (int i = 0; i < fftMem.Count; i++)
            {
                object[] args =
                [
                    fftMem.DevicePointers[i],
                    temp.DevicePointers[0],
                    chunkSize,
                    overlapSize,
                    sampleRate,
                    channels,
                    factor
                ];
                kernel.Run(args);
                CopyDeviceToDevice(fftMem.DevicePointers[i], temp.DevicePointers[0], chunkSize * sizeof(float) * 2L);
            }
        }
        finally
        {
            this._register.FreeMemory(temp);
        }

        return fftMem.IndexPointer;
    }

    private static void CopyDeviceToDevice(CUdeviceptr dst, CUdeviceptr src, long byteSize)
    {
        var result = ManagedCuda.DriverAPINativeMethods.SynchronousMemcpy_v2.cuMemcpyDtoD_v2(dst, src, (SizeT)byteSize);
        if (result != CUResult.Success)
        {
            throw new CudaException(result);
        }
    }
}