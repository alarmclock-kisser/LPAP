using LPAP.Audio;
using LPAP.Cuda;
using LPAP.Forms.Views;
using ManagedCuda.VectorTypes;
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
        private Dictionary<string, Control>? KernelArgumentControls = null;
        internal Dictionary<string, object>? KernelArgumentValues
        {
            get
            {
                if (this.KernelArgumentControls == null || this.KernelArgumentControls.Count == 0)
                {
                    return null;
                }

                var defs = this.KernelArgumentDefinitions ?? new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
                var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

                foreach (var (name, ctrl) in this.KernelArgumentControls)
                {
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }

                    if (!defs.TryGetValue(name, out var targetType) || targetType == null)
                    {
                        // Fallback to string
                        targetType = typeof(string);
                    }

                    // Skip device pointer args (are set internally by launcher)
                    if (targetType.IsPointer)
                    {
                        continue;
                    }

                    object? value = ExtractKernelArgControlValue(ctrl, targetType);
                    if (value != null)
                    {
                        result[name] = value;
                    }
                }

                return result;
            }
        }
        internal double? StretchFactor => this.KernelArgumentDefinitions != null && this.KernelArgumentDefinitions.Keys.Any(k => k.Contains("fac", StringComparison.OrdinalIgnoreCase))
            ? this.KernelArgumentValues != null && this.KernelArgumentValues.Keys.Any(k => k.Contains("fac", StringComparison.OrdinalIgnoreCase))
                ? Convert.ToDouble(this.KernelArgumentValues.First(kv => kv.Key.Contains("fac", StringComparison.OrdinalIgnoreCase)).Value, System.Globalization.CultureInfo.InvariantCulture)
                : null : null;

		private static object? ExtractKernelArgControlValue(Control ctrl, Type targetType)
        {
            var effectiveType = Nullable.GetUnderlyingType(targetType) ?? targetType;

            if (ctrl is NumericUpDown nud)
            {
                decimal d = nud.Value;

                if (effectiveType == typeof(int))
				{
					return (int)d;
				}

				if (effectiveType == typeof(long))
				{
					return (long)d;
				}

				if (effectiveType == typeof(float))
				{
					return (float)d;
				}

				if (effectiveType == typeof(double))
				{
					return (double)d;
				}

				if (effectiveType == typeof(decimal))
				{
					return d;
				}

				try { return Convert.ChangeType(d, effectiveType, System.Globalization.CultureInfo.InvariantCulture); } catch { return null; }
            }

            if (ctrl is CheckBox cb)
            {
                if (effectiveType == typeof(bool))
				{
					return cb.Checked;
				}

				return cb.Checked ? 1 : 0;
            }

            if (ctrl is TextBox tb)
            {
                if (effectiveType == typeof(string))
				{
					return tb.Text;
				}

				if (string.IsNullOrWhiteSpace(tb.Text) && Nullable.GetUnderlyingType(targetType) != null)
				{
					return null;
				}

				try { return Convert.ChangeType(tb.Text, effectiveType, System.Globalization.CultureInfo.InvariantCulture); } catch { return tb.Text; }
            }

            if (ctrl is ComboBox combo)
            {
                string? s = combo.SelectedItem?.ToString();
                if (effectiveType.IsEnum && !string.IsNullOrWhiteSpace(s))
                {
                    try { return Enum.Parse(effectiveType, s, ignoreCase: true); } catch { }
                }
                return s ?? string.Empty;
            }

            return null;
        }

        public Boolean FftRequired { get; private set; }
        internal int ChunkSize => (int) this.numericUpDown_chunkSize.Value;
        internal float Overlap => (float) this.numericUpDown_overlap.Value;


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
                    this.label_cudaWatts.Text = "Power: " + this.Cuda.GetPowerUsageInWatts().ToString("F1") + " W";
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


        private Task BuildCudaKernelArgsAsync()
        {
            this.panel_cudaKernelArguments.SuspendLayout();
            try
            {
                // Ensure panel supports scrolling
                this.panel_cudaKernelArguments.AutoScroll = true;
                this.KernelArgumentControls = [];
                this.panel_cudaKernelArguments.Controls.Clear();

                if (!this.Cuda.Initialized || string.IsNullOrWhiteSpace(this.SelectedKernelName))
                {
                    return Task.CompletedTask;
                }

                var argDefs = this.KernelArgumentDefinitions;
                if (argDefs == null || argDefs.Count == 0)
                {
                    return Task.CompletedTask;
                }

                int yOffset = 5;

                foreach (var (argName, argType) in argDefs)
                {
                    // label
                    var lbl = new Label
                    {
                        Text = $"{argName} ({argType.Name})",
                        Location = new Point(5, yOffset),
                        AutoSize = true
                    };

                    // input control
                    Control input;

                    // Device pointers: show disabled field (informational only)
                    if (argType.IsPointer)
                    {
                        var tb = new TextBox
                        {
                            Location = new Point(170, yOffset - 2),
                            Size = new Size(120, 23),
                            ReadOnly = true,
                            Text = "auto",
                            Enabled = false
                        };
                        input = tb;
                    }
                    else if (argType == typeof(bool))
                    {
                        var cb = new CheckBox
                        {
                            Location = new Point(170, yOffset - 1),
                            Size = new Size(120, 23),
                            Checked = false
                        };
                        input = cb;
                    }
                    else
                    {
                        // NumericUpDown for scalar numeric types
                        var nud = new NumericUpDown
                        {
                            Location = new Point(170, yOffset - 2),
                            Size = new Size(120, 23),
                            DecimalPlaces = (argType == typeof(double) || argType == typeof(decimal)) ? 12 : argType == typeof(float) ? 6 : 0,

                            Minimum = -1000000,
                            Maximum = 1000000,
                            Increment = (argType == typeof(float) || argType == typeof(double) || argType == typeof(decimal)) ? 0.1m : 1m
                        };
                        // set default value safely
                        try
                        {
                            var defObj = this.Cuda.GetDefaultArgValue(argType, argName, WindowMain.LastSelectedTrackView?.Audio);
                            decimal defVal = 0m;
                            if (defObj is decimal dm)
                            {
                                defVal = dm;
                            }
                            else if (defObj != null)
                            {
                                defVal = Convert.ToDecimal(defObj, System.Globalization.CultureInfo.InvariantCulture);
                            }

                            if (defVal < nud.Minimum)
                            {
                                defVal = nud.Minimum;
                            }

                            if (defVal > nud.Maximum)
                            {
                                defVal = nud.Maximum;
                            }

                            nud.Value = defVal;
                        }
                        catch { /* ignore */ }
                        if ((argName.Contains("size", StringComparison.OrdinalIgnoreCase) &! argName.Contains("overlap", StringComparison.OrdinalIgnoreCase)) || argName.Contains("length", StringComparison.OrdinalIgnoreCase))
                        {
                            nud.Value = this.ChunkSize;
							nud.ValueChanged += (s, e) =>
                            {
                                int sizeVal = 0;
                                if (argType == typeof(int))
                                {
                                    sizeVal = (int)nud.Value;
                                }
                                else if (argType == typeof(long))
                                {
                                    sizeVal = (int)(long)nud.Value;
                                }
                                this.numericUpDown_chunkSize.Value = Math.Clamp(sizeVal, this.numericUpDown_chunkSize.Minimum, this.numericUpDown_chunkSize.Maximum);
							};
						}
                        else if (argName.Contains("overlap", StringComparison.OrdinalIgnoreCase))
                        {
                            nud.Value = (decimal) this.Overlap;
							if (argType == typeof(float) || argType == typeof(double) || argType == typeof(decimal))
                            {
                                nud.ValueChanged += (s, e) =>
                                {
                                    float overlapVal = 0f;
                                    if (argType == typeof(float))
                                    {
                                        overlapVal = (float)nud.Value;
                                    }
                                    else if (argType == typeof(double))
                                    {
                                        overlapVal = (float)(double)nud.Value;
                                    }
                                    else if (argType == typeof(decimal))
                                    {
                                        overlapVal = (float)(decimal)nud.Value;
                                    }
                                    this.numericUpDown_overlap.Value = Math.Clamp((decimal)overlapVal, this.numericUpDown_overlap.Minimum, this.numericUpDown_overlap.Maximum);
								};
							}
                            else if (argType == typeof(int) || argType == typeof(long))
                            {
                                nud.ValueChanged += (s, e) =>
                                {
                                    int overlapVal = 0;
                                    if (argType == typeof(int))
                                    {
                                        overlapVal = (int)nud.Value;
                                    }
                                    else if (argType == typeof(long))
                                    {
                                        overlapVal = (int)(long)nud.Value;
                                    }
                                    this.numericUpDown_overlap.Value = (decimal)overlapVal / this.ChunkSize;
                                };
							}
						}

						input = nud;
                    }

                    // tag the control with (argName,argType) for later extraction
                    input.Tag = (argName, argType);

                    this.panel_cudaKernelArguments.Controls.Add(lbl);
                    this.panel_cudaKernelArguments.Controls.Add(input);

                    this.KernelArgumentControls[argName] = input;

                    yOffset += 28;
                }

                // Update scrollable area and adjust widths if vertical scrollbar appears
                int contentHeight = yOffset + 5;
                this.panel_cudaKernelArguments.AutoScrollMinSize = new Size(0, contentHeight);

                bool needVScroll = contentHeight > this.panel_cudaKernelArguments.ClientSize.Height;
                if (needVScroll)
                {
                    int sbw = System.Windows.Forms.SystemInformation.VerticalScrollBarWidth;
                    foreach (var item in this.panel_cudaKernelArguments.Controls.OfType<NumericUpDown>())
                    {
                        item.Width = Math.Max(20, item.Width - sbw);
                    }
                    foreach (var item in this.panel_cudaKernelArguments.Controls.OfType<TextBox>())
                    {
                        item.Width = Math.Max(20, item.Width - sbw);
                    }
                    foreach (var item in this.panel_cudaKernelArguments.Controls.OfType<CheckBox>())
                    {
                        item.Width = Math.Max(20, item.Width - sbw);
                    }
                }

                return Task.CompletedTask;
            }
            finally
            {
                this.panel_cudaKernelArguments.ResumeLayout();
                this.panel_cudaKernelArguments.PerformLayout();
            }
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
            bool shiftFlag = (ModifierKeys & Keys.Shift) == Keys.Shift;
            if (ctrlFlag)
            {
                if (shiftFlag)
                {
                    var rslt = MessageBox.Show("This will clear & reset the local statistics file. Continue?", "Clear Local Stats", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                    if (rslt == DialogResult.Yes)
                    {
                        string fp = NvencVideoRenderer.Reset_LocalStats_File();
                        NvencVideoRenderer.WriteHardwareInfo_To_LocalStats();
                        CudaLog.Info("Local statistics file reset: " + fp, null, "UI");
                    }

                    return;
                }

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
            try
            {
                // clear cache
                this.KernelArgumentControls?.Clear();
                this.panel_cudaKernelArguments.Controls.Clear();

                if (!this.Cuda.Initialized)
                {
                    CudaLog.Warn("CUDA not initialized (select a device + Initialize).");
                    return;
                }

                if (string.IsNullOrWhiteSpace(this.SelectedKernelName))
                {
                    return;
                }

                // Build UI for args (on UI thread)
                await this.BuildCudaKernelArgsAsync().ConfigureAwait(true);
                this.FftRequired = this.KernelArgumentDefinitions?.Values.Where(t => t.IsPointer).FirstOrDefault() == typeof(float2*);

                this.label_kernelType.Text = "Kernel Type: " + this.Cuda.GetKernelExecutionType(this.SelectedKernelName);
                this.label_fftRequired.Text = this.FftRequired ? "FFT Required: Yes" : "FFT Required: No";
                this.label_fftRequired.ForeColor = this.FftRequired ? System.Drawing.Color.DarkGreen : System.Drawing.Color.Gray;
            }
            catch (Exception ex)
            {
                CudaLog.Error(ex, "Failed to rebuild CUDA kernel argument UI.");
            }
        }

        private async void button_cudaExecute_Click(object sender, EventArgs e)
        {
            if (!this.Cuda.Initialized)
            {
                MessageBox.Show("CUDA not initialized.", "CUDA", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            bool ctrlFlag = (ModifierKeys & Keys.Control) == Keys.Control;
            bool shiftFlag = (ModifierKeys & Keys.Shift) == Keys.Shift;
            bool altFlag = (ModifierKeys & Keys.Alt) == Keys.Alt;
            if (altFlag)
            {
                this.AudioC.Clear();
            }

            string kernelName = this.SelectedKernelName ?? string.Empty;
            if (string.IsNullOrWhiteSpace(kernelName))
            {
                MessageBox.Show("No kernel selected.", "CUDA", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // --- Try get current audio (reflection fallback) ---
            List<AudioObj> audios = WindowMain.SelectedAudios.ToList();
            if (audios.Count < 1)
            {
                MessageBox.Show("No audio loaded/selected.", "CUDA", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Determine suitable presets count (for timestretch07 only)
            var suitablePresets = altFlag
                ? CudaKernelPresets.GetAllPresets().Where(p => p.IsSuitableForKernel(kernelName)).ToList()
                : new List<CudaKernelPresets.CudaKernelPreset>(); // none if not ALT

            int totalTracks = audios.Count;
            int presetsPerTrack = altFlag ? Math.Max(1, suitablePresets.Count) : 1;
            int totalComb = totalTracks * presetsPerTrack;

            IProgress<double> progress = ProgressAdapters.ToProgressBar(this.progressBar_cudaKernel, this.progressBar_cudaKernel.Maximum);
            int currentComb = 0;
            this.label_kernelProgress.Text = $"{currentComb + 1} / {presetsPerTrack} / {totalComb}";
            this.button_cudaExecute.Enabled = false;

            foreach (AudioObj audio in audios)
            {
                if (!altFlag)
                {
                    // Single run with current UI args
                    var res = await this.Cuda.ExecuteAudioKernelAutoAsync(
                        audio,
                        kernelName,
                        this.ChunkSize,
                        this.Overlap,
                        this.StretchFactor,
                        this.KernelArgumentValues,
                        (ctrlFlag || altFlag),
                        progress);

                    if (ctrlFlag && res != null)
                    {
                        audio.CopyAudioObj(res);
                    }

                    // advance overall combo counter
                    currentComb++;
                    this.label_kernelProgress.Text = $"{currentComb} / {presetsPerTrack} / {totalComb}";
                }
                else
                {
                    // ALT pressed: iterate all suitable v07 presets and execute each
                    if (suitablePresets.Count == 0)
                    {
                        // Fallback: run once with current args
                        var res = await this.Cuda.ExecuteAudioKernelAutoAsync(
                            audio,
                            kernelName,
                            this.ChunkSize,
                            this.Overlap,
                            this.StretchFactor,
                            this.KernelArgumentValues,
                            true,
                            progress);
                        if (res != null)
                        {
                            res.Name = audio.Name + " [Default]";
                            this.AudioC.Add(res);
                        }

                        this.progressBar_cudaKernel.Value = 0;
                        currentComb++;
                        this.label_kernelProgress.Text = $"{currentComb} / {presetsPerTrack} / {totalComb}";
                    }
                    else
                    {
                        foreach (var preset in suitablePresets)
                        {
                            // Merge current args with preset args (preset overwrites where defined)
                            var mergedArgs = ApplyPresetToArgs(this.KernelArgumentValues, preset);

                            // Determine chunkSize/overlap from merged (if provided), else UI values
                            int chunkSize = this.ChunkSize;
                            float overlap = this.Overlap;
                            if (mergedArgs.TryGetValue("chunkSize", out var csObj))
                            {
                                try { chunkSize = Convert.ToInt32(csObj, System.Globalization.CultureInfo.InvariantCulture); } catch { }
                            }
                            if (mergedArgs.TryGetValue("overlap", out var ovObj))
                            {
                                try { overlap = Convert.ToSingle(ovObj, System.Globalization.CultureInfo.InvariantCulture); } catch { }
                            }
                            double? factor = this.StretchFactor;
                            if (mergedArgs.TryGetValue("factor", out var fObj))
                            {
                                try { factor = Convert.ToDouble(fObj, System.Globalization.CultureInfo.InvariantCulture); } catch { }
                            }

                            // Update progress label before each preset execution
                            this.label_kernelProgress.Text = $"{currentComb + 1} / {presetsPerTrack} / {totalComb}";
                            CudaLog.Info($"Executing kernel '{kernelName}' on audio '{audio.Name}' with preset '{preset.Name}' (chunkSize={chunkSize}, overlap={overlap}, factor={factor})", null, "UI");

							var res = await this.Cuda.ExecuteAudioKernelAutoAsync(
                                audio,
                                kernelName,
                                chunkSize,
                                overlap,
                                factor,
                                mergedArgs,
                                true,
                                progress);

                            if (res != null)
                            {
                                // Name result by preset name
                                res.Name = audio.Name + " [" + preset.Name + "]";
                                this.AudioC.Add(res);
                            }

                            this.progressBar_cudaKernel.Value = 0;
                            currentComb++;
                            this.label_kernelProgress.Text = $"{currentComb} / {presetsPerTrack} / {totalComb}";
                        }
                    }
                }
            }

            if (altFlag)
            {
                var acv = new AudioCollectionView(this.AudioC.Items);
            }

            this.button_cudaExecute.Enabled = true;
            this.progressBar_cudaKernel.Value = 0;
            WindowMain.UpdateAllCollectionViews();
            WindowMain.UpdateTrackDependentUi();
        }

		private async void button_cufft_Click(object sender, EventArgs e)
		{
            if (!this.Cuda.Initialized)
            {
                MessageBox.Show("CUDA not initialized.", "CUFFT", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
			}

			// CtrlFlag for serial execution instead of asMany
			bool ctrlFlag = (ModifierKeys & Keys.Control) == Keys.Control;

			var audios = WindowMain.SelectedAudios.ToList();
            if (audios.Count < 1)
            {
                MessageBox.Show("No audio loaded/selected.", "CUFFT", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
			}

            IProgress<double> progress = ProgressAdapters.ToProgressBar(this.progressBar_cudaKernel, this.progressBar_cudaKernel.Maximum);
            this.button_cufft.Enabled = false;

			foreach (var audio in audios)
            {
                var res = await this.Cuda.ExecuteCufftAsync(audio, this.ChunkSize, this.Overlap, !ctrlFlag, progress);
                audio.Pointer = res ?? IntPtr.Zero;
			}

            this.button_cufft.Enabled = true;
			this.progressBar_cudaKernel.Value = 0;
			WindowMain.UpdateAllCollectionViews();
            WindowMain.UpdateTrackDependentUi((audios.Count == 1 ? audios.First() : null));
		}



		private void listBox_cudaLog_RightClick(object sender, MouseEventArgs e)
        {
            // Nur auf Rechtsklick reagieren
            if (e.Button != MouseButtons.Right)
            {
                return;
            }

            // Optional: Item unter Maus selektieren, wenn nicht bereits ausgewählt
            int index = this.listBox_cudaLog.IndexFromPoint(e.Location);
            if (index >= 0 && !this.listBox_cudaLog.SelectedIndices.Contains(index))
            {
                this.listBox_cudaLog.SelectedIndex = index;
            }

            var selected = this.listBox_cudaLog.SelectedItems.Cast<string>();
            if (!selected.Any())
            {
                return;
            }

            string text = string.Join(Environment.NewLine, selected);
            try
            {
                Clipboard.SetText(text);
                CudaLog.Info(selected.Count() + " Log-Entries copied to clipboard.", null, "UI");
            }
            catch
            {
                // Clipboard kann fehlschlagen (z. B. kein STA), still halten
            }
        }

		private void listBox_cudaLog_MouseDoubleClick(object sender, MouseEventArgs e)
		{
			if (!ModifierKeys.HasFlag(Keys.Control))
			{
				return;
			}

			var items = this.listBox_cudaLog.Items.Cast<string>().ToArray();
			string text = string.Join(Environment.NewLine, items);

			try
			{
				Clipboard.SetText(text);
			}
			catch (Exception ex)
			{
				CudaLog.Warn(ex.ToString());
			}
		}

		private void numericUpDown_chunkSize_ValueChanged(object sender, EventArgs e)
		{
            NumericUpDown? argNum = this.KernelArgumentControls?
                .Where(kvp => kvp.Value is NumericUpDown)
                .Select(kvp => (kvp.Key, Control: kvp.Value as NumericUpDown))
                .FirstOrDefault(t => (t.Key.Contains("size", StringComparison.OrdinalIgnoreCase) &! t.Key.Contains("overlap", StringComparison.OrdinalIgnoreCase)) || t.Key.Contains("length", StringComparison.OrdinalIgnoreCase)).Control as NumericUpDown;

			if (argNum != null)
            {
                argNum.Value = this.ChunkSize;
			}
		}

		private void numericUpDown_overlap_ValueChanged(object sender, EventArgs e)
		{
            NumericUpDown? argNum = this.KernelArgumentControls?
                .Where(kvp => kvp.Value is NumericUpDown)
                .Select(kvp => (kvp.Key, Control: kvp.Value as NumericUpDown))
                .FirstOrDefault(t => t.Key.Equals("overlap", StringComparison.OrdinalIgnoreCase)).Control as NumericUpDown;
            Type? argType = this.KernelArgumentDefinitions != null && this.KernelArgumentDefinitions.ContainsKey("overlap")
                ? this.KernelArgumentDefinitions["overlap"]
                : null;

            if (argNum != null && argType != null)
            {
                if (argType == typeof(float))
                {
                    argNum.Value = (decimal) (float) this.Overlap;
                }
                else if (argType == typeof(double))
                {
                    argNum.Value = (decimal) (double) this.Overlap;
                }
                else if (argType == typeof(decimal))
                {
                    argNum.Value = (decimal) this.Overlap;
                }
                else if (argType == typeof(int) || argType == typeof(long))
                {
                    argNum.Value = (decimal)(float) (this.ChunkSize * this.Overlap);
                }
            }
		}


		// Apply preset over current args and return merged dictionary
		private static Dictionary<string, object> ApplyPresetToArgs(
			Dictionary<string, object>? currentArgs,
			CudaKernelPresets.CudaKernelPreset preset)
		{
			var merged = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

			if (currentArgs != null)
			{
				foreach (var kv in currentArgs)
				{
					merged[kv.Key] = kv.Value;
				}
			}

			foreach (var kv in preset.Arguments)
			{
				merged[kv.Key] = kv.Value;
			}

			return merged;
		}

	}
}
