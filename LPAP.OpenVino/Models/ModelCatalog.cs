using LPAP.OpenVino.Util;

namespace LPAP.OpenVino.Models
{
	internal static class ModelCatalog
	{
		public static IReadOnlyList<(string name, string xmlPath, string? binPath)> Scan(string modelsRootDir)
		{
			Guard.NotNullOrWhiteSpace(modelsRootDir, nameof(modelsRootDir));
			if (!Directory.Exists(modelsRootDir))
			{
				return Array.Empty<(string, string, string?)>();
			}

			var xmls = Directory.EnumerateFiles(modelsRootDir, "*.xml", SearchOption.AllDirectories)
				.OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
				.ToList();

			var list = new List<(string, string, string?)>(xmls.Count);
			foreach (var xml in xmls)
			{
				var bin = Path.ChangeExtension(xml, ".bin");
				string? binPath = File.Exists(bin) ? bin : null;

				var name = Path.GetFileNameWithoutExtension(xml);
				list.Add((name, xml, binPath));
			}
			return list;
		}
	}
}
