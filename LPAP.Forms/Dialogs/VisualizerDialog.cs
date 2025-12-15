using LPAP.Audio;
using LPAP.Audio.Processing;
using LPAP.Cuda;
using Microsoft.VisualBasic.FileIO;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace LPAP.Forms.Dialogs
{
	public partial class VisualizerDialog : Form
	{
		private readonly AudioObj Audio;

		private string SelectedResolution => this.comboBox_resolution.SelectedItem as string ?? "1024x512";
		internal Size ParsedResolution
		{
			get
			{
				string[] parts = this.SelectedResolution.Split('x').Select(p => p.Trim()).ToArray();
				if (parts.Length != 2)
				{
					return new Size(1024, 512);
				}
				if (int.TryParse(parts[0], out int width) && int.TryParse(parts[1], out int height))
				{
					return new Size(width, height);
				}
				else
				{
					return new Size(1024, 512);
				}
			}
		}

		private int MaxWorkers => (int) this.numericUpDown_threads.Value;

		internal long ApproxFrameCount => (long) ((this.numericUpDown_endSeconds.Value - this.numericUpDown_startSeconds.Value) * this.numericUpDown_frameRate.Value);
		internal double ApproxSizeInMb => Math.Round(this.ApproxFrameCount * 4.0 / (this.ParsedResolution.Width * this.ParsedResolution.Height), 2);

		public VisualizerDialog(AudioObj audio)
		{
			this.Audio = audio;
			this.InitializeComponent();
			this.InitializeDialogValues();
		}



		internal void InitializeDialogValues()
		{
			this.label_cuda.Text = WindowMain.CudaDevice != null ? $"CUDA: {WindowMain.CudaDevice}" : "CUDA: <Offline>";

			this.numericUpDown_threads.Maximum = Environment.ProcessorCount;
			this.numericUpDown_threads.Value = this.numericUpDown_threads.Maximum - 1;

			this.comboBox_resolution.Items.AddRange(NvencVideoRenderer.CommonResolutions.ToArray());
			if (this.comboBox_resolution.Items.Count > 0)
			{
				this.comboBox_resolution.SelectedIndex = 0;
			}

			this.numericUpDown_endSeconds.Maximum = (decimal) this.Audio.Duration.TotalSeconds;
			this.numericUpDown_endSeconds.Value = this.numericUpDown_endSeconds.Maximum;
			this.numericUpDown_startSeconds.Maximum = this.numericUpDown_endSeconds.Maximum;
			this.numericUpDown_startSeconds.Value = 0;
			this.UpdateApproxInfo();
		}

		internal void UpdateApproxInfo()
		{
			this.label_sizeApprox.Text = $"ca. {this.ApproxFrameCount:N0} frames ({this.ApproxSizeInMb:F2} MB)";
		}


		private void numericUpDown_frameRate_ValueChanged(object sender, EventArgs e)
		{
			this.UpdateApproxInfo();
		}

		private void numericUpDown_startSeconds_ValueChanged(object sender, EventArgs e)
		{
			if (this.numericUpDown_startSeconds.Value > this.numericUpDown_endSeconds.Value)
			{
				this.numericUpDown_endSeconds.Value = Math.Clamp(this.numericUpDown_startSeconds.Value, this.numericUpDown_startSeconds.Minimum, this.numericUpDown_endSeconds.Maximum);
			}

			this.UpdateApproxInfo();
		}

		private void numericUpDown_endSeconds_ValueChanged(object sender, EventArgs e)
		{
			if (this.numericUpDown_endSeconds.Value < this.numericUpDown_startSeconds.Value)
			{
				this.numericUpDown_startSeconds.Value = Math.Clamp(this.numericUpDown_endSeconds.Value, this.numericUpDown_startSeconds.Minimum, this.numericUpDown_endSeconds.Maximum);
			}

			this.UpdateApproxInfo();
		}

		private async void button_render_Click(object sender, EventArgs e)
		{
			AudioObj audio = this.Audio;
			int width = this.ParsedResolution.Width;
			int height = this.ParsedResolution.Height;
			float frameRate = (float) this.numericUpDown_frameRate.Value;
			double startSeconds = (double) this.numericUpDown_startSeconds.Value;
			double endSeconds = (double) this.numericUpDown_endSeconds.Value;
			bool offload = this.checkBox_offload.Checked;

			// If start & end are changed, cut audioobj from audio
			if (startSeconds > 0 || endSeconds < audio.Duration.TotalSeconds)
			{
				long startSample = (long) (startSeconds * audio.SampleRate);
				long endSample = (long) (endSeconds * audio.SampleRate);
				audio = await audio.CopyFromSelectionAsync(startSample, endSample);
			}

			string? outputPath = null;
			IProgress<double> progress = ProgressAdapters.ToProgressBar(this.progressBar_rendering);
			try
			{
				Image[] frames = [];
				if (offload)
				{
					string? tempFramesDir = Path.GetTempPath();
					tempFramesDir = await AudioProcessor.RenderVisualizerImagesToTempDirAsync(audio, width, height, frameRate, null, this.MaxWorkers, progress, null);
					frames = await NvencVideoRenderer.LoadImagesParallelAsync([], tempFramesDir, this.MaxWorkers);
				}
				else
				{
					frames = await AudioProcessor.RenderVisualizerImagesAsync(audio, width, height, frameRate, null, this.MaxWorkers, progress, null);
				}

				this.progressBar_rendering.Invoke(() => this.progressBar_rendering.Value = 0);

				outputPath = await NvencVideoRenderer.NvencRenderVideoAsync(frames, width, height, frameRate, audio, null, null, progress, null);

				if (outputPath != null)
				{
					CudaService.Log("Successfully rendered MP4");
					CudaService.Log(outputPath);
				}
				else
				{
					CudaService.Log("Error rendering video via NVENC");
				}
			}
			catch (Exception ex)
			{
				CudaService.Log(ex);
			}

			this.DialogResult = DialogResult.OK;
			this.Close();
		}
	}
}
