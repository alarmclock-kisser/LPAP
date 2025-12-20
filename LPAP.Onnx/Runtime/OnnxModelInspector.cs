using Microsoft.ML.OnnxRuntime;

namespace LPAP.Onnx.Runtime
{
	public static class OnnxModelInspector
	{
		public static string Describe(InferenceSession session)
		{
			var sb = new System.Text.StringBuilder();
			sb.AppendLine("Inputs:");
			foreach (var kv in session.InputMetadata)
			{
				var m = kv.Value;
				sb.AppendLine($"- {kv.Key}: {m.ElementType} [{string.Join(",", m.Dimensions)}]");
			}
			sb.AppendLine("Outputs:");
			foreach (var kv in session.OutputMetadata)
			{
				var m = kv.Value;
				sb.AppendLine($"- {kv.Key}: {m.ElementType} [{string.Join(",", m.Dimensions)}]");
			}
			return sb.ToString();
		}
	}
}
