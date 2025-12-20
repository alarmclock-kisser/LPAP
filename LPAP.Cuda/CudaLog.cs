using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;

namespace LPAP.Cuda
{
	public static class CudaLog
	{
		private static readonly Lock Sync = new();
		private static readonly Queue<string> Buffer = new();

		internal static int MaxEntries { get; set; } = 1024;
		internal static bool Verbose { get; set; }
		internal static string? LogFilePath { get; set; }

		public static void Info(string message, string? detail = null, [CallerMemberName] string? caller = null)
			=> Write("INFO", message, detail, caller);

		public static void Debug(string message, string? detail = null, [CallerMemberName] string? caller = null)
		{
			if (Verbose)
			{
				Write("DEBUG", message, detail, caller);
			}
		}

		public static void Warn(string message, string? detail = null, [CallerMemberName] string? caller = null)
			=> Write("WARN", message, detail, caller);

		public static void Error(string message, string? detail = null, [CallerMemberName] string? caller = null)
			=> Write("ERROR", message, detail, caller);

		public static void Error(Exception exception, string? detail = null, [CallerMemberName] string? caller = null)
			=> Write("ERROR", exception.Message, detail ?? exception.InnerException?.Message, caller);



		public static event Action<string>? LogAdded;

		private static void Write(string level, string message, string? detail, string? caller)
		{
			string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
			string origin = string.IsNullOrWhiteSpace(caller) ? "CUDA" : caller!;
			string payload = string.IsNullOrWhiteSpace(detail) ? message : $"{message} ({detail})";
			string entry = $"[{timestamp}] {level.PadRight(5)} {origin}: {payload}";

			lock (Sync)
			{
				Buffer.Enqueue(entry);
				while (Buffer.Count > MaxEntries && Buffer.TryDequeue(out _))
				{
				}

				if (!string.IsNullOrWhiteSpace(LogFilePath))
				{
					try
					{
						File.AppendAllLines(LogFilePath!, [entry]);
					}
					catch
					{
						// ignore logging failures
					}
				}
			}

			Console.WriteLine(entry);

			// Notify subscribers
			LogAdded?.Invoke(entry);
		}
	}
}
