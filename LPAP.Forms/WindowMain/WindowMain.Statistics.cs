using LPAP.Cuda;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;

namespace LPAP.Forms
{
	public partial class WindowMain
	{
		public float[] CoreUsages { get; private set; } = [];
		public double MaxMemoryKb { get; private set; } = 0;
		public double UsedMemoryKb { get; private set; } = 0;


		private Timer _statisticsTimer;
		private bool _statsInitialized;
		private volatile int _statsRunning; // 0 = idle, 1 = running

		 // ---- configurable statistics sampling ----
		// default delay used before initialization
		private int _statsUpdateDelayMs = 250;
		private bool _statsEnabled = true;

		// Expose toggles for timer enable and update delay
		[Browsable(false)]
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public bool StatisticsEnabled
		{
			get => this._statsEnabled;
			set
			{
				this._statsEnabled = value;
				// lambda-like setter behavior: apply immediately if timer exists
				if (this._statisticsTimer != null)
				{
					try
					{
						if (value)
						{
							this._statisticsTimer.Start();
						}
						else
						{
							this._statisticsTimer.Stop();
						}
					}
					catch { }
				}
			}
		}

		[Browsable(false)]
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public int StatisticsUpdateDelayMs
		{
			get => this._statsUpdateDelayMs;
			set
			{
				// sanitize
				int v = value <= 0 ? 1 : value;
				this._statsUpdateDelayMs = v;
				// lambda-like setter: update interval immediately if timer exists
				if (this._statisticsTimer != null)
				{
					try { this._statisticsTimer.Interval = v; } catch { }
				}
			}
		}

		// Initialize statistics sampling timer (configurable delay) and return it so caller can assign mandatory attribute
		internal Timer InitializeStatisticsTimer()
		{
			if (this._statsInitialized && this._statisticsTimer != null)
			{
				return this._statisticsTimer;
			}

			var timer = new Timer
			{
				Interval = this._statsUpdateDelayMs
			};
			timer.Tick += this.StatisticsTimer_Tick;
			// respect enabled toggle
			if (this._statsEnabled)
			{
				timer.Start();
			}
			this._statisticsTimer = timer;
			this._statsInitialized = true;
			return timer;
		}

