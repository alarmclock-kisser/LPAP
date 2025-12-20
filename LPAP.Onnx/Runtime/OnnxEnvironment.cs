using Microsoft.ML.OnnxRuntime;

namespace LPAP.Onnx.Runtime
{
	/// <summary>
	/// OrtEnv is process-wide. Keep a single accessor so the rest of the library doesn't
	/// accidentally create multiple environments.
	/// </summary>
	public sealed class OnnxEnvironment
	{
		public static OrtEnv Env => OrtEnv.Instance();
	}

	public sealed record CudaDeviceItem(
		int DeviceId,
		string Name,
		long MemoryMB)
	{
		public override string ToString()
			=> $"CUDA {this.DeviceId}: {this.Name} ({this.MemoryMB} MB)";
	}

	public sealed record OnnxModelItem(
		string DisplayName,
		string FullPath)
	{
		public override string ToString() => this.DisplayName;
	}

	public static class OnnxModelEnumerator
	{
		public static List<OnnxModelItem> ListModels(string basePath)
		{
			var list = new List<OnnxModelItem>();

			if (!Directory.Exists(basePath))
			{
				return list;
			}

			foreach (var file in Directory.EnumerateFiles(basePath, "*.onnx", SearchOption.TopDirectoryOnly))
			{
				var name = Path.GetFileNameWithoutExtension(file);
				list.Add(new OnnxModelItem(name, file));
			}

			// Optional: alphabetisch sortieren
			list.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase));

			return list;
		}
	}

}
