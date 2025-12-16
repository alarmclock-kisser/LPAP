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
using System.Threading.Channels;
using System.Windows.Forms;
using static LPAP.Audio.Processing.AudioProcessor;
using static LPAP.Cuda.NvencVideoRenderer;
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

        internal static Color PreviewBackColor = Color.White;



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
            this.Text = "Visualizer Renderer - '" + audio.Name + "'";
            PreviewBackColor = this.button_backColor.BackColor;
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
            this.comboBox_codecPreset.DataSource = NvencVideoRenderer.GetAllPresetEntries(!string.IsNullOrEmpty(WindowMain.CudaDevice));
            this.comboBox_codecPreset.DisplayMember = nameof(NvencVideoRenderer.PresetEntry.DisplayName);
            this.comboBox_codecPreset.ValueMember = nameof(NvencVideoRenderer.PresetEntry.Options);

            // Visualizer selectors
            this.comboBox_mode.DataSource = AudioProcessor.GetVisualizerModes();
            this.comboBox_visPreset.DataSource = AudioProcessor.GetVisualizerPresets();

            // IMPORTANT: allow "nothing selected" so old call can be used
            this.comboBox_mode.SelectedIndex = -1;
            this.comboBox_visPreset.SelectedIndex = -1;

            // Keep your placeholder text from Designer (Select Mode. / Select Preset.)
            this.comboBox_mode.Text = "Select Mode.";
            this.comboBox_visPreset.Text = "Select Preset.";


            this.numericUpDown_threads.Maximum = Environment.ProcessorCount;
            this.numericUpDown_threads.Value = this.numericUpDown_threads.Maximum - 1;

            this.numericUpDown_frameRate.Value = 60;

            Size currentRes = WindowsScreenHelper.GetScreenSize(this) ?? new Size(1024, 1024);
            this.comboBox_resolution.Items.Clear();
            this.comboBox_resolution.Items.Add("Current: " + $"{currentRes.Width}x{currentRes.Height}");
            this.comboBox_resolution.Items.AddRange(NvencVideoRenderer.CommonResolutions.ToArray());
            if (this.comboBox_resolution.Items.Contains("1920x1080"))
            {
                this.comboBox_resolution.SelectedIndex = this.comboBox_resolution.Items.IndexOf("1920x1080");
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

            this.label_time.Text = $"Elapsed: {left} / ~ {right}";

            if (this._estimatedFramesPerSec > 0.01)
            {
                this.label_framesPerSec.Text = "~ " + this._estimatedFramesPerSec.ToString("F2") + " fps";
            }
            else
            {
                this.label_framesPerSec.Text = "~ 0.00 fps";
            }

            this.label_percentage.Text = (this._lastProgress01 * 100).ToString("F1") + " %";
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
            bool success = false;
            double totalFramesRenderedMb = this.ApproxSizeInMb;

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

            var enc = ((NvencVideoRenderer.PresetEntry?) this.comboBox_codecPreset.SelectedItem)?.Options ?? NvencVideoRenderer.CpuPresets.X264_Default;
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

                var selectedPreset = this.comboBox_visPreset.SelectedItem is VisualizerPreset vp
                    ? vp
                    : (VisualizerPreset?) null;

                var selectedMode = this.comboBox_mode.SelectedItem is VisualizerMode vm
                    ? vm
                    : (VisualizerMode?) null;

                // UI amplification (percent -> factor)
                float uiAmp = (float) (this.numericUpDown_amplification.Value / 100.0m);

                // Build options from preset (if any)
                VisualizerOptions? visOpt = selectedPreset is { } p
                    ? AudioProcessor.GetOptionsForPreset(p)
                    : null;

                // helper: if only preset selected, choose a sensible default mode
                static VisualizerMode DefaultModeForPreset(VisualizerPreset? p) => p switch
                {
                    VisualizerPreset.Bars_Punchy => VisualizerMode.Bars,
                    VisualizerPreset.Spectrum_Smooth => VisualizerMode.SpectrumBars,
                    VisualizerPreset.Radial_Vaporwave => VisualizerMode.RadialWave,
                    VisualizerPreset.PeakMeter_Broadcast => VisualizerMode.PeakMeter,
                    VisualizerPreset.Waveform_Default => VisualizerMode.Waveform,
                    VisualizerPreset.Default => VisualizerMode.Waveform,
                    _ => VisualizerMode.Waveform
                };

                ChannelReader<AudioProcessor.FramePacket> reader;
                int frameCount;

                if (selectedPreset is null && selectedMode is null)
                {
                    // OLD overload
                    (reader, frameCount) = AudioProcessor.RenderVisualizerFramesBgraChannel(
                        audio,
                        width,
                        height,
                        frameRate,
                        amplification: uiAmp,
                        maxWorkers: this.MaxWorkers,
                        channelCapacity: 0,
                        progress: renderProgress,
                        ct: token);
                }
                else
                {
                    // NEW overload
                    var modeToUse = selectedMode ?? DefaultModeForPreset(selectedPreset);
                    var optToUse = (visOpt ?? new VisualizerOptions()) with { Amplification = uiAmp };

                    (reader, frameCount) = AudioProcessor.RenderVisualizerFramesBgraChannel(
                        audio,
                        width,
                        height,
                        mode: modeToUse,
                        opt: optToUse,
                        frameRate: frameRate,
                        maxWorkers: this.MaxWorkers,
                        channelCapacity: 0,
                        progress: renderProgress,
                        ct: token);
                }

                this._totalFrameCount = frameCount;

                string fileName = NvencVideoRenderer.SanitizeFileName(
                    mainAssemblyName + "_" + enc.VideoCodec.ToUpperInvariant() + "_" + this.Audio.Name +
                    (this.Audio.BeatsPerMinute > 0 ? " [" + this.Audio.BeatsPerMinute.ToString("F2") + "]" : "")
                );

                // --- 5) CONSUMER: NVENC ---
                outputPath = await NvencVideoRenderer.NvencRenderVideoAsync(
                    reader,
                    frameCount,
                    width,
                    height,
                    frameRate,
                    audio,
                    outputFilePath: Path.Combine(WindowMain.ExportDirectory, "NVEnc_Output", fileName),
                    options: enc,
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
                    success = !string.IsNullOrWhiteSpace(outputPath) && File.Exists(outputPath);
                    if (success && !string.IsNullOrEmpty(outputPath))
                    {
                        double avgFrameRate = this._totalFrameCount / sw.Elapsed.TotalSeconds;
                        var fi = new FileInfo(outputPath);
                        NvencVideoRenderer.WriteVideoRenderingResult_To_LocalStats(enc.VideoCodec, sw.Elapsed.TotalSeconds, avgFrameRate, this._totalFrameCount, totalFramesRenderedMb, fi.Length);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex);
                }

                try { this.progressBar_rendering.Invoke(() => this.progressBar_rendering.Value = 0); } catch { }
            }

            if (success)
            {
                this.DialogResult = DialogResult.OK;
                this.Close();
            }
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
            var preset = (NvencVideoRenderer.PresetEntry?) this.comboBox_codecPreset.SelectedItem;
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

        private void comboBox_resolution_Click(object sender, EventArgs e)
        {
            // If right and not left clicked, set to this screens values
            if ((e is MouseEventArgs args && args.Button == MouseButtons.Right) || ModifierKeys.HasFlag(Keys.Control))
            {
                this.numericUpDown_frameRate.Value = (decimal) (WindowsScreenHelper.GetScreenRefreshRate(this) ?? 24.00f);
                this.comboBox_resolution.SelectedIndex = Math.Clamp(0, 0, this.comboBox_resolution.Items.Count - 1);

                CudaLog.Info("Set resolution & frame rate to current screen values.", "", "Visualizer");
            }
        }

        private void comboBox_codecPreset_Click(object sender, EventArgs e)
        {
            bool isSpecialClick = (e is MouseEventArgs me && me.Button == MouseButtons.Right) || ModifierKeys.HasFlag(Keys.Control);

            if (!isSpecialClick)
            {
                return;
            }

            // --- Determine next state (tri-state cycle) ---
            string state = this.comboBox_codecPreset.Tag as string ?? "Default";
            string nextState = state switch
            {
                "BySpeedDesc" => "ByQualityDesc",
                "ByQualityDesc" => "Default",
                _ => "BySpeedDesc"
            };

            this.comboBox_codecPreset.Tag = nextState;

            // --- Extract items from ComboBox ---
            var items = new List<object>();
            foreach (var it in this.comboBox_codecPreset.Items)
            {
                items.Add(it);
            }

            if (items.Count == 0)
            {
                return;
            }

            // --- Helper to read scores dynamically ---
            static int GetScore(object item, string propName)
            {
                var prop = item.GetType().GetProperty(propName);
                if (prop == null)
                {
                    return 0;
                }

                var val = prop.GetValue(item);
                if (val is int i)
                {
                    return i;
                }

                return 0;
            }

            // --- Sort according to state ---
            switch (nextState)
            {
                case "BySpeedDesc":
                    items = items
                        .OrderByDescending(i => GetScore(i, "SpeedScore"))
                        .ThenBy(i => i.ToString())
                        .ToList();
                    break;

                case "ByQualityDesc":
                    items = items
                        .OrderByDescending(i => GetScore(i, "QualityScore"))
                        .ThenBy(i => i.ToString())
                        .ToList();
                    break;

                case "Default":
                default:
                    // Default = alphabetical by display text
                    items = items
                        .OrderBy(i => i.ToString())
                        .ToList();
                    break;
            }

            // --- Rebind ComboBox without flicker ---
            object? selected = this.comboBox_codecPreset.SelectedItem;

            this.comboBox_codecPreset.BeginUpdate();
            try
            {
                this.comboBox_codecPreset.DataSource = null;
                this.comboBox_codecPreset.Items.Clear();

                foreach (var it in items)
                {
                    this.comboBox_codecPreset.Items.Add(it);
                }

                // Restore selection if possible
                if (selected != null && items.Contains(selected))
                {
                    this.comboBox_codecPreset.SelectedItem = selected;
                }
            }
            finally
            {
                this.comboBox_codecPreset.EndUpdate();
                CudaLog.Info($"Sorted presets by '{nextState}'.", "", "Visualizer");
            }
        }

        private void numericUpDown_volume_ValueChanged(object sender, EventArgs e)
        {
            if (this.numericUpDown_volume.Value == 100)
            {
                this.numericUpDown_volume.ForeColor = Color.Gray;
            }
            else
            {
                this.numericUpDown_volume.ForeColor = SystemColors.ControlText;
            }
        }

        private async void button_preview_Click(object sender, EventArgs e)
        {
            bool ctrlFlag = ModifierKeys.HasFlag(Keys.Control);

            double selectionEndTime = (double) Math.Max(this.numericUpDown_startSeconds.Value + this.numericUpDown_previewDuration.Value, this.numericUpDown_endSeconds.Value);
            long selectionStart = (long) (this.numericUpDown_startSeconds.Value * this.Audio.SampleRate);
            long selectionEnd = (long) (selectionEndTime * this.Audio.SampleRate);

            AudioObj selectionObj = await this.Audio.CopyFromSelectionAsync(selectionStart, selectionEnd);

            Size resolution = this.ParsedResolution;
            double frameRate = (double) this.numericUpDown_frameRate.Value;
            NvencOptions enc = ((NvencVideoRenderer.PresetEntry?) this.comboBox_codecPreset.SelectedItem)?.Options ?? NvencVideoRenderer.CpuPresets.X264_Default;
            VisualizerOptions? opts = this.comboBox_visPreset.SelectedItem is VisualizerPreset preset ? AudioProcessor.GetOptionsForPreset(preset) : null;
            VisualizerMode? mode = (VisualizerMode?) this.comboBox_mode.SelectedItem;
            float amp = (float) (this.numericUpDown_amplification.Value / 100.0m);

            if (ctrlFlag)
            {
                string paramsInfo = $"Res: {resolution.Width}x{resolution.Height}, FR: {frameRate:F2} fps, Preset: {enc.VideoCodec}, {enc.Preset}, VisualizerOpts: {(opts != null ? "Custom" : "Null")}, Mode: {(mode != null ? mode.ToString() : "Null")}";
                MessageBox.Show(paramsInfo, "Preview Parameters", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }

            if (this.numericUpDown_volume.Value != 100)
            {
                await selectionObj.NormalizeAsync((float)(this.numericUpDown_volume.Value / 100.0m)).ConfigureAwait(true);
            }

            var dlg = new VisualizerDialogPreview(selectionObj, resolution, frameRate, amp, enc, opts, mode);
            dlg.ShowDialog(this);
        }

        private void button_backColor_Click(object sender, EventArgs e)
        {
            // Color picker
            var colorDialog = new ColorDialog
            {
                AllowFullOpen = true,
                AnyColor = true,
                FullOpen = true,
                Color = PreviewBackColor
            };
            if (colorDialog.ShowDialog(this) == DialogResult.OK)
            {
                PreviewBackColor = colorDialog.Color;
                this.button_backColor.BackColor = PreviewBackColor;
                if (PreviewBackColor.GetBrightness() < 0.6f)
                {
                    this.button_backColor.ForeColor = Color.White;
                }
                else
                {
                    this.button_backColor.ForeColor = Color.Black;
                }
            }
        }
    }
}
