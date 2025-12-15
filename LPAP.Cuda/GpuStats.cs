using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

internal sealed class GpuStats
{
	// UI statistics
	public static Task<double?> GetGpuLoadAsync(int deviceIndex = 0, CancellationToken ct = default)
	{
		// NVML Calls sind i.d.R. sehr schnell (kein I/O), daher: kein Task.Run nötig.
		// Trotzdem async-Signatur: Task.FromResult.
		ct.ThrowIfCancellationRequested();

		return Task.FromResult(NvmlGpu.TryGetGpuUtilization(deviceIndex));
	}

	private static class NvmlGpu
	{
		// NVML init einmalig pro Prozess
		private static int _initialized; // 0 = no, 1 = yes
		private static readonly Lock _initLock = new();

		public static double? TryGetGpuUtilization(int deviceIndex)
		{
			if (!EnsureInitialized())
			{
				return null;
			}

			var rc = nvmlDeviceGetHandleByIndex_v2((uint) deviceIndex, out var device);
			if (rc != NvmlReturn.Success)
			{
				return null;
			}

			rc = nvmlDeviceGetUtilizationRates(device, out var util);
			if (rc != NvmlReturn.Success)
			{
				return null;
			}

			// util.gpu ist Prozent [0..100]
			return util.gpu / 100.0;
		}

		private static bool EnsureInitialized()
		{
			if (Volatile.Read(ref _initialized) == 1)
			{
				return true;
			}

			lock (_initLock)
			{
				if (_initialized == 1)
				{
					return true;
				}

				// nvmlInit_v2 lädt nvml.dll; wenn nicht vorhanden -> DllNotFoundException
				try
				{
					var rc = nvmlInit_v2();
					if (rc != NvmlReturn.Success)
					{
						return false;
					}

					_initialized = 1;

					// Optional: sauber runterfahren beim Prozessende
					AppDomain.CurrentDomain.ProcessExit += (_, __) =>
					{
						try { nvmlShutdown(); } catch { /* ignore */ }
					};

					return true;
				}
				catch (DllNotFoundException)
				{
					// nvml.dll nicht gefunden (Treiber fehlt / falsche Bitness)
					return false;
				}
				catch (EntryPointNotFoundException)
				{
					// sehr alter Treiber / inkompatible nvml.dll
					return false;
				}
			}
		}

		// ----- NVML P/Invoke -----

		// Wichtig: x64 Prozess! nvml.dll ist 64-bit auf normalen Windows-NVIDIA-Treibern.
		private const string NvmlDll = "nvml.dll";

		private enum NvmlReturn : int
		{
			Success = 0,
			ErrorUninitialized = 1,
			ErrorInvalidArgument = 2,
			ErrorNotSupported = 3,
			ErrorNoPermission = 4,
			ErrorAlreadyInitialized = 5,
			ErrorNotFound = 6,
			ErrorInsufficientSize = 7,
			ErrorInsufficientPower = 8,
			ErrorDriverNotLoaded = 9,
			ErrorTimeout = 10,
			ErrorIrqIssue = 11,
			ErrorLibraryNotFound = 12,
			ErrorFunctionNotFound = 13,
			ErrorCorruptedInforom = 14,
			ErrorGpuIsLost = 15,
			ErrorResetRequired = 16,
			ErrorOperatingSystem = 17,
			ErrorLibRmVersionMismatch = 18,
			ErrorInUse = 19,
			ErrorMemory = 20,
			ErrorNoData = 21,
			ErrorVgpuEccNotSupported = 22,
			ErrorUnknown = 999
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct nvmlUtilization_t
		{
			public uint gpu;    // GPU utilization in %
			public uint memory; // Memory utilization in %
		}

		// nvmlDevice_t ist ein pointer/handle
		[StructLayout(LayoutKind.Sequential)]
		private struct nvmlDevice_t
		{
			public IntPtr Handle;
		}

		[DllImport(NvmlDll, CallingConvention = CallingConvention.Cdecl)]
		private static extern NvmlReturn nvmlInit_v2();

		[DllImport(NvmlDll, CallingConvention = CallingConvention.Cdecl)]
		private static extern NvmlReturn nvmlShutdown();

		[DllImport(NvmlDll, CallingConvention = CallingConvention.Cdecl)]
		private static extern NvmlReturn nvmlDeviceGetHandleByIndex_v2(uint index, out nvmlDevice_t device);

		[DllImport(NvmlDll, CallingConvention = CallingConvention.Cdecl)]
		private static extern NvmlReturn nvmlDeviceGetUtilizationRates(nvmlDevice_t device, out nvmlUtilization_t utilization);
	}
}

