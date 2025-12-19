using Microsoft.ML.OnnxRuntime;

namespace LPAP.Onnx.Runtime;

public static class OnnxSessionFactory
{
	public static SessionOptions CreateSessionOptions(OnnxOptions options)
	{
		var so = new SessionOptions
		{
			GraphOptimizationLevel = options.GraphOptimizationLevel,
		};

		// You can tune these if you want; for many audio models, ORT internal threading is fine.
		// so.IntraOpNumThreads = Environment.ProcessorCount;
		// so.InterOpNumThreads = 1;

		if (options.PreferCuda)
		{
			TryAppendCuda(so, options.DeviceId);
		}

		return so;
	}

	private static void TryAppendCuda(SessionOptions so, int deviceId)
	{
		try
		{
			// This method exists in the ORT GPU package for Windows.
			so.AppendExecutionProvider_CUDA(deviceId);
		}
		catch
		{
			// If CUDA EP is unavailable/misconfigured, silently fall back to CPU.
		}
	}
}
