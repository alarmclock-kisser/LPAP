using ManagedCuda;
using ManagedCuda.NVRTC;
using ManagedCuda.VectorTypes;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace LPAP.Cuda
{
	internal sealed class CudaCompiler : IDisposable
	{
		private readonly PrimaryContext _ctx;
		private readonly string _kernelPath;

		// Thread-safe kernel/module handling:
		// Old behavior: keep exactly one "current" kernel and call _ctx.UnloadKernel() inside LoadKernel().
		// That breaks immediately with parallel per-stream execution, because loading a kernel for stream B
		// unloads the kernel stream A is still using.
		//
		// Fix:
		//  - Cache long-lived kernels by name (LoadKernel)
		//  - Provide short-lived per-stream kernel instances (CreateKernelInstance)

		private readonly object _loadLock = new();
		private readonly Dictionary<string, CudaKernel> _kernelCache = new(StringComparer.OrdinalIgnoreCase);
		private readonly List<CudaKernel> _transientKernels = new();

		internal CudaCompiler(PrimaryContext ctx, string kernelPath, bool compileAll = true, bool logCompile = false)
		{
			this._ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
			this._kernelPath = kernelPath ?? throw new ArgumentNullException(nameof(kernelPath));

			Directory.CreateDirectory(Path.Combine(this._kernelPath, "CU"));
			Directory.CreateDirectory(Path.Combine(this._kernelPath, "PTX"));
			Directory.CreateDirectory(Path.Combine(this._kernelPath, "Logs"));

			if (compileAll)
			{
				this.CompileAll(!logCompile, logCompile);
			}
		}

		// Back-compat metadata (last kernel loaded via LoadKernel)
		public CudaKernel? Kernel { get; private set; }
		public string? KernelName { get; private set; }
		public string? KernelFile { get; private set; }
		public string? KernelCode { get; private set; }

		public IReadOnlyList<string> SourceFiles => this.GetCuFiles();
		public IReadOnlyList<string> CompiledFiles => this.GetPtxFiles();
		public string KernelPath => this._kernelPath;

		public void Dispose()
		{
			this.UnloadAllKernels();
			GC.SuppressFinalize(this);
		}

		/// <summary>Unloads all cached and transient kernels. Call only on shutdown.</summary>
		public void UnloadAllKernels()
		{
			lock (this._loadLock)
			{
				foreach (var k in this._transientKernels.ToArray())
				{
					this.SafeUnloadKernel(k);
				}
				this._transientKernels.Clear();

				foreach (var kv in this._kernelCache.ToArray())
				{
					this.SafeUnloadKernel(kv.Value);
				}
				this._kernelCache.Clear();

				this.Kernel = null;
				this.KernelName = null;
				this.KernelFile = null;
				this.KernelCode = null;
			}
		}

		/// <summary>Unload a transient kernel created via CreateKernelInstance(). Safe to call multiple times.</summary>
		public void UnloadKernelInstance(CudaKernel? kernel)
		{
			if (kernel == null)
			{
				return;
			}

			lock (this._loadLock)
			{
				this._transientKernels.Remove(kernel);
				this.SafeUnloadKernel(kernel);
			}
		}

		private void SafeUnloadKernel(CudaKernel kernel)
		{
			try
			{
				this._ctx.UnloadKernel(kernel);
			}
			catch (Exception ex)
			{
				CudaLog.Warn("Failed to unload kernel", ex.Message);
			}
		}

		public List<string> GetCuFiles(string? path = null)
		{
			path ??= Path.Combine(this._kernelPath, "CU");
			return Directory.Exists(path) ? Directory.GetFiles(path, "*.cu").Select(Path.GetFullPath).ToList() : [];
		}

		public List<string> GetPtxFiles(string? path = null)
		{
			path ??= Path.Combine(this._kernelPath, "PTX");
			return Directory.Exists(path) ? Directory.GetFiles(path, "*.ptx").Select(Path.GetFullPath).ToList() : [];
		}

		public void CompileAll(bool silent = false, bool logErrors = false)
		{
			foreach (var source in this.SourceFiles)
			{
				var result = this.CompileKernel(source, silent);
				if (string.IsNullOrWhiteSpace(result) && logErrors)
				{
					CudaLog.Warn("Kernel compilation failed", Path.GetFileNameWithoutExtension(source));
				}
			}
		}

		public string? CompileKernel(string filePath, bool silent = false)
		{
			if (!File.Exists(filePath))
			{
				CudaLog.Warn("Kernel file not found", filePath);
				return null;
			}

			if (!string.Equals(Path.GetExtension(filePath), ".cu", StringComparison.OrdinalIgnoreCase))
			{
				return this.CompileString(File.ReadAllText(filePath), silent);
			}

			string kernelName = Path.GetFileNameWithoutExtension(filePath);
			string logPath = Path.Combine(this._kernelPath, "Logs", kernelName + ".log");
			string code = File.ReadAllText(filePath);

			var stopwatch = Stopwatch.StartNew();
			if (!silent)
			{
				CudaLog.Info($"Compiling kernel '{kernelName}'");
			}

			using var rtc = new CudaRuntimeCompiler(code, kernelName);
			try
			{
				rtc.Compile([]);
				WriteCompilationLog(rtc, logPath, silent);

				stopwatch.Stop();
				if (!silent)
				{
					CudaLog.Info($"Compiled in {stopwatch.ElapsedMilliseconds} ms", Path.GetFileName(logPath));
				}

				var ptx = rtc.GetPTX();
				string ptxPath = Path.Combine(this._kernelPath, "PTX", kernelName + ".ptx");
				File.WriteAllBytes(ptxPath, ptx);

				if (!silent)
				{
					CudaLog.Info("PTX exported", Path.GetFileName(ptxPath));
				}

				return ptxPath;
			}
			catch (Exception ex)
			{
				File.WriteAllText(logPath, rtc.GetLogAsString());
				CudaLog.Error("CUDA compilation failed", ex.Message);
				return null;
			}
		}

		public string? CompileString(string kernelSource, bool silent = false)
		{
			string? name = this.PrecompileKernelString(kernelSource, silent);
			if (string.IsNullOrWhiteSpace(name))
			{
				return null;
			}

			string cuPath = Path.Combine(this._kernelPath, "CU", name + ".cu");
			File.WriteAllText(cuPath, kernelSource);
			return this.CompileKernel(cuPath, silent);
		}

		public string? PrecompileKernelString(string kernelSource, bool silent = false)
		{
			if (!kernelSource.Contains("extern \"C\"", StringComparison.Ordinal))
			{
				if (!silent)
				{
					CudaLog.Warn("Kernel string missing extern \"C\"");
				}

				return null;
			}

			if (!kernelSource.Contains("__global__", StringComparison.Ordinal))
			{
				if (!silent)
				{
					CudaLog.Warn("Kernel string missing __global__");
				}

				return null;
			}

			if (!kernelSource.Contains("void ", StringComparison.Ordinal))
			{
				if (!silent)
				{
					CudaLog.Warn("Kernel string missing void signature");
				}

				return null;
			}

			int start = kernelSource.IndexOf("void ", StringComparison.Ordinal) + 5;
			int end = kernelSource.IndexOf('(', start);
			if (end < 0)
			{
				return null;
			}

			string name = kernelSource.Substring(start, end - start).Trim();
			if (!silent)
			{
				CudaLog.Info("Validated kernel source", name);
			}

			return name;
		}

		/// <summary>
		/// Loads (and caches) a kernel by name. Does NOT unload other kernels.
		/// Safe for repeated calls; returns the cached kernel.
		/// </summary>
		public CudaKernel? LoadKernel(string kernelName, bool silent = false)
		{
			string ptxPath = Path.Combine(this._kernelPath, "PTX", kernelName + ".ptx");
			string cuPath = Path.Combine(this._kernelPath, "CU", kernelName + ".cu");

			if (!File.Exists(ptxPath))
			{
				if (!silent)
				{
					CudaLog.Warn("PTX file missing", kernelName);
				}

				return null;
			}

			try
			{
				lock (this._loadLock)
				{
					this._ctx.SetCurrent();
					if (this._kernelCache.TryGetValue(kernelName, out var cached))
					{
						this.Kernel = cached;
						this.KernelName = kernelName;
						this.KernelFile = ptxPath;
						this.KernelCode = File.Exists(cuPath) ? File.ReadAllText(cuPath) : null;
						return cached;
					}

					var stopwatch = Stopwatch.StartNew();
					byte[] ptxCode = File.ReadAllBytes(ptxPath);

					var kernel = this._ctx.LoadKernelPTX(ptxCode, kernelName);
					this._kernelCache[kernelName] = kernel;

					this.Kernel = kernel;
					this.KernelName = kernelName;
					this.KernelFile = ptxPath;
					this.KernelCode = File.Exists(cuPath) ? File.ReadAllText(cuPath) : null;

					stopwatch.Stop();
					if (!silent)
					{
						CudaLog.Info($"Loaded kernel '{kernelName}'", $"{stopwatch.ElapsedMilliseconds} ms");
					}

					return kernel;
				}
			}
			catch (Exception ex)
			{
				if (!silent)
				{
					CudaLog.Error("Failed to load kernel", ex.Message);
				}

				lock (this._loadLock) { this.Kernel = null; }
				return null;
			}
		}

		/// <summary>
		/// Creates a short-lived independent kernel instance (its own module load).
		/// Use this for parallel per-stream work (each stream changes grid/block dims).
		/// Caller MUST later call UnloadKernelInstance().
		/// </summary>
		public CudaKernel? CreateKernelInstance(string kernelName, bool silent = true)
		{
			string ptxPath = Path.Combine(this._kernelPath, "PTX", kernelName + ".ptx");
			if (!File.Exists(ptxPath))
			{
				if (!silent)
				{
					CudaLog.Warn("PTX file missing", kernelName);
				}

				return null;
			}

			try
			{
				lock (this._loadLock)
				{
					byte[] ptxCode = File.ReadAllBytes(ptxPath);
					var k = this._ctx.LoadKernelPTX(ptxCode, kernelName);
					this._transientKernels.Add(k);
					return k;
				}
			}
			catch (Exception ex)
			{
				if (!silent)
				{
					CudaLog.Error("Failed to create kernel instance", ex.Message);
				}

				return null;
			}
		}

		public CudaKernel? CompileLoadKernelFromString(string kernelCodeOrFile)
		{
			if (File.Exists(kernelCodeOrFile))
			{
				var ptx = this.CompileKernel(kernelCodeOrFile, silent: true);
				if (!string.IsNullOrEmpty(ptx))
				{
					var name = Path.GetFileNameWithoutExtension(kernelCodeOrFile);
					return this.LoadKernel(name!, silent: true);
				}
				return null;
			}

			var nameFromSource = this.PrecompileKernelString(kernelCodeOrFile, silent: true);
			if (string.IsNullOrWhiteSpace(nameFromSource))
			{
				return null;
			}

			var result = this.CompileString(kernelCodeOrFile, silent: true);
			return string.IsNullOrEmpty(result) ? null : this.LoadKernel(nameFromSource!, silent: true);
		}

		public Dictionary<string, Type> GetArguments(string? kernelCodeOrFileName = null, bool log = false)
		{
			string code;
			if (!string.IsNullOrWhiteSpace(kernelCodeOrFileName) && File.Exists(kernelCodeOrFileName) &&
				Path.GetExtension(kernelCodeOrFileName).Equals(".cu", StringComparison.OrdinalIgnoreCase))
			{
				code = File.ReadAllText(kernelCodeOrFileName);
			}
			else if (!string.IsNullOrWhiteSpace(kernelCodeOrFileName))
			{
				var cu = this.SourceFiles.FirstOrDefault(f =>
					Path.GetFileNameWithoutExtension(f).Equals(kernelCodeOrFileName, StringComparison.OrdinalIgnoreCase));
				code = cu != null ? File.ReadAllText(cu) : kernelCodeOrFileName;
			}
			else if (!string.IsNullOrEmpty(this.KernelCode))
			{
				code = this.KernelCode;
			}
			else
			{
				return [];
			}

			int kernelStart = code.IndexOf("__global__ void", StringComparison.Ordinal);
			if (kernelStart < 0)
			{
				if (log)
				{
					CudaLog.Warn("__global__ void not found for argument extraction");
				}

				return [];
			}

			string signature = code[kernelStart..];
			signature = Regex.Replace(signature, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
			signature = Regex.Replace(signature, @"//.*", string.Empty);

			int open = signature.IndexOf('(');
			int close = signature.IndexOf(')', open + 1);
			if (open < 0 || close < 0)
			{
				return [];
			}

			string argSection = signature.Substring(open + 1, close - open - 1);
			argSection = Regex.Replace(argSection, "\\s+", " ").Trim();
			string[] rawArgs = argSection.Split([','], StringSplitOptions.RemoveEmptyEntries);

			Dictionary<string, Type> args = new(StringComparer.OrdinalIgnoreCase);
			foreach (var raw in rawArgs)
			{
				var tokens = raw.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
				if (tokens.Length < 2)
				{
					continue;
				}

				string name = tokens[^1].Trim();
				string typeName = string.Join(' ', tokens.Take(tokens.Length - 1));
				args[name] = this.GetArgumentType(typeName);
			}

			if (log)
			{
				CudaLog.Info("Extracted kernel arguments", string.Join(", ", args.Keys));
			}

			return args;
		}

		public Type GetArgumentType(string typeName)
		{
			string cleaned = Regex.Replace(typeName, @"\b(const|__restrict__|restrict|volatile)\b", string.Empty).Trim();
			bool isPointer = cleaned.EndsWith("*");
			if (isPointer)
			{
				cleaned = cleaned.TrimEnd('*').Trim();
			}

			string norm = Regex.Replace(cleaned, "\\s+", " ").Trim().ToLowerInvariant();

			bool isUnsigned = norm.Contains("unsigned ");
			bool isSigned = norm.Contains("signed ");
			bool isLongLong = norm.Contains("long long");
			bool isLong = !isLongLong && norm.Contains("long");
			bool isShort = norm.Contains("short");

			if (norm is "uint" or "unsigned int")
			{
				return isPointer ? typeof(uint).MakePointerType() : typeof(uint);
			}

			if (norm is "int" or "signed int")
			{
				return isPointer ? typeof(int).MakePointerType() : typeof(int);
			}

			if (norm is "float")
			{
				return isPointer ? typeof(float).MakePointerType() : typeof(float);
			}

			if (norm is "double")
			{
				return isPointer ? typeof(double).MakePointerType() : typeof(double);
			}

			if (norm is "bool")
			{
				return isPointer ? typeof(bool).MakePointerType() : typeof(bool);
			}

			if (norm is "byte")
			{
				return isPointer ? typeof(byte).MakePointerType() : typeof(byte);
			}

			if (norm is "float2")
			{
				return isPointer ? typeof(float2).MakePointerType() : typeof(float2);
			}

			if (norm.Contains("char"))
			{
				if (isUnsigned || norm.StartsWith("uchar"))
				{
					return isPointer ? typeof(byte).MakePointerType() : typeof(byte);
				}

				if (isSigned)
				{
					return isPointer ? typeof(sbyte).MakePointerType() : typeof(sbyte);
				}

				return isPointer ? typeof(sbyte).MakePointerType() : typeof(sbyte);
			}

			if (isShort)
			{
				var t = isUnsigned ? typeof(ushort) : typeof(short);
				return isPointer ? t.MakePointerType() : t;
			}

			if (isLongLong)
			{
				var t = isUnsigned ? typeof(ulong) : typeof(long);
				return isPointer ? t.MakePointerType() : t;
			}

			if (isLong)
			{
				var t = isUnsigned ? typeof(ulong) : typeof(long);
				return isPointer ? t.MakePointerType() : t;
			}

			if (norm is "size_t")
			{
				var t = typeof(ulong);
				return isPointer ? t.MakePointerType() : t;
			}

			if (norm is "ssize_t" or "ptrdiff_t")
			{
				var t = typeof(long);
				return isPointer ? t.MakePointerType() : t;
			}

			var fallback = typeof(void);
			return isPointer ? fallback.MakePointerType() : fallback;
		}

		private static void WriteCompilationLog(CudaRuntimeCompiler rtc, string logPath, bool silent)
		{
			string log = rtc.GetLogAsString();
			if (string.IsNullOrWhiteSpace(log))
			{
				if (!silent)
				{
					CudaLog.Info("NVRTC compilation completed without warnings");
				}

				return;
			}

			File.WriteAllText(logPath, log);
			if (!silent)
			{
				CudaLog.Warn("NVRTC emitted warnings", Path.GetFileName(logPath));
			}
		}
	}
}
