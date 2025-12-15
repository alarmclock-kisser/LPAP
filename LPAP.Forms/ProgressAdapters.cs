using System;
using System.Windows.Forms;

public static class ProgressAdapters
{
	public static IProgress<double> ToProgressBar(
		ProgressBar bar,
		int max = 1000)
	{
		if (bar == null)
		{
			throw new ArgumentNullException(nameof(bar));
		}

		bar.Minimum = 0;
		bar.Maximum = max;
		bar.Value = 0;

		return new Progress<double>(p =>
		{
			if (double.IsNaN(p) || double.IsInfinity(p))
			{
				return;
			}

			p = Math.Clamp(p, 0.0, 1.0);
			int value = (int) Math.Round(p * max);

			if (bar.IsDisposed)
			{
				return;
			}

			if (bar.InvokeRequired)
			{
				bar.BeginInvoke(() =>
				{
					if (!bar.IsDisposed)
					{
						bar.Value = Math.Min(bar.Maximum, Math.Max(bar.Minimum, value));
					}
				});
			}
			else
			{
				bar.Value = Math.Min(bar.Maximum, Math.Max(bar.Minimum, value));
			}
		});
	}
}
