using System.Management;
using System.Runtime.Versioning;
using Microsoft.ML.OnnxRuntime;

namespace LPAP.Onnx.Runtime
{
	public static class OnnxProviderService
	{
		public static IReadOnlyList<string> GetAvailableProviders()
		{
			try
			{
				// Newer ORT builds expose this via OrtEnv.
				return OnnxEnvironment.Env.GetAvailableProviders();
			}
			catch
			{
				// Fallback: CPU is always available.
				return ["CPUExecutionProvider"];
			}
		}

		/// <summary>
		/// Heuristic GPU listing (Windows): show NVIDIA adapters via WMI.
		/// This is *not* the same as CUDA device enumeration; it's just useful UI.
		/// </summary>
		[SupportedOSPlatform("windows")]
		public static IReadOnlyList<string> ListVideoControllers()
		{
			try
			{
				var list = new List<string>();
				using var searcher = new ManagementObjectSearcher("SELECT Name,AdapterRAM FROM Win32_VideoController");
				foreach (var obj in searcher.Get())
				{
					var name = obj["Name"]?.ToString() ?? "(unknown)";
					var ram = obj["AdapterRAM"] is ulong u ? (long) u : (obj["AdapterRAM"] is long l ? l : 0);
					if (ram > 0)
					{
						list.Add($"{name} ({ram / (1024 * 1024)} MB)");
					}
					else
					{
						list.Add(name);
					}
				}
				return list;
			}
			catch
			{
				return [];
			}
		}
	}
}
