using LPAP.Cuda;
using System;
using System.Collections.Generic;
using System.Text;
using Timer = System.Windows.Forms.Timer;

namespace LPAP.Forms
{
	public partial class WindowMain
	{
		private readonly CudaService Cuda = new("");

		internal static string? CudaDevice { get; private set; } = null;


		internal string? SelectedKernelName => this.comboBox_cudaKernels.SelectedItem as string;
		internal Dictionary<string, Type>? KernelArgumentDefinitions => this.Cuda.Initialized ? this.Cuda.GetKernelArguments(this.SelectedKernelName) : null;
		private Dictionary<string, NumericUpDown>? KernelArgumentControls = null;
		internal Dictionary<string, object>? KernelArgumentValues
		{
			get
			{
				if (this.KernelArgumentControls == null)
				{
					return null;
				}

				return this.KernelArgumentControls.Select(kvp => new KeyValuePair<string, object>(kvp.Key, kvp.Value.Value)).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
			}
		}


		private void ListBox_Bind_CudaLog()
		{
			this.listBox_cudaLog.SuspendLayout();
			this.listBox_cudaLog.Items.Clear();

            CudaLog.LogAdded += this.OnLogAdded;
			CudaLog.Info("WindowMain initialized", null, "UI");

            this.listBox_cudaLog.DoubleClick += (s, e) =>
			{
				if (this.listBox_cudaLog.SelectedItem is string)
				{
					Clipboard.SetText(this.listBox_cudaLog.SelectedItem as string ?? "");
				}
			};
			
			this.listBox_cudaLog.HorizontalScrollbar = true;
		}

        private void OnLogAdded(string logEntry)
        {
            if (this.listBox_cudaLog.InvokeRequired)
            {
                this.listBox_cudaLog.Invoke(new Action(() => this.listBox_cudaLog.Items.Add($"[Thread] {logEntry}")));
            }
            else
            {
                this.listBox_cudaLog.Items.Add($"[Main] {logEntry}");
            }
        }

        private void ComboBox_FillCudaDevices()
		{
			this.comboBox_cudaDevices.SuspendLayout();
			this.comboBox_cudaDevices.Items.Clear();

			var deviceNames = this.Cuda.GetAvailableDevices().Values;
			string[] deviceNamesWithIndex = deviceNames.Select((name, index) => $"[{index}]: {name}").ToArray();

            this.comboBox_cudaDevices.Items.AddRange(deviceNamesWithIndex);
			if (this.comboBox_cudaDevices.Items.Count > 0)
			{
				this.comboBox_cudaDevices.SelectedIndex = 0;
            }

			this.comboBox_cudaDevices.ResumeLayout();
		}

		private void ComboBox_FillCudaKernels(string? filter = null)
		{
			this.comboBox_cudaKernels.SuspendLayout();
			this.comboBox_cudaKernels.Items.Clear();
			this.comboBox_cudaKernels.Items.AddRange(this.Cuda.GetKernels(filter).ToArray());
			this.comboBox_cudaKernels.ResumeLayout();
		}

        private async Task UpdateCudaStatistics()
        {
            // Snapshot / compute: darf off-thread passieren
            bool initialized = this.Cuda.Initialized;

            double? load = null;
            double total = 0, free = 0, used = 0;

            if (initialized)
            {
                load = await this.Cuda.GetGpuLoadInPercentAsync().ConfigureAwait(false);

                total = this.Cuda.GetMemoryInBytes(VramStats.Total) / (1024.0 * 1024.0);
                free = this.Cuda.GetMemoryInBytes(VramStats.Free) / (1024.0 * 1024.0);
                used = total - free;
            }

            // UI update: MUSS auf UI thread
            if (this.IsDisposed || !this.IsHandleCreated)
            {
                return;
            }

            this.BeginInvoke(new Action(() =>
            {
                if (this.IsDisposed)
                {
                    return;
                }

                if (initialized)
                {
                    this.label_gpuLoad.Text = "Load: " + (load.HasValue ? load.Value.ToString("F1") + " %" : "N/A");
                    this.label_gpuLoad.ForeColor = load switch
                    {
                        >= 95.0 => System.Drawing.Color.Red,
                        >= 80.0 => System.Drawing.Color.DarkRed,
                        >= 50.0 => System.Drawing.Color.DarkOrange,
                        >= 25.0 => System.Drawing.Color.DarkGoldenrod,
                        >= 10.0 => System.Drawing.Color.Green,
                        _ => System.Drawing.Color.DarkGreen,
                    };

                    this.label_vram.Text = "VRAM: " + used.ToString("F0") + " MB / " + total.ToString("F0") + " MB";

                    int max = (int) Math.Max(1, Math.Min(int.MaxValue, total));
                    int val = (int) Math.Clamp(used, 0, max);

                    this.progressBar_vram.Maximum = max;
                    this.progressBar_vram.Value = val;
                }
                else
                {
                    this.progressBar_vram.Value = 0;
                    this.label_vram.Text = "VRAM: N/A";
                    this.label_gpuLoad.Text = "GPU offline";
                    this.label_gpuLoad.ForeColor = SystemColors.ControlText;
                }
            }));
        }


