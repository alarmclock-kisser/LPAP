using ManagedCuda;
using ManagedCuda.VectorTypes;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace LPAP.Cuda
{
	public class CudaExecutioner : IDisposable
	{
		// Fields
		private readonly PrimaryContext CTX;
		private readonly CudaRegister Register;
		private readonly CudaFourier Fourier;
		private readonly CudaCompiler Compiler;

		private CudaKernel? Kernel => this.Compiler.Kernel;

		// Properties



		// Constructor
		public CudaExecutioner(PrimaryContext ctx, CudaRegister register, CudaFourier fourier, CudaCompiler compiler)
		{
			this.CTX = ctx;
			this.Register = register;
			this.Fourier = fourier;
			this.Compiler = compiler;
		}


		// Methods
		public void Dispose()
		{

		}


		// Methods: Time stretching (audio)
		public IntPtr ExecuteTimeStretch(IntPtr indexPointer, string kernel, double factor, int chunkSize, int overlapSize, int sampleRate, bool keep = false)
		{
			// Verify kernel loaded
			this.Compiler.LoadKernel(kernel);
			if (this.Kernel == null)
			{
				CudaService.Log("Kernel not loaded or invalid.");
				return indexPointer;
			}
			else
			{
				CudaService.Log($"Kernel '{kernel}' loaded successfully.");
			}

			// Get memory from register
			var mem = this.Register[indexPointer];
			if (mem == null || mem.IndexPointer == nint.Zero || mem.IndexLength == nint.Zero)
			{
				CudaService.Log("Memory not found or invalid pointer.");
				return nint.Zero;
			}
			else
			{
				CudaService.Log($"Localized input memory: {mem.Count} chunks, total length: {mem.TotalLength}, total size: {mem.TotalSize} bytes.");
			}

			// Verify memory is in fft form (-> float2), optionally transform
			bool transformed = false;
			IntPtr resultPointer = nint.Zero;
			if (mem.ElementType != typeof(float2))
			{
				resultPointer = this.Fourier.PerformFft(indexPointer, keep);
				if (resultPointer == nint.Zero)
				{
					CudaService.Log("Failed to perform FFT on memory.");
					return indexPointer;
				}

				CudaService.Log("Memory transformed to float2 format for time stretch.");	
				transformed = true;
			}

			mem = this.Register[resultPointer];
			if (mem == null || mem.IndexPointer == nint.Zero || mem.IndexLength == nint.Zero)
			{
				CudaService.Log("Memory not found or invalid pointer after transformation.");
				return nint.Zero;
			}

			if (mem.ElementType != typeof(float2))
			{
				CudaService.Log("Failed to transform memory to float2 format.");
				return nint.Zero;
			}

			// Allocate output memory (float2)
			var outMem = this.Register.AllocateGroup<float2>(mem.Lengths);
			if (outMem == null || outMem.IndexPointer == nint.Zero)
			{
				CudaService.Log("Could not allocate output memory.");
				return indexPointer;
			}
			else
			{
				CudaService.Log($"Allocated output memory: {outMem.Count} chunks, total length: {outMem.TotalLength}, total size: {outMem.TotalSize} bytes.");
			}

			// Exec on every pointer
			try
			{
				for (int i = 0; i < mem.Count; i++)
				{
					// Build kernel arguments ()
					object[] args =
					[
					mem.DevicePointers[i], // Input pointer
				outMem.DevicePointers[i], // Output pointer
				mem.Lengths[i], // Length of input / chunkSize
				overlapSize, // Overlap size
				sampleRate, // Sample rate
				factor, // Time stretch factor
				];

					// Calculate grid and block dimensions
					dim3 blockDim = new(16, 16);
					dim3 gridDim = new(
						(uint) (mem.Lengths[i] + blockDim.x - 1) / blockDim.x,
						(uint) (mem.Count + blockDim.y - 1) / blockDim.y
					);

					var k = this.Kernel;

					k.BlockDimensions = blockDim;
					k.GridDimensions = gridDim;

					k.Run(args);

					CudaService.Log($"Executed kernel on chunk {i + 1}/{mem.Count} with length {mem.Lengths[i]}.");
				}
			}
			catch (Exception ex)
			{
				CudaService.Log(ex, "Error executing time stretch kernel.");
			}

			// If transformed previously, transform (inverse FFT -> float)
			if (transformed)
			{
				resultPointer = this.Fourier.PerformIfft(outMem.IndexPointer, keep);
				if (resultPointer == nint.Zero)
				{
					CudaService.Log("Failed to perform inverse FFT on output memory.");
					return indexPointer;
				}
				CudaService.Log("Transformed output memory back to float format after time stretch.");
			}

			outMem = this.Register[resultPointer];
			if (outMem == null || outMem.IndexPointer == nint.Zero || outMem.IndexLength == nint.Zero)
			{
				CudaService.Log("Output memory not found or invalid pointer after execution.");
				return indexPointer;
			}
			else
			{
				CudaService.Log($"Output memory after execution: {outMem.Count} chunks, total length: {outMem.TotalLength}, total size: {outMem.TotalSize} bytes.");
			}

			return resultPointer;
		}

		public async Task<IntPtr> ExecuteTimeStretchLinearAsync(IntPtr pointer, string kernel, double factor, int chunkSize, int overlapSize, int sampleRate, bool asMany = false, bool keep = false)
		{
			// Verify kernel loaded
			this.Compiler.LoadKernel(kernel);
			if (this.Kernel == null)
			{
				CudaService.Log("Kernel not loaded or invalid.");
				return nint.Zero;
			}

			// Get memory from register
			var mem = this.Register[pointer];
			if (mem == null || mem.IndexPointer == nint.Zero || mem.IndexLength == nint.Zero)
			{
				CudaService.Log("Memory not found or invalid pointer.");
				return nint.Zero;
			}

			// Verify memory is in fft form (-> float2), optionally transform
			bool transformed = false;
			IntPtr indexPointer = mem.IndexPointer;
			if (mem.ElementType != typeof(float2))
			{
				indexPointer = asMany ? await this.Fourier.PerformFftManyAsync(pointer, keep) : await this.Fourier.PerformFftAsync(pointer, keep);

				transformed = true;
			}

			mem = this.Register[indexPointer];
			if (mem == null || mem.IndexPointer == nint.Zero || mem.IndexLength == nint.Zero)
			{
				CudaService.Log("Memory not found or invalid pointer after transformation.");
				return nint.Zero;
			}

			if (mem.ElementType != typeof(float2))
			{
				CudaService.Log("Failed to transform memory to float2 format.");
				return nint.Zero;
			}

			// Allocate output memory (float2)
			var outMem = await this.Register.AllocateGroupAsync<float2>(mem.Lengths);
			if (outMem == null || outMem.IndexPointer == nint.Zero)
			{
				CudaService.Log("Could not allocate output memory.");
				return pointer;
			}

			// Execute kernel on every pointer per stream
			var stream = this.Register.GetStream();
			if (stream == null)
			{
				CudaService.Log("No stream available for execution.");
				return pointer;
			}

			try
			{
				// Exec on every pointer
				for (int i = 0; i < mem.Count; i++)
				{
					// Build kernel arguments ()
					object[] args =
					[
					mem.DevicePointers[i], // Input pointer
				outMem.DevicePointers[i], // Output pointer
				mem.Lengths[i], // Length of input / chunkSize
				overlapSize, // Overlap size
				sampleRate, // Sample rate
				factor, // Time stretch factor
				];

					// Calculate grid and block dimensions
					dim3 blockDim = new(16, 16);
					dim3 gridDim = new(
						(uint) (mem.Lengths[i] + blockDim.x - 1) / blockDim.x,
						(uint) (mem.Count + blockDim.y - 1) / blockDim.y
					);

					var k = this.Kernel;

					k.BlockDimensions = blockDim;
					k.GridDimensions = gridDim;

					await Task.Run(() => k.RunAsync(stream.Stream, args));
				}
			}
			catch (Exception ex)
			{
				CudaService.Log(ex, "Error executing time stretch kernel linear (async).");
			}

			// If transformed previously, transform (inverse FFT -> float)
			if (transformed)
			{
				pointer = asMany
					? await this.Fourier.PerformIfftManyAsync(outMem.IndexPointer, keep)
					: await this.Fourier.PerformIfftAsync(outMem.IndexPointer, keep);
			}
			else
			{
				pointer = outMem.IndexPointer;
			}

			outMem = this.Register[pointer];
			if (outMem == null || outMem.IndexPointer == nint.Zero || outMem.IndexLength == nint.Zero)
			{
				CudaService.Log("Output memory not found or invalid pointer after execution.");
				return pointer;
			}

			return pointer;
		}

		public async Task<IntPtr> ExecuteTimeStretchInterleavedAsync(IntPtr pointer, string kernel, double factor, int chunkSize, int overlapSize, int sampleRate, int maxStreams = 1, bool asMany = false, bool keep = false)
		{
			// Verify kernel loaded
			this.Compiler.LoadKernel(kernel);
			if (this.Kernel == null)
			{
				CudaService.Log("Kernel not loaded or invalid.");
				return nint.Zero;
			}

			// Get memory from register
			var mem = this.Register[pointer];
			if (mem == null || mem.IndexPointer == nint.Zero || mem.IndexLength == nint.Zero)
			{
				CudaService.Log("Memory not found or invalid pointer.");
				return nint.Zero;
			}

			// Verify memory is in fft form (-> float2), optionally transform
			bool transformed = false;
			IntPtr indexPointer = mem.IndexPointer;
			if (mem.ElementType != typeof(float2))
			{
				indexPointer = asMany ? await this.Fourier.PerformFftManyAsync(pointer, keep) : await this.Fourier.PerformFftAsync(pointer, keep);

				transformed = true;
			}

			mem = this.Register[indexPointer];
			if (mem == null || mem.IndexPointer == nint.Zero || mem.IndexLength == nint.Zero)
			{
				CudaService.Log("Memory not found or invalid pointer after transformation.");
				return nint.Zero;
			}

			if (mem.ElementType != typeof(float2))
			{
				CudaService.Log("Failed to transform memory to float2 format.");
				return nint.Zero;
			}

			// Allocate output memory (float2)
			var outMem = await this.Register.AllocateGroupAsync<float2>(mem.Lengths);
			if (outMem == null || outMem.IndexPointer == nint.Zero)
			{
				CudaService.Log("Could not allocate output memory.");
				return pointer;
			}

			// Execute kernel on every pointer per stream
			var streams = await this.Register.GetManyStreamsAsync(maxStreams);
			if (streams == null || !streams.Any())
			{
				CudaService.Log("No streams available for execution.");
				return pointer;
			}

			try
			{
				ConcurrentDictionary<Task, CudaKernel> tasks = [];
				for (int i = 0; i < mem.Count; i++)
				{
					// Build kernel arguments ()
					object[] args =
					[
					mem.DevicePointers[i], // Input pointer
				outMem.DevicePointers[i], // Output pointer
				mem.Lengths[i], // Length of input / chunkSize
				overlapSize, // Overlap size
				sampleRate, // Sample rate
				factor, // Time stretch factor
				];

					// Calculate grid and block dimensions
					int numChunks = mem.Count;
					dim3 blockSize = new(16, 16);
					dim3 gridSize = new(
						(uint) (chunkSize + blockSize.x - 1) / blockSize.x,
						(uint) (numChunks + blockSize.y - 1) / blockSize.y
					);

					var k = this.Kernel;

					k.GridDimensions = gridSize;
					k.BlockDimensions = blockSize;

					tasks[Task.Run(() =>
					{
						var stream = streams.ElementAt(Math.Min(streams.Count() - 1, i));
						if (stream == null)
						{
							CudaService.Log("Invalid stream for execution.");
							return;
						}

						// Launch kernel
						k.RunAsync(stream.Stream, args);
					})] = k;
				}

				await Task.WhenAll(tasks.Keys);
			}
			catch (Exception ex)
			{
				CudaService.Log(ex, "Error executing time stretch kernel interleaved (async).");
			}

			// If transformed previously, transform (inverse FFT -> float)
			if (transformed)
			{
				pointer = asMany
					? await this.Fourier.PerformIfftManyAsync(outMem.IndexPointer, keep)
					: await this.Fourier.PerformIfftAsync(outMem.IndexPointer, keep);
			}
			else
			{
				pointer = outMem.IndexPointer;
			}

			outMem = this.Register[pointer];
			if (outMem == null || outMem.IndexPointer == nint.Zero || outMem.IndexLength == nint.Zero)
			{
				CudaService.Log("Output memory not found or invalid pointer after execution.");
				return pointer;
			}

			return pointer;
		}



		// Generic kernel execution (sync)
		public IntPtr ExecuteGenericAudioKernel<T>(
			IntPtr indexPointer,
			string kernel,
			int chunkSize,
			float overlap,
			bool keep = false,
			Dictionary<string, object>? arguments = null) where T : unmanaged
		{
			// Verify kernel loaded
			this.Compiler.LoadKernel(kernel);
			if (this.Kernel == null)
			{
				CudaService.Log("Kernel not loaded or invalid.");
				return nint.Zero;
			}

			// Get memory from register
			var mem = this.Register[indexPointer];
			if (mem == null || mem.IndexPointer == nint.Zero || mem.IndexLength == nint.Zero)
			{
				CudaService.Log("Memory not found or invalid pointer.");
				return nint.Zero;
			}

			// Allocate output memory
			var outMem = this.Register.AllocateGroup<T>(mem.Lengths);
			if (outMem == null || outMem.IndexPointer == nint.Zero)
			{
				CudaService.Log("Could not allocate output memory.");
				return nint.Zero;
			}

			try
			{
				for (int i = 0; i < mem.Count; i++)
				{
					// Merge provided args with known audio params (per chunk)
					var baseArgs = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
			{
				{ "input",        mem.DevicePointers[i] },
				{ "output",       outMem.DevicePointers[i] },
				{ "length",       (int) mem.Lengths[i] },
				{ "chunkSize",    chunkSize },
				{ "overlap",      (int) Math.Clamp((int) Math.Round(chunkSize * overlap), 0, chunkSize - 1) }
			};

					// include user arguments (can override)
					if (arguments != null)
					{
						foreach (var kv in arguments)
						{
							baseArgs[kv.Key] = kv.Value;
						}
					}

					// Order according to .cu signature
					object[] args = this.OrderArguments(baseArgs, kernel);

					// Grid/Block dims (analog zu TimeStretch)
					dim3 blockDim = new(16, 16);
					dim3 gridDim = new(
						(uint) ((int) mem.Lengths[i] + blockDim.x - 1) / blockDim.x,
						(uint) (mem.Count + blockDim.y - 1) / blockDim.y
					);

					var k = this.Kernel!;
					k.BlockDimensions = blockDim;
					k.GridDimensions = gridDim;
					k.Run(args);

					CudaService.Log($"Executed generic kernel on chunk {i + 1}/{mem.Count} with length {mem.Lengths[i]}.");
				}
			}
			catch (Exception ex)
			{
				CudaService.Log(ex, "Error executing generic audio kernel.");
				return nint.Zero;
			}

			// Option: Input freigeben, wenn nicht keep
			if (!keep)
			{
				try { this.Register.FreeMemory(mem); } catch { }
			}

			return outMem.IndexPointer;
		}

		// Generic kernel execution (async, linear)
		public async Task<IntPtr> ExecuteGenericAudioKernelAsync<T>(
			IntPtr indexPointer,
			string kernel,
			int chunkSize,
			float overlap,
			bool keep = false,
			Dictionary<string, object>? arguments = null) where T : unmanaged
		{
			// Verify kernel loaded
			this.Compiler.LoadKernel(kernel);
			if (this.Kernel == null)
			{
				CudaService.Log("Kernel not loaded or invalid.");
				return nint.Zero;
			}

			// Get memory from register
			var mem = this.Register[indexPointer];
			if (mem == null || mem.IndexPointer == nint.Zero || mem.IndexLength == nint.Zero)
			{
				CudaService.Log("Memory not found or invalid pointer.");
				return nint.Zero;
			}

			// Allocate output memory
			var outMem = await this.Register.AllocateGroupAsync<T>(mem.Lengths);
			if (outMem == null || outMem.IndexPointer == nint.Zero)
			{
				CudaService.Log("Could not allocate output memory.");
				return nint.Zero;
			}

			// Execute per stream
			var stream = this.Register.GetStream();
			if (stream == null)
			{
				CudaService.Log("No stream available for execution.");
				return nint.Zero;
			}

			try
			{
				for (int i = 0; i < mem.Count; i++)
				{
					var baseArgs = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
			{
				{ "input",        mem.DevicePointers[i] },
				{ "output",       outMem.DevicePointers[i] },
				{ "length",       (int) mem.Lengths[i] },
				{ "chunkSize",    chunkSize },
				{ "overlap",      (int) Math.Clamp((int) Math.Round(chunkSize * overlap), 0, chunkSize - 1) }
			};

					if (arguments != null)
					{
						foreach (var kv in arguments)
						{
							baseArgs[kv.Key] = kv.Value;
						}
					}

					object[] args = this.OrderArguments(baseArgs, kernel);

					dim3 blockDim = new(16, 16);
					dim3 gridDim = new(
						(uint) ((int) mem.Lengths[i] + blockDim.x - 1) / blockDim.x,
						(uint) (mem.Count + blockDim.y - 1) / blockDim.y
					);

					var k = this.Kernel!;
					k.BlockDimensions = blockDim;
					k.GridDimensions = gridDim;

					await Task.Run(() => k.RunAsync(stream.Stream, args));
				}
			}
			catch (Exception ex)
			{
				CudaService.Log(ex, "Error executing generic audio kernel (async).");
				return nint.Zero;
			}

			// Option: Input freigeben, wenn nicht keep
			if (!keep)
			{
				try { this.Register.FreeMemory(mem); } catch { }
			}

			return outMem.IndexPointer;
		}

		// Argument-Ordering gemäß .cu-Definitionen
		internal object[] OrderArguments(Dictionary<string, object> arguments, string? kernelName = null)
		{
			kernelName ??= this.Compiler.KernelName;
			if (string.IsNullOrEmpty(kernelName))
			{
				CudaService.Log("Kernel name not specified for argument ordering.");
				return [];
			}

			var argDefs = this.Compiler.GetKernelArguments(kernelName);
			if (argDefs == null || argDefs.Count == 0)
			{
				CudaService.Log("No argument definitions found for kernel.");
				return [];
			}

			// Hinweis: Dictionary in .NET bewahrt Einfügereihenfolge; Compiler.GetKernelArguments baut sie aus der Signatur.
			var ordered = new object[argDefs.Count];

			for (int i = 0; i < argDefs.Count; i++)
			{
				string name = argDefs.ElementAt(i).Key;
				Type type = argDefs.ElementAt(i).Value;

				// Matching-Strategie:
				// - exakte Namen aus 'arguments' (case-insensitive)
				// - Heuristik-basierte Mapping-Regeln für bekannte Audio-Parameter
				object? value = null;

				// 1) direkter Treffer aus arguments
				if (arguments != null)
				{
					// exakter Name
					if (arguments.TryGetValue(name, out var v))
					{
						value = v;
					}
					else
					{
						// einfache Heuristiken
						foreach (var kv in arguments)
						{
							if (string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase))
							{
								value = kv.Value;
								break;
							}
						}
					}
				}

				// 2) falls nicht gefunden: Heuristische Zuordnung nach Namen
				if (value == null)
				{
					string lname = name.ToLowerInvariant();

					if (type == typeof(IntPtr))
					{
						// Pointers: input/output
						// bevorzugt Keys "input"/"output"
						if (lname.Contains("in") && arguments != null && arguments.TryGetValue("input", out var vin))
						{
							value = vin;
						}
						else if (lname.Contains("out") && arguments != null && arguments.TryGetValue("output", out var vout))
						{
							value = vout;
						}
					}
					else if (type == typeof(int))
					{
						// Zahlen-Argumente
						if (lname.Contains("len") && arguments != null && arguments.TryGetValue("length", out var vlen))
						{
							value = vlen;
						}
						else if (lname.Contains("chunk") && arguments != null && arguments.TryGetValue("chunkSize", out var vchunk))
						{
							value = vchunk;
						}
						else if (lname.Contains("overlap") && arguments != null && arguments.TryGetValue("overlap", out var vover))
						{
							value = vover;
						}
						else if (lname.Contains("sample") && arguments != null && arguments.TryGetValue("sampleRate", out var vsr))
						{
							value = vsr;
						}
						else if (lname.Contains("chan") && arguments != null && arguments.TryGetValue("channels", out var vch))
						{
							value = vch;
						}
						else if (lname.Contains("bit") && arguments != null && arguments.TryGetValue("bitdepth", out var vbd))
						{
							value = vbd;
						}
						else if (lname.Contains("factor") && arguments != null && arguments.TryGetValue("factor", out var vfac))
						{
							value = vfac;
						}
					}
					else if (type == typeof(float) || type == typeof(double))
					{
						if (lname.Contains("overlap") && arguments != null && arguments.TryGetValue("overlap", out var vo))
						{
							value = vo;
						}
						else if (lname.Contains("factor") && arguments != null && arguments.TryGetValue("factor", out var vf))
						{
							value = vf;
						}
					}
				}

				// 3) Fallback: typgerechter Default
				if (value == null)
				{
					if (type == typeof(IntPtr))
					{
						value = IntPtr.Zero;
					}
					else if (type == typeof(int))
					{
						value = 0;
					}
					else if (type == typeof(float))
					{
						value = 0f;
					}
					else if (type == typeof(double))
					{
						value = 0.0;
					}
					else if (type == typeof(bool))
					{
						value = false;
					}
					else if (type == typeof(byte))
					{
						value = (byte) 0;
					}
					else
					{
						value = 0;
					}
				}

				ordered[i] = value!;
			}

			return ordered;
		}
	}



}