        private void StatisticsTimer_Tick(object? sender, EventArgs e)
        {
            if (Interlocked.CompareExchange(ref this._statsRunning, 1, 0) != 0)
            {
                return;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    long totalBytes = StatisticsExtensions.GetTotalMemoryBytes();
                    long usedBytes = StatisticsExtensions.GetUsedMemoryBytes();
                    this.MaxMemoryKb = totalBytes / 1024.0;
                    this.UsedMemoryKb = usedBytes / 1024.0;

                    this.UpdateMemoryUiSafe();

                    var usages = await StatisticsExtensions.GetThreadUsagesAsync().ConfigureAwait(false);
                    this.CoreUsages = usages;

                    if (this.pictureBox_cores.Width > 0 && this.pictureBox_cores.Height > 0)
                    {
                        var bmp = await RenderCoresBitmapAsync(
                            usages,
                            this.pictureBox_cores.Width,
                            this.pictureBox_cores.Height,
                            this.pictureBox_cores.BackColor,
                            CancellationToken.None).ConfigureAwait(false);

                        if (this.IsDisposed || !this.IsHandleCreated)
                        {
                            return;
                        }

                        this.BeginInvoke(new Action(() =>
                        {
                            var old = this.pictureBox_cores.Image;
                            this.pictureBox_cores.Image = bmp;
                            old?.Dispose();
                        }));
                    }
                }
                catch (Exception ex)
                {
                    // NICHT komplett schlucken: zumindest loggen
                    CudaLog.Error(ex, "StatisticsTimer_Tick worker failed", "Stats");
                }
                finally
                {
                    try
                    {
                        await this.UpdateCudaStatistics().ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        CudaLog.Error(ex, "UpdateCudaStatistics failed", "Stats");
                    }

                    Interlocked.Exchange(ref this._statsRunning, 0);
                }
            });
        }


        private void UpdateMemoryUiSafe()
		{
			try
			{
				if (this.IsHandleCreated)
				{
					this.BeginInvoke(new Action(() =>
					{
						// progress bar percentage based on used/total
						int percent = 0;
						if (this.MaxMemoryKb > 0)
						{
							percent = (int) Math.Clamp(Math.Round(this.UsedMemoryKb / this.MaxMemoryKb * 100.0), 0, 100);
						}
						this.progressBar_memory.Minimum = 0;
						this.progressBar_memory.Maximum = 100;
						this.progressBar_memory.Value = percent;

						// Show GB with 2 decimals
						double usedGb = this.UsedMemoryKb / (1024.0 * 1024.0);
						double totalGb = this.MaxMemoryKb / (1024.0 * 1024.0);
						this.label_memory.Text = $"RAM: {usedGb:F2} GB / {totalGb:F2} GB";
					}));
				}
			}
			catch { }
		}

		// Async bitmap renderer for core/thread loads, non-blocking; parallel fill per cell
		private static Task<Bitmap> RenderCoresBitmapAsync(float[] usages, int width, int height, Color backColor, CancellationToken ct)
		{
			return Task.Run(() =>
			{
				ct.ThrowIfCancellationRequested();

				int count = Math.Max(1, usages?.Length ?? 1);

				// Compute grid: try to make it as square as possible
				int cols = (int) Math.Ceiling(Math.Sqrt(count));
				int rows = (int) Math.Ceiling(count / (double) cols);

				var bmp = new Bitmap(Math.Max(1, width), Math.Max(1, height));
				using (var g = Graphics.FromImage(bmp))
				{
					g.Clear(backColor);
					g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
					g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;

					// Padding and cell sizes
					int pad = 2;
					int gridW = width - pad * (cols + 1);
					int gridH = height - pad * (rows + 1);
					if (gridW < cols)
					{
						gridW = cols;
					}

					if (gridH < rows)
					{
						gridH = rows;
					}

					int cellW = gridW / cols;
					int cellH = gridH / rows;

					using var borderPen = new Pen(Color.Black, 1f);
					using var fillBrush = new SolidBrush(Color.FromArgb(64, 160, 255));
					using var highBrush = new SolidBrush(Color.FromArgb(255, 96, 96));

					for (int i = 0; i < count; i++)
					{
						ct.ThrowIfCancellationRequested();
						int r = i / cols;
						int c = i % cols;
						int x = pad + c * (cellW + pad);
						int y = pad + r * (cellH + pad);

						// Outer rect
						var rect = new Rectangle(x, y, cellW, cellH);
						g.DrawRectangle(borderPen, rect);

						// Fill proportionally from bottom based on usage
						float u = usages[i];
						if (u < 0f)
						{
							u = 0f;
						}

						if (u > 1f)
						{
							u = 1f;
						}

						int filledH = (int) Math.Round(u * cellH);
						if (filledH > 0)
						{
							var fillRect = new Rectangle(x + 1, y + cellH - filledH + 1, Math.Max(1, cellW - 2), Math.Max(1, filledH - 2));
							// use red above 80%
							var brush = u >= 0.8f ? highBrush : fillBrush;
							g.FillRectangle(brush, fillRect);
						}
					}
				}

				return bmp;
			}, ct);
		}
	}



	public static class StatisticsExtensions
	{
		// -------- CPU pro logischem Prozessor (async, non-blocking) --------

		private static readonly PerformanceCounter[] _cpuCounters = CreateCpuCounters();
		private static readonly TimeSpan _samplingInterval = TimeSpan.FromMilliseconds(250);
		private static DateTime _lastSampleUtc = DateTime.MinValue;
		private static float[] _lastUsages = [];
		private static readonly Lock _sampleLock = new();

		private static PerformanceCounter[] CreateCpuCounters()
		{
			int coreCount = Environment.ProcessorCount;
			var counters = new PerformanceCounter[coreCount];

			for (int i = 0; i < coreCount; i++)
			{
				counters[i] = new PerformanceCounter("Processor", "% Processor Time", i.ToString(), true);
				// erste Probe, damit der nächste Wert „richtig“ ist
				_ = counters[i].NextValue();
			}

			_lastUsages = new float[coreCount];
			return counters;
		}

		/// <summary>
		/// CPU-Auslastung pro logischem Prozessor (0.0f - 1.0f).
		/// Nicht-blockierend: liefert gecachte Werte, wenn Intervall noch nicht abgelaufen.
		/// </summary>
		public static Task<float[]> GetThreadUsagesAsync(CancellationToken cancellationToken = default)
		{
			lock (_sampleLock)
			{
				var now = DateTime.UtcNow;
				var elapsed = now - _lastSampleUtc;
				if (elapsed < _samplingInterval && _lastUsages.Length == _cpuCounters.Length)
				{
					return Task.FromResult((float[]) _lastUsages.Clone());
				}

				int coreCount = _cpuCounters.Length;
				var usages = new float[coreCount];

				for (int i = 0; i < coreCount; i++)
				{
					float percent = _cpuCounters[i].NextValue(); // 0..100
					if (percent < 0f)
					{
						percent = 0f;
					}

					if (percent > 100f)
					{
						percent = 100f;
					}

					usages[i] = percent / 100f;
				}

				_lastUsages = usages;
				_lastSampleUtc = now;
				return Task.FromResult((float[]) usages.Clone());
			}
		}

		/// <summary>
		/// Sync-Wrapper, falls du irgendwo keine async-Methode aufrufen willst.
		/// </summary>
		public static float[] GetThreadUsages()
			=> GetThreadUsagesAsync().GetAwaiter().GetResult();


		// -------- Speicher (physisch) --------
		// Die Speicherabfragen sind sehr schnell und blockieren nicht nennenswert.
		// Async bringt hier praktisch nichts, daher bleiben sie synchron.

		[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
		private struct MEMORYSTATUSEX
		{
			public uint dwLength;
			public uint dwMemoryLoad;
			public ulong ullTotalPhys;
			public ulong ullAvailPhys;
			public ulong ullTotalPageFile;
			public ulong ullAvailPageFile;
			public ulong ullTotalVirtual;
			public ulong ullAvailVirtual;
			public ulong ullAvailExtendedVirtual;
		}

		[DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
		private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

		private static MEMORYSTATUSEX GetMemoryStatus()
		{
			var status = new MEMORYSTATUSEX
			{
				dwLength = (uint) Marshal.SizeOf<MEMORYSTATUSEX>()
			};

			if (!GlobalMemoryStatusEx(ref status))
			{
				throw new Win32Exception(Marshal.GetLastWin32Error());
			}

			return status;
		}

		/// <summary>
		/// Gesamter physischer Speicher in BYTES.
		/// </summary>
		public static long GetTotalMemoryBytes()
		{
			var status = GetMemoryStatus();
			return (long) status.ullTotalPhys;
		}

		/// <summary>
		/// Verwendeter physischer Speicher in BYTES.
		/// </summary>
		public static long GetUsedMemoryBytes()
		{
			var status = GetMemoryStatus();
			ulong used = status.ullTotalPhys - status.ullAvailPhys;
			return (long) used;
		}
	}
}