        private async Task BuildCudaKernelArgsAsync(float inputWidthPart = 0.64f)
		{
			var argDefs = this.KernelArgumentDefinitions;

			this.panel_cudaKernelArguments.SuspendLayout();
			this.panel_cudaKernelArguments.Controls.Clear();

			if (!this.Cuda.Initialized || string.IsNullOrEmpty(this.SelectedKernelName) || argDefs == null || argDefs.Count == 0)
			{
				this.panel_cudaKernelArguments.ResumeLayout();
				return;
			}

			this.KernelArgumentControls = [];
			int panelWidth = this.panel_cudaKernelArguments.ClientSize.Width;
			int yOffset = 5;
			int xOffset = 5;
			await Task.Run(() =>
			{
				foreach (var kvp in argDefs)
				{
					string argName = kvp.Key;
					Type argType = kvp.Value;
					Label lbl = new()
					{
						Text = argName,
						Left = xOffset,
						Top = yOffset + 3,
						Width = (int)(panelWidth * inputWidthPart) - 10,
					};
					NumericUpDown nud = new()
					{
						Left = xOffset + (int)(panelWidth * inputWidthPart),
						Top = yOffset,
						Width = (int)(panelWidth * (1 - inputWidthPart)) - 10,
						DecimalPlaces = (argType == typeof(int) || argType == typeof(long)) ? 0 : argType == typeof(float) ? 5 : 12,
						Increment = 0.1M,
						Minimum = 0,
						Maximum = 1000000,
						Value = 1,
					};
					this.panel_cudaKernelArguments.Invoke(() =>
					{
						this.panel_cudaKernelArguments.Controls.Add(lbl);
						this.panel_cudaKernelArguments.Controls.Add(nud);
					});
					this.KernelArgumentControls[argName] = nud;
					if (argType.Name.Contains('*'))
					{
						nud.Visible = false;
						nud.Value = 0;
					}
					else
					{
						yOffset += nud.Height + 5;
					}
				}
			});
		}



		private void comboBox_cudaDevices_SelectedIndexChanged(object sender, EventArgs e)
		{
			if (this.comboBox_cudaDevices.SelectedIndex < 0)
			{
				this.button_cudaInitialize.Enabled = false;
			}
			else
			{
				this.button_cudaInitialize.Enabled = true;
			}
		}

		private void button_cudaInitialize_Click(object sender, EventArgs e)
		{
			if (this.Cuda.Initialized)
			{
				this.Cuda.Dispose();
				if (this.Cuda.Initialized)
				{
					MessageBox.Show("Error disposing CUDA.", "CUDA Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
					return;
				}

				CudaDevice = null;
				this.button_cudaInitialize.Text = "Initialize";
				this.comboBox_cudaDevices.Enabled = true;
			}
			else
			{
				if (this.comboBox_cudaDevices.SelectedIndex < 0)
				{
					MessageBox.Show("No CUDA Device selected.", "CUDA Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
					return;
				}

				this.Cuda.Initialize(this.comboBox_cudaDevices.SelectedIndex);
				if (!this.Cuda.Initialized)
				{
					MessageBox.Show("Error initializing CUDA.", "CUDA Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
					return;
				}

				if (this.Cuda.DeviceIndex >= 0 && this.Cuda.DeviceIndex < this.Cuda.GetAvailableDevices().Count)
				{
                    CudaDevice = this.Cuda.AvailableDevices.Values.ElementAt(this.Cuda.DeviceIndex);
                    this.button_cudaInitialize.Text = "Dispose";
                    this.comboBox_cudaDevices.Enabled = false;
                }
				else
				{
					CudaLog.Error("CUDA Device index out of range after initialization.");
					MessageBox.Show("CUDA Device index out of range after initialization.", "CUDA Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }

				this.ComboBox_FillCudaKernels();
			}
		}

        private void button_cudaInfo_Click(object sender, EventArgs e)
        {
            string info = string.Empty;
            bool ctrlFlag = (ModifierKeys & Keys.Control) == Keys.Control;
            if (ctrlFlag)
            {
                string[] stats = NvencVideoRenderer.ReadAllLines_LocalStats(false);
                info = string.Join(Environment.NewLine, stats);
                var result = MessageBox.Show(info + Environment.NewLine + Environment.NewLine + " --- Copy to Clipboard? ---", "Hardware Local Stats", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                if (result == DialogResult.Yes)
                {
                    Clipboard.SetText(info);
                }
            }
            else
            {
                if (!this.Cuda.Initialized)
                {
                    MessageBox.Show("CUDA is not initialized.", "CUDA Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                info = string.Join(Environment.NewLine, this.Cuda.GetDeviceInfo());
                MessageBox.Show(info, "CUDA Info [" + this.Cuda.DeviceIndex + "]", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }


        private async void comboBox_cudaKernels_SelectedIndexChanged(object sender, EventArgs e)
		{
			await this.BuildCudaKernelArgsAsync();
		}

		private void button_cudaExecute_Click(object sender, EventArgs e)
		{

		}


	}
}
