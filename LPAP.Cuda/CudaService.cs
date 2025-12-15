using LPAP.Audio;
using ManagedCuda;
using ManagedCuda.BasicTypes;
using ManagedCuda.VectorTypes;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace LPAP.Cuda
{
	public class CudaService
	{
		// Objects
		private CudaRegister? Register;
		private CudaFourier? Fourier;
		private CudaCompiler? Compiler;
		private CudaExecutioner? Executioner;

		// Fields
		private PrimaryContext? CTX;
		private CUdevice? DEV;

		public CudaKernel? Kernel => this.Compiler?.Kernel;
		public string KernelPath => this.Compiler?.KernelPath ?? string.Empty;


		// UI
		public double GpuLoadPercent => this.CTX != null ? GpuStats.GetGpuLoadAsync(this.Index).GetAwaiter().GetResult() ?? 0 : 0;
		public double TotalMemoryMb => this.Register != null  ? this.Register.TotalMemory / (1024.0 * 1024.0) : 0;
		public double AvailableMemoryMb => this.Register != null ? this.Register.TotalMemoryAvailable / (1024.0 * 1024.0) : 0;
		public double UsedMemoryMb => this.TotalMemoryMb - this.AvailableMemoryMb;
		public double AllocatedMemoryMb => this.Register != null ? this.Register.TotalMemoryAllocated / (1024.0 * 1024.0) : 0;


		// Attributes
		public string RuntimePath { get; set; } = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "LPAP.Cuda"));
		public bool Initialized => this.Register != null && this.Fourier != null && this.Compiler != null && this.Executioner != null && this.CTX != null && this.DEV != null;
		public int Index { get; private set; } = -1;
		public Dictionary<CUdevice, string> Devices { get; private set; } = [];

		public BindingList<string> DeviceEntries = [];
		public string SelectedDevice => this.Index >= 0 && this.Index < this.DeviceEntries.Count ? this.Devices.Values.ElementAt(this.Index) : string.Empty;
		public long AllocatedMemory => this.Register?.TotalMemoryAllocated ?? 0;
		public int RegisteredMemoryObjects => this.Register?.RegisteredMemoryObjects ?? 0;
		public int MaxThreads = 0;
		public int ThreadsActive => this.Register?.ThreadsActive ?? 0;
		public int ThreadsIdle => this.Register?.ThreadsIdle ?? 0;

		public static BindingList<string> LogEntries { get; set; } = [];
		public static int MaxLogEntries { get; set; } = 1024;
		public static string LogFilePath = string.Empty;
		public static event EventHandler? LogEntryAdded;
		public static bool AggregateSameEntries { get; set; } = false;


		// Constructor
		public CudaService(int index = -1, string device = "RTX", bool logToFile = true)
		{
			this.Devices = this.GetDevices();
			this.DeviceEntries = new BindingList<string>(this.Devices.Values
						.Select((name, idx) => $"[{idx}] {name}")
						.OrderBy(name => name)
						.ToList());

			// Verify runtime path
			if (!Directory.Exists(this.RuntimePath))
			{
				this.RuntimePath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory));
			}

			// Create / overwrite log file
			if (logToFile)
			{
				LogFilePath = Path.Combine(this.RuntimePath, "AsynCUDA.log");

				try
				{
					if (File.Exists(LogFilePath))
					{
						File.Delete(LogFilePath);
					}

					using var logFile = File.CreateText(LogFilePath);
					logFile.WriteLine("AsynCUDA Runtime Log");
					logFile.WriteLine($"Initialized at: {DateTime.Now}");
					logFile.WriteLine(new string('-', 32));
				}
				catch (Exception ex)
				{
					Console.WriteLine($"Failed to create log file: {ex.Message}", "Error", 1);
				}
			}

			// Initialize
			if (index > 0)
			{
				this.Initialize(index);
			}
			else if (!string.IsNullOrEmpty(device))
			{
				this.Initialize(device);
			}
		}

		// Method: Dispose
		public void Dispose()
		{
			this.Index = -1;

			this.Devices = this.GetDevices();
			this.DeviceEntries = new BindingList<string>(this.Devices.Values
						.Select((name, idx) => $"[{idx}] {name}")
						.OrderBy(name => name)
						.ToList());

			this.CTX?.Dispose();
			this.CTX = null;
			this.DEV = null;

			this.Register?.Dispose();
			this.Register = null;
			this.Fourier?.Dispose();
			this.Fourier = null;
			this.Compiler?.Dispose();
			this.Compiler = null;
			this.Executioner?.Dispose();
			this.Executioner = null;
		}


		// Method: Log (static)
		public static string Log(string message = "", string inner = "", int indent = 0, string? invoker = null, bool addTimeStamp = true)
		{
			if (string.IsNullOrEmpty(invoker))
			{
				// Get the calling class name
				var stackTrace = new StackTrace();
				var frame = stackTrace.GetFrame(1);
				invoker = frame?.GetMethod()?.DeclaringType?.Name ?? "Unknown";
			}

			// PadRight / cut off invoker to 12 characters
			invoker = invoker.PadRight(12).Substring(0, 12);

			// Time stamp as HH:mm:ss.fff (24h)
			string timeStamp = DateTime.Now.ToString("HH:mm:ss.fff");
			
			// Indentation
			string indentString = new(' ', indent * 2);

			string logMessage = $"[{invoker}{(addTimeStamp ? @" @" + timeStamp : "")}] {indentString}{message} {(string.IsNullOrEmpty(inner) ? "" : $"({inner})")}";

			Console.WriteLine(logMessage);
			LogEntries.Add(logMessage);

			if (LogEntries.Count > MaxLogEntries)
			{
				LogEntries.RemoveAt(0);
			}

			LogEntryAdded?.Invoke(null, EventArgs.Empty);

			// Write to log file
			try
			{
				using var logFile = new StreamWriter(LogFilePath, true);
				logFile.WriteLine(logMessage);
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Failed to write to log file: {ex.Message}", "Error", 1);
			}

			return logMessage;
		}

		public static string Log(Exception exception, string? description = null, int indent = 0, string? invoker = null, bool addTimeStamp = true)
		{
			if (string.IsNullOrEmpty(invoker))
			{
				// Get the calling class name
				var stackTrace = new StackTrace();
				var frame = stackTrace.GetFrame(1);
				invoker = frame?.GetMethod()?.DeclaringType?.Name ?? "Unknown";
			}

			// Time stamp as HH:mm:ss.fff (24h)
			string timeStamp = DateTime.Now.ToString("HH:mm:ss.fff");

			// Indentation
			string indentString = new(' ', indent * 2);

			// Create folded message
			string message = string.Empty;
			if (!string.IsNullOrEmpty(description))
			{
				message += $"{description.Replace(":", "").Trim()}: ";
			}

			message += exception.Message;
			Exception? inner = exception.InnerException;
			int innerCount = 0;
			while (inner != null)
			{
				message += $" ({inner.Message}";
				inner = inner.InnerException;
				innerCount++;
			}
			if (innerCount > 0)
			{
				message += new string(')', innerCount);
			}

			string logMessage = $"[{invoker}{(addTimeStamp ? @" @" + timeStamp : "")}] {indentString}{message}";

			Console.WriteLine(logMessage);
			LogEntries.Add(logMessage);

			if (LogEntries.Count > MaxLogEntries)
			{
				LogEntries.RemoveAt(0);
			}

			LogEntryAdded?.Invoke(null, EventArgs.Empty);

			// Write to log file
			try
			{
				using var logFile = new StreamWriter(LogFilePath, true);
				logFile.WriteLine(logMessage);
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Failed to write to log file: {ex.Message}", "Error", 1);
			}

			return logMessage;
		}


		// Methods: Initialize
		public void Initialize(int index = 0)
		{
			if (this.Initialized)
			{
				this.Dispose();
			}

			if (index < 0 || index >= this.Devices.Count)
			{
				return;
			}

			try
			{
				this.Index = index;
				this.DEV = this.Devices.Keys.ElementAt(index);
				this.CTX = new PrimaryContext(this.DEV.Value);

				this.MaxThreads = this.CTX.GetDeviceInfo().MaxThreadsPerMultiProcessor;

				this.CTX.SetCurrent();

				Log($"Initialized CUDA service for device: {this.SelectedDevice} (Index: {this.Index})", "", 0, "CUDA-Service", true);

				this.Register = new CudaRegister(this.CTX);
				this.Fourier = new CudaFourier(this.CTX, this.Register);
				this.Compiler = new CudaCompiler(this.CTX);
				this.Executioner = new CudaExecutioner(this.CTX, this.Register, this.Fourier, this.Compiler);
			}
			catch (Exception ex)
			{
				Log(ex, "Failed to initialize CUDA service", 0, "CUDA-Service", true);
				this.Dispose();
				return;
			}
		}

		public void Initialize(string device = "NVIDIA")
		{
			int index = this.Devices.Values
				.Select((name, idx) => new { Name = name, Index = idx })
				.FirstOrDefault(x => x.Name.Contains(device, StringComparison.OrdinalIgnoreCase))?.Index ?? -1;

			this.Initialize(index);
		}


		// Method: Devices enumeration 
		internal Dictionary<CUdevice, string> GetDevices(bool silent = true)
		{
			var devices = new Dictionary<CUdevice, string>();
			
			try
			{
				int deviceCount = CudaContext.GetDeviceCount();

				for (int i = 0; i < deviceCount; i++)
				{
					CUdevice device = new(i);
					string deviceName = CudaContext.GetDeviceName(i);
					devices.Add(device, deviceName);
				}

				if (!silent)
				{
					Log($"Found {deviceCount} CUDA devices.", "", 0, "CUDA-Service", true);
				}
			}
			catch (Exception ex)
			{
				Log(ex, "Failed to enumerate CUDA devices", 0, "CUDA-Service", true);
			}

			return devices;
		}
		
		public Dictionary<string, Type>? GetKernelArgumentDefinitions(string? kernelName)
		{
			if (this.Compiler == null || string.IsNullOrEmpty(kernelName))
			{
				return null;
			}

			return this.Compiler.GetKernelArguments(kernelName);
		}



		// Accessors: Kernel
		public IEnumerable<string> GetAvailableKernels(string? filter = null)
		{
			if (this.Compiler == null)
			{
				return [];
			}

			return this.Compiler.SourceFiles.Select(f => Path.GetFileNameWithoutExtension(f))
				.Where(name => string.IsNullOrEmpty(filter) || name.Contains(filter, StringComparison.OrdinalIgnoreCase))
				.OrderBy(name => name);
		}

		public string? GetLatestKernel(IEnumerable<string>? fromKernels = null)
		{
			fromKernels ??= this.Compiler?.SourceFiles ?? [];
			List<string> kernelFiles = [];
			foreach (var kernel in fromKernels ?? [])
			{
				if (!File.Exists(kernel))
				{
					kernelFiles.Add(Path.Combine(this.Compiler?.KernelPath ?? "", "CU", kernel.Replace(".cu", "") + ".cu"));
				}
			}

			var fileInfos = kernelFiles
				.Select(f => new FileInfo(f))
				.Where(fi => fi.Exists)
				.OrderByDescending(fi => fi.LastWriteTime)
				.ToList();

			if (fileInfos.Count == 0)
			{
				Console.WriteLine("No valid kernel files found.", "Error", 1);
				return null;
			}

			var latestFile = Path.GetFileNameWithoutExtension(fileInfos.First().FullName);
			return latestFile;
		}

		public CudaKernel? LoadKernel(string kernelName)
		{
			if (this.Compiler == null)
			{
				return null;
			}

			var kernel = this.Compiler.LoadKernel(kernelName);
			if (kernel == null)
			{
				string? kernelFile = this.Compiler.SourceFiles.FirstOrDefault(f => Path.GetFileNameWithoutExtension(f).Equals(kernelName, StringComparison.OrdinalIgnoreCase));
				if (string.IsNullOrEmpty(kernelFile))
				{
					Console.WriteLine($"Kernel '{kernelName}' not found in source files.", "Error", 1);
					return null;
				}

				string? ptxFile = this.Compiler.CompileKernel(kernelFile);
				if (string.IsNullOrEmpty(ptxFile))
				{
					Console.WriteLine($"Kernel '{kernelName}' could not be compiled.", "Error", 1);
					return null;
				}

				kernel = this.Compiler.LoadKernel(kernelName);
				if (kernel == null)
				{
					Console.WriteLine($"Kernel '{kernelName}' could not be loaded from compiled PTX file.", "Error", 1);
					return null;
				}
			}

			if (kernel == null)
			{
				Console.WriteLine($"Kernel '{kernelName}' could not be loaded.", "Error", 1);
				return null;
			}

			return this.Kernel;
		}

		public string? CompileKernel(string kernelNameOrFile)
		{
			if (this.Compiler == null)
			{
				return null;
			}

			if (File.Exists(kernelNameOrFile))
			{
				return this.Compiler.CompileKernel(kernelNameOrFile);
			}
			else
			{
				kernelNameOrFile = this.Compiler.KernelPath + kernelNameOrFile + ".cu";
				if (File.Exists(kernelNameOrFile))
				{
					return this.Compiler.CompileKernel(kernelNameOrFile);
				}
				else
				{
					Console.WriteLine($"Kernel file '{kernelNameOrFile}' does not exist.", "Error", 1);
					return null;
				}
			}

		}



		// Accessors: Move AudioObj
		public AudioObj MoveAudio(AudioObj obj, int chunkSize = 16384, float overlap = 0.5f, bool keep = false)
		{
			if (this.Register == null)
			{
				return obj;
			}

			Stopwatch sw = Stopwatch.StartNew();
			if (obj.OnHost && obj.Data.LongLength > 0)
			{
				obj.IsProcessing = true;
				
				// Move -> Device
				var chunks = obj.GetChunksAsync(chunkSize, overlap, 4, keep).Result;
				if (chunks == null || !chunks.Any())
				{
					sw.Stop();
					return obj;
				}

				obj["chunk"] = sw.Elapsed.TotalMilliseconds;
				sw.Restart();

				var mem = this.Register.PushChunks(chunks);
				if (mem == null || mem.IndexPointer == nint.Zero)
				{
					sw.Stop();
					return obj;
				}

				obj["push"] = sw.Elapsed.TotalMilliseconds;
				obj.Pointer = mem.IndexPointer;
			}
			else if (obj.OnDevice && obj.Pointer != nint.Zero)
			{
				obj.IsProcessing = true;
				
				// Move -> Host
				var chunks = this.Register.PullChunks<float>(obj.Pointer, keep);
				if (chunks == null || chunks.LongCount() <= 0)
				{
					sw.Stop();
					return obj;
				}

				obj["pull"] = sw.Elapsed.TotalMilliseconds;
				sw.Restart();
				
				obj.AggregateStretchedChunksAsync(chunks, 4, keep).Wait();
				obj["aggregate"] = sw.Elapsed.TotalMilliseconds;
			}

			sw.Stop();
			obj.IsProcessing = false;
			return obj;
		}

		public async Task<AudioObj> MoveAudioAsync(AudioObj obj, int chunkSize = 16384, float overlap = 0.5f, bool keep = false)
		{
			if (this.Register == null)
			{
				return obj;
			}

			Stopwatch sw = Stopwatch.StartNew();

			if (obj.OnHost && obj.Data.LongLength > 0)
			{
				obj.IsProcessing = true;

				// Move -> Device
				var chunks = await obj.GetChunksAsync(chunkSize, overlap, 4, keep);
				if (chunks == null || !chunks.Any())
				{
					sw.Stop();
					return obj;
				}

				obj["chunk"] = sw.Elapsed.TotalMilliseconds;
				sw.Restart();

				var mem = await this.Register.PushChunksAsync(chunks);
				if (mem == null || mem.IndexPointer == nint.Zero)
				{
					sw.Stop();
					return obj;
				}
				obj["push"] = sw.Elapsed.TotalMilliseconds;

				obj.Pointer = mem.IndexPointer;
			}
			else if (obj.OnDevice && obj.Pointer != nint.Zero)
			{
				obj.IsProcessing = true;

				// Move -> Host
				var chunks = await this.Register.PullChunksAsync<float>(obj.Pointer, keep);
				if (chunks == null || chunks.LongCount() <= 0)
				{
					sw.Stop();
					return obj;
				}
				obj["pull"] = sw.Elapsed.TotalMilliseconds;
				sw.Restart();

				await obj.AggregateStretchedChunksAsync(chunks, 4, keep);
				obj["aggregate"] = sw.Elapsed.TotalMilliseconds;
			}

			sw.Stop();
			obj.IsProcessing = false;
			return obj;
		}


		// Accessors: Fourier Transform
		public AudioObj FourierTransform(AudioObj obj, int chunkSize = 16384, float overlap = 0.5f, bool keep = false, bool autoPull = false, bool autoNormalize = false, bool asyncFourier = true)
		{
			if (this.Fourier == null || this.Register == null)
			{
				return obj;
			}

			// Move audio to device if not already there
			if (!obj.OnDevice)
			{
				this.MoveAudio(obj, chunkSize, overlap, keep);
			}
			if (obj.Pointer == nint.Zero)
			{
				return obj;
			}

			Stopwatch sw = Stopwatch.StartNew();

			// Perform Fourier Transform
			IntPtr transformedPointer = nint.Zero;
			if (obj.Form == "f")
			{
				obj.IsProcessing = true;

				transformedPointer = asyncFourier
					? this.FourierTransformAsync(obj, chunkSize, overlap, keep, false, autoPull, autoNormalize).GetAwaiter().GetResult().Pointer
					: this.Fourier.PerformFft(obj.Pointer, keep);

				if (transformedPointer != nint.Zero)
				{
					obj.Pointer = transformedPointer;
					obj.Form = "c";
				}
			}
			else if (obj.Form == "c")
			{
				obj.IsProcessing = true;

				transformedPointer = this.Fourier.PerformIfft(obj.Pointer, keep);
				if (transformedPointer != nint.Zero)
				{
					obj.Pointer = transformedPointer;
					obj.Form = "f";
				}
			}
			else
			{
				sw.Stop();
				return obj;
			}

			sw.Stop();
			obj.IsProcessing = false;

			if (autoPull && obj.Form == "f")
			{
				this.MoveAudio(obj, chunkSize, overlap, keep);

				if (autoNormalize)
				{
					obj.NormalizeAsync(1.0f).Wait();
				}
			}

			return obj;
		}

		public async Task<AudioObj> FourierTransformAsync(AudioObj obj, int chunkSize = 16384, float overlap = 0.5f, bool keep = false, bool asMany = false, bool autoPull = false, bool autoNormalize = false)
		{
			if (this.Fourier == null || this.Register == null)
			{
				return obj;
			}

			// Move audio to device if not already there
			if (!obj.OnDevice)
			{
				await this.MoveAudioAsync(obj, chunkSize, overlap, keep);
			}
			if (obj.Pointer == nint.Zero)
			{
				return obj;
			}

			Stopwatch sw = Stopwatch.StartNew();

			// Perform Fourier Transform
			IntPtr transformedPointer;
			if (obj.Form == "f")
			{
				obj.IsProcessing = true;

				transformedPointer = asMany ? await this.Fourier.PerformFftManyAsync(obj.Pointer, keep) : await this.Fourier.PerformFftAsync(obj.Pointer, keep);

				obj["fft"] = sw.Elapsed.TotalMilliseconds;
			}
			else if (obj.Form == "c")
			{
				obj.IsProcessing = true;

				transformedPointer = asMany ? await this.Fourier.PerformIfftManyAsync(obj.Pointer, keep) : await this.Fourier.PerformIfftAsync(obj.Pointer, keep);

				obj["ifft"] = sw.Elapsed.TotalMilliseconds;
			}
			else
			{
				sw.Stop();
				return obj;
			}

			sw.Stop();
			obj.IsProcessing = false;

			if (transformedPointer == nint.Zero)
			{
				Console.WriteLine("Fourier Transform failed, pointer is null.");
				return obj;
			}

			obj.Pointer = transformedPointer;
			obj.Form = obj.Form == "f" ? "c" : "f";

			if (autoPull && obj.Form == "f")
			{
				await this.MoveAudioAsync(obj, chunkSize, overlap, false);

				if (autoNormalize)
				{
					await obj.NormalizeAsync(1.0f);
				}
			}

			return obj;
		}


		// Accessors: Time Stretch
		public AudioObj TimeStretch(AudioObj obj, string kernel = "timestretch00", double factor = 1.0, int chunkSize = 16384, float overlap = 0.5f, bool keep = false, bool autoNormalize = false)
		{
			if (this.Executioner == null || this.Compiler == null || this.Fourier == null || this.Register == null)
			{
				return obj;
			}

			// Move audio to device if not already there
			if (!obj.OnDevice)
			{
				this.MoveAudio(obj, chunkSize, overlap, keep);
			}
			if (obj.Pointer == nint.Zero)
			{
				return obj;
			}

			int overlapSize = (int)(chunkSize * overlap);
			IntPtr result = nint.Zero;

			Stopwatch sw = Stopwatch.StartNew();

			obj.IsProcessing = true;
			result = this.Executioner.ExecuteTimeStretch(obj.Pointer, kernel, factor, chunkSize, overlapSize, obj.SampleRate, keep);
			obj.IsProcessing = false;

			sw.Stop();
			obj["stretch"] = sw.Elapsed.TotalMilliseconds;

			if (result != nint.Zero)
			{
				obj.Pointer = result;
				obj.StretchFactor = factor;
			}
			
			if (!obj.OnHost)
			{
				this.MoveAudio(obj, chunkSize, overlap);
			}
			
			if (autoNormalize)
			{
				obj.NormalizeAsync(1.0f).Wait();
			}

			return obj;
		}

		public async Task<AudioObj> TimeStretchAsync(AudioObj obj, string kernel = "timestretch00", double factor = 1.0, int chunkSize = 16384, float overlap = 0.5f, int maxStreams = 1, bool keep = false, bool asMany = false, bool autoNormalize = false)
		{
			if (this.Executioner == null || this.Compiler == null || this.Fourier == null || this.Register == null)
			{
				return obj;
			}

			// Move audio to device if not already there
			if (!obj.OnDevice)
			{
				await this.MoveAudioAsync(obj, chunkSize, overlap, keep);
			}
			if (obj.Pointer == nint.Zero)
			{
				return obj;
			}

			int overlapSize = (int)(chunkSize * overlap);

			obj.IsProcessing = true;
			IntPtr result = nint.Zero;

			Stopwatch sw = Stopwatch.StartNew();

			result = maxStreams == 1
				? await this.Executioner.ExecuteTimeStretchLinearAsync(obj.Pointer, kernel, factor, chunkSize, overlapSize, obj.SampleRate, asMany, keep)
				: await this.Executioner.ExecuteTimeStretchInterleavedAsync(obj.Pointer, kernel, factor, chunkSize, overlapSize, obj.SampleRate, maxStreams, asMany, keep);

			sw.Stop();
			obj["stretch"] = sw.Elapsed.TotalMilliseconds;

			obj.IsProcessing = false;

			if (result != nint.Zero)
			{
				obj.Pointer = result;
				obj.StretchFactor = factor;
			}

			if (!obj.OnHost)
			{
				await this.MoveAudioAsync(obj, chunkSize, overlap);
			}

			if (autoNormalize)
			{
				await obj.NormalizeAsync(1.0f);
			}

			return obj;
		}



		// Accessors: Generic Kernel Execution
		public async Task<AudioObj> ExecuteGenericAudioKernelAsync<T>(
	AudioObj obj,
	string kernel = "timestretch00",
	double factor = 1.0,
	int chunkSize = 16384,
	float overlap = 0.5f,
	bool autoInverseFft = false,
	bool keep = false,
	bool autoNormalize = false) where T : unmanaged
		{
			if (this.Executioner == null || this.Compiler == null || this.Register == null || this.Fourier == null)
			{
				return obj;
			}

			// Kernel laden
			var loaded = this.Compiler.LoadKernel(kernel);
			if (loaded == null)
			{
				CudaService.Log($"Kernel '{kernel}' konnte nicht geladen werden.");
				return obj;
			}

			// Erkennen, ob Kernel komplexe Eingaben erwartet
			string? cuCode = this.Compiler.KernelCode;
			bool expectsFloat2 = !string.IsNullOrEmpty(cuCode)
				&& (cuCode.Contains("float2*") || cuCode.Contains("Float2*"));

			// Audio → Device falls nötig
			if (!obj.OnDevice)
			{
				await this.MoveAudioAsync(obj, chunkSize, overlap, keep);
			}
			if (obj.Pointer == nint.Zero)
			{
				return obj;
			}

			// Optional FFT (wenn Kernel float2 erwartet und Daten aktuell float sind)
			bool transformedToFloat2 = false;
			if (expectsFloat2 && obj.Form != "c")
			{
				var fftPtr = await this.Fourier.PerformFftAsync(obj.Pointer, keep);
				if (fftPtr == nint.Zero)
				{
					CudaService.Log("FFT fehlgeschlagen.");
					return obj;
				}
				obj.Pointer = fftPtr;
				obj.Form = "c";
				transformedToFloat2 = true;
			}

			// Kernel-Argumente (benannte) vorbereiten; Executioner ordnet sie gemäß .cu
			var namedArgs = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
	{
		{ "factor", factor },
		{ "sampleRate", obj.SampleRate },
		{ "channels", obj.Channels },
		{ "bitdepth", obj.BitDepth }
	};

			// Generischer Kernel-Start über Executioner
			IntPtr resultPtr = await this.Executioner.ExecuteGenericAudioKernelAsync<T>(
				obj.Pointer, kernel, chunkSize, overlap, keep, namedArgs);

			if (resultPtr == nint.Zero)
			{
				CudaService.Log("Generische Kernel-Ausführung ergab null-Pointer.");
				return obj;
			}

			// Optional IFFT zurück auf float
			if (transformedToFloat2 && autoInverseFft)
			{
				var ifftPtr = await this.Fourier.PerformIfftAsync(resultPtr, keep);
				if (ifftPtr == nint.Zero)
				{
					CudaService.Log("Inverse FFT fehlgeschlagen.");
					return obj;
				}
				resultPtr = ifftPtr;
				obj.Form = "f";
			}

			// Ergebnis übernehmen
			obj.Pointer = resultPtr;

			// Falls Ergebnis float ist und zurück auf Host soll: ziehen & aggregieren
			var outMem = this.Register[resultPtr];
			if (outMem != null && outMem.ElementType == typeof(float))
			{
				var chunks = await this.Register.PullChunksAsync<float>(obj.Pointer, keep);
				if (chunks != null && chunks.Any())
				{
					await obj.AggregateStretchedChunksAsync(chunks, 4);
					if (autoNormalize)
					{
						await obj.NormalizeAsync(1.0f);
					}
				}
			}

			return obj;
		}

		// Nicht-generische Variante: Typ automatisch bestimmen, Default = float
		public async Task<AudioObj> ExecuteGenericAudioKernelAsync(
			AudioObj obj,
			string kernel = "timestretch00",
			double factor = 1.0,
			int chunkSize = 16384,
			float overlap = 0.5f,
			bool autoInverseFft = false,
			bool keep = false,
			bool autoNormalize = false)
		{
			// Kernel laden, um zu prüfen ob float2 nötig ist
			if (this.Compiler == null || this.Executioner == null || this.Register == null || this.Fourier == null)
			{
				return obj;
			}

			var loaded = this.Compiler.LoadKernel(kernel);
			if (loaded == null)
			{
				CudaService.Log($"Kernel '{kernel}' konnte nicht geladen werden.");
				return obj;
			}

			string? cuCode = this.Compiler.KernelCode;
			bool expectsFloat2 = !string.IsNullOrEmpty(cuCode)
				&& (cuCode.Contains("float2*") || cuCode.Contains("Float2*"));

			// Typwahl:
			// - wenn Kernel komplexe Eingaben erwartet oder obj.Form == "c" → float2
			// - sonst → float
			if (expectsFloat2 || obj.Form == "c")
			{
				return await this.ExecuteGenericAudioKernelAsync<float2>(obj, kernel, factor, chunkSize, overlap, autoInverseFft, keep, autoNormalize);
			}
			else
			{
				return await this.ExecuteGenericAudioKernelAsync<float>(obj, kernel, factor, chunkSize, overlap, autoInverseFft, keep, autoNormalize);
			}
		}




		public async Task<byte[]> ExecuteAudioVisualizerKernelAsync(AudioObj obj, string kernel = "visualizer00", Dictionary<string, object>? arguments = null)
		{
			byte[] result = [];



			await Task.CompletedTask;
			return result;
		}

	}
}
