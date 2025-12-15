using LPAP.Audio;
using LPAP.Audio.Processing;
using LPAP.Cuda;
using Microsoft.VisualBasic.Devices;
using Microsoft.VisualBasic.FileIO;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;

namespace LPAP.Forms.Dialogs
{
    public partial class VisualizerDialog : Form
    {
        private readonly AudioObj Audio;

        private CancellationTokenSource? _renderCts;
        private bool _isRendering;

        private DateTime? _renderingStarted = null;
        private readonly Timer _timeTimer = new();
        private double _lastProgress01 = 0.0;
        private readonly Queue<(double tSec, double p)> _progressSamples = new();
        private double _estimatedTotalSec = 0.0;
        private bool _hadAnyProgress = false;
        private double _estimatedFramesPerSec = 0.0;
        private int _totalFrameCount = 0;



        private string SelectedResolution => this.comboBox_resolution.SelectedItem as string ?? "1024x512";
        internal Size ParsedResolution
        {
            get
            {
                string[] parts = this.SelectedResolution.Replace("Current:", "").Trim().Split('x').Select(p => p.Trim()).ToArray();
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




        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (this._isRendering)
            {
                try { this._renderCts?.Cancel(); } catch { }
            }

            try { this._timeTimer.Stop(); } catch { }
            base.OnFormClosing(e);
        }


        internal void InitializeDialogValues()
        {
            this.label_cuda.Text = WindowMain.CudaDevice != null ? $"CUDA: {WindowMain.CudaDevice}" : "CUDA: <Offline>";
            this.comboBox_preset.DataSource = NvencVideoRenderer.GetAllPresetEntries(!string.IsNullOrEmpty(WindowMain.CudaDevice));
            this.comboBox_preset.DisplayMember = nameof(NvencVideoRenderer.PresetEntry.DisplayName);
            this.comboBox_preset.ValueMember = nameof(NvencVideoRenderer.PresetEntry.Options);


            this.numericUpDown_threads.Maximum = Environment.ProcessorCount;
            this.numericUpDown_threads.Value = this.numericUpDown_threads.Maximum - 1;

            this.numericUpDown_frameRate.Value = (decimal) (WindowsScreenHelper.GetScreenRefreshRate(this) ?? 24.00f);

            Size currentRes = WindowsScreenHelper.GetScreenSize(this) ?? new Size(1024, 1024);
            this.comboBox_resolution.Items.Clear();
            this.comboBox_resolution.Items.Add("Current: " + $"{currentRes.Width}x{currentRes.Height}");
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

            this.SetupTimeTimer();
            this.ResetTimeLabel();

        }

        internal void UpdateApproxInfo()
        {
            this.label_sizeApprox.Text = $"{this.ApproxFrameCount:N0} frames ({(this.ApproxSizeInMb > 2048 ? this.ApproxSizeInMb / 1024.0 : this.ApproxSizeInMb):F2} {(this.ApproxSizeInMb > 2048 ? "GB" : "MB")})";
        }

        private void SetupTimeTimer()
        {
            this._timeTimer.Interval = 250;
            this._timeTimer.Tick += (_, __) => this.TimeTimer_Tick();
        }

        private void ResetTimeLabel()
        {
            try
            {
                this.label_time.Text = "0.00s / ~0.00s";
            }
            catch { }
        }

        private void TimeTimer_Tick()
        {
            if (!this._isRendering || !this._renderingStarted.HasValue)
            {
                this.ResetTimeLabel();
                return;
            }

            double elapsedSec = (DateTime.UtcNow - this._renderingStarted.Value).TotalSeconds;

            string left = FormatTime(elapsedSec);

            string right;
            if (!this._hadAnyProgress || this._estimatedTotalSec <= 0.01)
            {
                right = "0.00s";
            }
            else
            {
                // bei 100% exakt abschließen
                if (this._lastProgress01 >= 0.999999)
                {
                    this._estimatedTotalSec = elapsedSec;
                }

                right = FormatTime(this._estimatedTotalSec);
            }

            this.label_time.Text = $"{left} / ~{right}";

            if (this._estimatedFramesPerSec > 0.01)
            {
                this.label_framesPerSec.Text = "~ " + this._estimatedFramesPerSec.ToString("F2") + " fps";
            }
            else
            {
                this.label_framesPerSec.Text = "~ 0.00 fps";
            }
        }


        private static string FormatTime(double seconds)
        {
            if (seconds < 0)
            {
                seconds = 0;
            }

            // < 60s: 0.00s
            if (seconds < 60.0)
            {
                return $"{seconds:0.00}s";
            }

            // >= 60s: m:ss (minuten minimal, sekunden ganz)
            int total = (int) Math.Floor(seconds);
            int m = total / 60;
            int s = total % 60;
            return $"{m}:{s:00}m";
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

            // Timer/ETA start
            this._renderingStarted = DateTime.UtcNow;
            this._estimatedFramesPerSec = 0.0;

            this._lastProgress01 = 0.0;
            this._estimatedTotalSec = 0.0;
            this._hadAnyProgress = false;
            this._progressSamples.Clear();

            this.ResetTimeLabel();
            this._timeTimer.Start();

            // Progress 0..1
            IProgress<double> uiProgressBase = ProgressAdapters.ToProgressBar(this.progressBar_rendering, max: 1000, growOnly: true);

            IProgress<double> uiProgress = new Progress<double>(p =>
            {
                p = Math.Clamp(p, 0.0, 1.0);
                uiProgressBase.Report(p);
            });

            var opts = ((NvencVideoRenderer.PresetEntry?) this.comboBox_preset.SelectedItem)?.Options ?? NvencVideoRenderer.CpuPresets.X264_Default;
            string mainAssemblyName = System.Reflection.Assembly.GetExecutingAssembly().GetName().Name?.Split('.').FirstOrDefault() ?? "NVENC";

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
                    p = Math.Clamp(p, 0.0, 1.0);

                    // UI progress (phased)
                    uiProgress.Report(renderPhase + p * (1.0 - renderPhase));

                    // ETA/FPS NUR aus encode-progress
                    if (p > this._lastProgress01 + 1e-6)
                    {
                        double nowSec =
                            (DateTime.UtcNow - this._renderingStarted!.Value).TotalSeconds;

                        this._lastProgress01 = p;
                        this._hadAnyProgress = true;

                        this.PushProgressSample(nowSec, p);
                        this.RecomputeEtaFromSamples(nowSec, p);
                        this.UpdateFpsEstimateFromSamples();
                    }
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

                this._totalFrameCount = frameCount;

                // --- 5) CONSUMER: NVENC ---
                outputPath = await NvencVideoRenderer.NvencRenderVideoAsync(
                    reader,
                    frameCount,
                    width,
                    height,
                    frameRate,
                    audio,
                    outputFilePath: NvencVideoRenderer.SanitizeFileName(mainAssemblyName + "_" + this.Audio.Name + (this.Audio.BeatsPerMinute > 0 ? "[" + this.Audio.BeatsPerMinute.ToString("F2") + "]" : "")),
                    options: opts,
                    progress: encodeProgress,
                    ct: token);

                if (!string.IsNullOrWhiteSpace(outputPath))
                {
                    double sizeInMb = 0;
                    try
                    {
                        FileInfo fi = new(outputPath);
                        sizeInMb = fi.Length / (1024.0 * 1024.0);
                    }
                    catch { }

                    CudaLog.Info($"Successfully rendered MP4 ({sizeInMb:F2} MB)", $"{sw.Elapsed.TotalSeconds:F3}s", "Visualizer");
                    CudaLog.Info(sw.Elapsed.TotalSeconds.ToString("F1") + " sec.: " + outputPath, "", "Visualizer");

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
                CudaLog.Info("Rendering cancelled by user.", "", "Visualizer");
            }
            catch (Exception ex)
            {
                CudaLog.Error(ex, "NVENC-Error.", "Visualizer");
            }
            finally
            {
                sw.Stop();
                try { this._timeTimer.Stop(); } catch { }
                this._estimatedFramesPerSec = 0.0;
                this.label_framesPerSec.Text = "~ 0.00 fps";

                this.Audio["visualizer"] = sw.Elapsed.TotalSeconds;

                this._isRendering = false;
                this.button_render.Text = "Render";
                this._renderCts?.Dispose();
                this._renderCts = null;

                try
                {
                    if (!string.IsNullOrEmpty(outputPath))
                    {
                        double avgFrameRate = this._totalFrameCount / sw.Elapsed.TotalSeconds;
                        var fi = new FileInfo(outputPath);
                        NvencVideoRenderer.WriteVideoRenderingResult_To_LocalStats(opts.VideoCodec, sw.Elapsed.TotalSeconds, avgFrameRate, fi.Length);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex);
                }

                try { this.progressBar_rendering.Invoke(() => this.progressBar_rendering.Value = 0); } catch { }
            }

            this.DialogResult = DialogResult.OK;
            this.Close();
        }




        private void PushProgressSample(double elapsedSec, double progress01)
        {
            this._progressSamples.Enqueue((elapsedSec, progress01));

            // Nur letzte ~3 Sekunden behalten (bei 250ms updates wären das ~12 Samples, bei unregelmäßigem progress weniger)
            while (this._progressSamples.Count > 0 && (elapsedSec - this._progressSamples.Peek().tSec) > 3.0)
            {
                this._progressSamples.Dequeue();
            }
        }

        private void RecomputeEtaFromSamples(double elapsedSec, double progress01)
        {
            if (this._progressSamples.Count < 2 || progress01 <= 0.0005)
            {
                return;
            }

            var first = this._progressSamples.Peek();
            var last = this._progressSamples.Last();

            double dt = last.tSec - first.tSec;
            double dp = last.p - first.p;

            if (dt < 0.15 || dp < 0.0001)
            {
                return;
            }

            double speed = dp / dt;
            if (speed <= 1e-9)
            {
                return;
            }

            double remaining = (1.0 - progress01) / speed;
            double total = elapsedSec + Math.Max(0, remaining);

            if (this._estimatedTotalSec <= 0.01)
            {
                this._estimatedTotalSec = total;
            }
            else
            {
                this._estimatedTotalSec = this._estimatedTotalSec * 0.6 + total * 0.4;
            }
        }


        private void UpdateFpsEstimateFromSamples()
        {
            if (this._progressSamples.Count < 2 || this._totalFrameCount <= 0)
            {
                return;
            }

            var first = this._progressSamples.Peek();
            var last = this._progressSamples.Last();

            double dt = last.tSec - first.tSec;
            double dp = last.p - first.p;

            if (dt < 0.25 || dp < 1e-6)
            {
                return;
            }

            double framesDone = dp * this._totalFrameCount;
            double fps = framesDone / dt;

            if (fps <= 0 || double.IsNaN(fps) || double.IsInfinity(fps))
            {
                return;
            }

            // Glätten (EMA, recht schnell reagieren)
            if (this._estimatedFramesPerSec <= 0.01)
            {
                this._estimatedFramesPerSec = fps;
            }
            else
            {
                this._estimatedFramesPerSec = (this._estimatedFramesPerSec * 0.65) + (fps * 0.35);
            }
        }

        private void button_codecInfo_Click(object sender, EventArgs e)
        {
            var preset = (NvencVideoRenderer.PresetEntry?) this.comboBox_preset.SelectedItem;
            if (preset == null)
            {
                MessageBox.Show("No preset selected.", "Preset Info", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            string presetNameUpper = preset.DisplayName.ToUpperInvariant();
            if (NvencVideoRenderer.TryGetPresetDescription(presetNameUpper, out var desc))
            {
                if (desc == null)
                {
                    return;
                }

                string infoText = desc.HardwareTech + Environment.NewLine + Environment.NewLine + Environment.NewLine +
                    "Speed-Score:   " + desc.SpeedScore + Environment.NewLine +
                    "Quality-Score: " + desc.QualityScore + Environment.NewLine + Environment.NewLine + Environment.NewLine +
                    "     ( higher is better )     ";

                MessageBox.Show(infoText, "Preset Info: '" + presetNameUpper + "'", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
    }
}
