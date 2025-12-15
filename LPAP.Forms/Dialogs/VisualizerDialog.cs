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

        private CancellationTokenSource? _renderCts;
        private bool _isRendering;

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
			this.label_sizeApprox.Text = $"{this.ApproxFrameCount:N0} frames ({(this.ApproxSizeInMb > 2048 ? this.ApproxSizeInMb / 1024.0 : this.ApproxSizeInMb):F2} {(this.ApproxSizeInMb > 2048 ? "GB" : "MB")})";
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
            // --- CANCEL FALL ---
            if (this._isRendering)
            {
                this._renderCts?.Cancel();
                return;
            }

            this._isRendering = true;
            this.button_render.Text = "Cancel";
            this.button_render.Enabled = true;

            this._renderCts = new CancellationTokenSource();
            CancellationToken token = this._renderCts.Token;

            Stopwatch sw = Stopwatch.StartNew();
            string? outputPath = null;

            // Progress 0..1 (growOnly gegen Zittern)
            IProgress<double> uiProgress = ProgressAdapters.ToProgressBar(this.progressBar_rendering, max: 1000, growOnly: true);

            try
            {
                AudioObj audio = this.Audio;

                int width = this.ParsedResolution.Width;
                int height = this.ParsedResolution.Height;
                float frameRate = (float) this.numericUpDown_frameRate.Value;

                double startSeconds = (double) this.numericUpDown_startSeconds.Value;
                double endSeconds = (double) this.numericUpDown_endSeconds.Value;

                float volume = (float) (this.numericUpDown_volume.Value / 100.0m);

                // --- 1) SELECTION CUT ---
                if (startSeconds > 0 || endSeconds < audio.Duration.TotalSeconds)
                {
                    long startSample = (long) (startSeconds * audio.SampleRate);
                    long endSample = (long) (endSeconds * audio.SampleRate);

                    audio = await audio.CopyFromSelectionAsync(startSample, endSample).ConfigureAwait(true);
                }

                token.ThrowIfCancellationRequested();

                // --- 2) NORMALIZE / VOLUME ---
                if (!float.IsNaN(volume) && volume > 0 && Math.Abs(volume - 1.0f) > 1e-4f)
                {
                    await audio.NormalizeAsync(volume).ConfigureAwait(true);
                }

                token.ThrowIfCancellationRequested();

                // --- 3) PHASED PROGRESS ---
                double renderPhase = 0.35;

                IProgress<double> renderProgress = new Progress<double>(p =>
                {
                    uiProgress.Report(Math.Clamp(p, 0, 1) * renderPhase);
                });

                IProgress<double> encodeProgress = new Progress<double>(p =>
                {
                    uiProgress.Report(renderPhase + Math.Clamp(p, 0, 1) * (1.0 - renderPhase));
                });

                // --- 4) PRODUCER: BGRA FRAMES ---
                var (reader, frameCount) =
                    AudioProcessor.RenderVisualizerFramesBgraChannel(
                        audio,
                        width,
                        height,
                        frameRate,
                        amplification: (float) (this.numericUpDown_amplification.Value / 100.0m),
                        maxWorkers: this.MaxWorkers,
                        channelCapacity: 0,
                        progress: renderProgress,
                        ct: token);

                // --- 5) CONSUMER: NVENC ---
                outputPath = await NvencVideoRenderer.NvencRenderVideoAsync(
                    reader,
                    frameCount,
                    width,
                    height,
                    frameRate,
                    audio,
                    outputFilePath: null,
                    options: null,
                    progress: encodeProgress,
                    ct: token);

                if (!string.IsNullOrWhiteSpace(outputPath))
                {
                    double sizeInMb = 0;
                    try
                    {
                        FileInfo fi = new FileInfo(outputPath);
                        sizeInMb = fi.Length / (1024.0 * 1024.0);
                    }
                    catch { }

                    CudaService.Log($"Successfully rendered MP4 ({sizeInMb:F2} MB)", $"{sw.Elapsed.TotalSeconds:F3}s", 0, "Visualizer");
                    CudaService.Log(sw.Elapsed.TotalSeconds.ToString("F1") + " sec.: " + outputPath, "", 0, "Visualizer");
                    
                    if (this.checkBox_copyPath.Checked)
                    {
                        try
                        {
                            Clipboard.SetText(outputPath);
                        }
                        catch { }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                CudaService.Log("Rendering cancelled by user.", "", 0, "Visualizer");
            }
            catch (Exception ex)
            {
                CudaService.Log(ex);
            }
            finally
            {
                sw.Stop();

                this.Audio["visualizer"] = sw.Elapsed.TotalSeconds;

                this._isRendering = false;
                this.button_render.Text = "Render";
                this._renderCts?.Dispose();
                this._renderCts = null;

                try
                {
                    this.progressBar_rendering.Invoke(() =>
                        this.progressBar_rendering.Value = 0);
                }
                catch { }
            }

            this.DialogResult = DialogResult.OK;
            this.Close();
        }


    }
}
