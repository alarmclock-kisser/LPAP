using LPAP.Audio;
using LPAP.Audio.Processing;
using LPAP.Cuda;
using Microsoft.VisualBasic.FileIO;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
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
		internal double ApproxSizeInMb => Math.Round((this.ApproxFrameCount * 4.0 * (this.ParsedResolution.Width * this.ParsedResolution.Height) / (1024.0 * 1024.0)), 2);

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

		private void comboBox_resolution_SelectedIndexChanged(object sender, EventArgs e)
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

			float amplitude = (float) (this.numericUpDown_volume.Value / 100.0m);

			// If start & end are changed, cut audioobj from audio
			if (startSeconds > 0 || endSeconds < audio.Duration.TotalSeconds)
			{
				long startSample = (long) (startSeconds * audio.SampleRate);
				long endSample = (long) (endSeconds * audio.SampleRate);
				audio = await audio.CopyFromSelectionAsync(startSample, endSample);
			}

			Stopwatch sw = Stopwatch.StartNew();

			string? outputPath = null;

			// Double Reporting from 0.0 - 1.0 (growOnly gegen "Zittern")
			IProgress<double> progress = ProgressAdapters.ToProgressBar(
				this.progressBar_rendering, max: 1000, growOnly: true);

			// Optional: Cancellation aus Dialog (falls du eins hast)
			// CancellationToken token = this._cts?.Token ?? CancellationToken.None;
			CancellationToken token = CancellationToken.None;

			// Offload-Fallback Variablen
			string? tempFramesDir = null;
			bool tempFramesDirCreated = false;

			try
			{
				if (offload)
				{
					// --- SSD/OFFLOAD FALLBACK (dein alter Weg, aber RAM-flach dank "framesDir -> NVENC stream") ---

					tempFramesDir = await AudioProcessor.RenderVisualizerImagesToTempDirAsync(
						audio, width, height, frameRate,
						outputFilePath: null,
						maxWorkers: this.MaxWorkers,
						progress: progress,
						ct: token);

					tempFramesDirCreated = !string.IsNullOrWhiteSpace(tempFramesDir);
					if (!tempFramesDirCreated || !Directory.Exists(tempFramesDir))
					{
						throw new Exception("Error creating temporary frames directory for offload rendering.");
					}

					// Reset UI
					this.progressBar_rendering.Invoke(() => this.progressBar_rendering.Value = 0);

					outputPath = await NvencVideoRenderer.NvencRenderVideoAsync(
						framesDir: tempFramesDir,
						width: width,
						height: height,
						frameRate: frameRate,
						audio: audio,
						amplitude: amplitude,
						searchPattern: "*.png",
						outputFilePath: null,
						options: null,
						progress: progress,
						ct: token);
				}
				else
				{
					// --- PRODUCER→CONSUMER PIPELINE (ohne SSD, ohne Image[], ohne Bitmap/Graphics) ---

					// Producer: rendert parallel in BGRA Raster und schreibt in bounded Channel
					var (reader, frameCount) = AudioProcessor.RenderVisualizerFramesBgraChannel(
						audio: audio,
						width: width,
						height: height,
						frameRate: frameRate,
						maxWorkers: this.MaxWorkers,
						channelCapacity: Math.Max(2, this.MaxWorkers * 2),
						progress: progress,     // Render-Phase (0..1)
						ct: token);

					// Reset UI zwischen Render/Encode (optional)
					this.progressBar_rendering.Invoke(() => this.progressBar_rendering.Value = 0);

					if (Math.Abs(amplitude - 1.0f) > 1e-6f)
					{
						await audio.NormalizeAsync(amplitude);
					}

					// Consumer: liest Channel, schreibt Frames in ffmpeg stdin + muxed Audio (RAM -> pipe)
					outputPath = await NvencVideoRenderer.NvencRenderVideoAsync(
						frames: reader,
						frameCount: frameCount,
						width: width,
						height: height,
						frameRate: frameRate,
						audio: audio,
						outputFilePath: null,
						options: null,
						progress: progress,     // Encode-Phase (ffmpeg out_time_ms)
						ct: token);
				}

				if (!string.IsNullOrWhiteSpace(outputPath))
				{
					CudaService.Log("Successfully rendered MP4", $"{sw.Elapsed.TotalSeconds:F3} elapsed", 0, "Visualizer");
					CudaService.Log(outputPath, "", 0, "Visualizer");
				}
				else
				{
					CudaService.Log("Error rendering video via NVENC", "", 0, "Visualizer");
				}
			}
			catch (OperationCanceledException)
			{
				CudaService.Log("Rendering cancelled.", "", 0, "Visualizer");
			}
			catch (Exception ex)
			{
				CudaService.Log(ex);
			}
			finally
			{
				this.Audio["visualizer"] = sw.Elapsed.TotalSeconds;
				sw.Stop();

				// Nur falls offload genutzt wurde: TempFramesDir löschen
				try
				{
					if (tempFramesDirCreated && !string.IsNullOrWhiteSpace(tempFramesDir) && Directory.Exists(tempFramesDir))
						Directory.Delete(tempFramesDir, recursive: true);
				}
				catch (Exception ex)
				{
					CudaService.Log(ex, "Error deleting temporary frames directory", 0, "Visualizer");
				}

				this.progressBar_rendering.Invoke(() => this.progressBar_rendering.Value = 0);
			}

			this.DialogResult = DialogResult.OK;
			this.Close();
		}


	}
}
