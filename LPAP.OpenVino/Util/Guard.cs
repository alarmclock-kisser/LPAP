namespace LPAP.OpenVino.Util
{
	internal static class Guard
	{
		public static void NotNull(object? value, string name)
		{
			if (value is null)
			{
				throw new ArgumentNullException(name);
			}
		}

		public static void NotNullOrWhiteSpace(string? value, string name)
		{
			if (string.IsNullOrWhiteSpace(value))
			{
				throw new ArgumentException("Value must not be null/empty.", name);
			}
		}

		public static void True(bool condition, string message)
		{
			if (!condition)
			{
				throw new InvalidOperationException(message);
			}
		}
	}
}
