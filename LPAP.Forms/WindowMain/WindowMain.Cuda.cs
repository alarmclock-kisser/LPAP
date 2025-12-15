using LPAP.Cuda;
using System;
using System.Collections.Generic;
using System.Text;
using Timer = System.Windows.Forms.Timer;

namespace LPAP.Forms
{
	public partial class WindowMain
	{
		private readonly CudaService Cuda = new();

		internal static string? CudaDevice { get; private set; } = null;


		internal string? SelectedKernelName => this.comboBox_cudaKernels.SelectedItem as string;
		internal Dictionary<string, Type>? KernelArgumentDefinitions => this.Cuda.Initialized ? this.Cuda.GetKernelArgumentDefinitions(this.SelectedKernelName) : null;
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

			this.listBox_cudaLog.DataSource = CudaService.LogEntries;
			CudaService.LogEntryAdded += (s, e) =>
			{
				this.listBox_cudaLog.TopIndex = this.listBox_cudaLog.Items.Count - 1;
			};

			this.listBox_cudaLog.DoubleClick += (s, e) =>
			{
				if (this.listBox_cudaLog.SelectedItem is string)
				{
					Clipboard.SetText(this.listBox_cudaLog.SelectedItem as string ?? "");
				}
			};
			
			this.listBox_cudaLog.HorizontalScrollbar = true;
		}

		private void ComboBox_FillCudaDevices()
		{
			this.comboBox_cudaDevices.SuspendLayout();
			this.comboBox_cudaDevices.Items.Clear();

			this.comboBox_cudaDevices.DataSource = this.Cuda.DeviceEntries;

			this.comboBox_cudaDevices.ResumeLayout();
		}

		private void ComboBox_FillCudaKernels(string? filter = null)
		{
			this.comboBox_cudaKernels.SuspendLayout();
			this.comboBox_cudaKernels.Items.Clear();
			this.comboBox_cudaKernels.Items.AddRange(this.Cuda.GetAvailableKernels(filter).ToArray());
			this.comboBox_cudaKernels.ResumeLayout();
		}

		private void UpdateCudaStatistics()
		{
			if (this.Cuda.Initialized)
			{
				double load = this.Cuda.GpuLoadPercent;
				double total = this.Cuda.TotalMemoryMb;
				double used = this.Cuda.AllocatedMemoryMb;
				double free = this.Cuda.AvailableMemoryMb;

				this.label_gpuLoad.Text = "Load: " + load.ToString("F2") + " %";
				this.label_vram.Text = "Vram: " + used.ToString("F2") + " MB / " + total.ToString("F2") + " MB";
				this.progressBar_vram.Maximum = (int) total;
				this.progressBar_vram.Value = (int) used;

			}
			else
			{
				this.progressBar_vram.Value = 0;
				this.label_vram.Text = $"VRAM: N/A";
				this.label_gpuLoad.Text = $"GPU offline";
			}
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

				CudaDevice = this.Cuda.SelectedDevice;
				this.button_cudaInitialize.Text = "Dispose";
				this.comboBox_cudaDevices.Enabled = false;

				this.ComboBox_FillCudaKernels();
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
