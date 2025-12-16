using LPAP.Audio;
using LPAP.Cuda;
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
				if (this.KernelArgumentControls == null)
				{
					return null;
				}

				return this.KernelArgumentControls.Select(kvp => new KeyValuePair<string, object>(kvp.Key, kvp.Value)).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
			}
		}

		public Boolean FftRequired { get; private set; }

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

                            if (defVal < nud.Minimum) defVal = nud.Minimum;
                            if (defVal > nud.Maximum) defVal = nud.Maximum;
                            nud.Value = defVal;
                        }
                        catch { /* ignore */ }
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

			string kernelName = this.SelectedKernelName ?? string.Empty;
            if (string.IsNullOrWhiteSpace(kernelName))
            {
                MessageBox.Show("No kernel selected.", "CUDA", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // --- Try get current audio (reflection fallback) ---
            static AudioObj? TryGetAudio(object form)
            {
                var t = form.GetType();

                // common property names
                foreach (var pn in new[] { "Audio", "CurrentAudio", "SelectedAudio", "AudioObj", "ActiveAudio" })
                {
                    var p = t.GetProperty(pn, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                    if (p != null && typeof(AudioObj).IsAssignableFrom(p.PropertyType))
                    {
                        if (p.GetValue(form) is AudioObj ao)
                        {
                            return ao;
                        }
                    }
                }

                // common field names
                foreach (var fn in new[] { "_audio", "audio", "Audio", "CurrentAudio", "SelectedAudio" })
                {
                    var f = t.GetField(fn, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                    if (f != null && typeof(AudioObj).IsAssignableFrom(f.FieldType))
                    {
                        if (f.GetValue(form) is AudioObj ao)
                        {
                            return ao;
                        }
                    }
                }

                return null;
            }

            var audio = TryGetAudio(this);
            if (audio is null || audio.Data is null || audio.Data.Length == 0)
            {
                MessageBox.Show("No audio loaded/selected.", "CUDA", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // --- Build argument dictionary from controls ---
            var args = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            // Fix for CS8602: check for null before dereferencing
            if (this.KernelArgumentControls != null)
            {
                foreach (var kvp in this.KernelArgumentControls)
                {
                    var argName = kvp.Key;
                    var control = kvp.Value;

                    if (control.Tag is ValueTuple<string, Type> meta)
                    {
                        var (_, argType) = meta;

                        // ignore pointer args in UI (CudaService auto-wires audio buffers)
                        if (argType.IsPointer)
                        {
                            continue;
                        }

                        object? valueObj = null;

                        try
                        {
                            if (control is CheckBox cb)
                            {
                                valueObj = cb.Checked;
                            }
                            else if (control is NumericUpDown nud)
                            {
                                // cast numeric according to expected type
                                if (argType == typeof(int))
                                {
                                    valueObj = (int)nud.Value;
                                }
                                else if (argType == typeof(long))
                                {
                                    valueObj = (long)nud.Value;
                                }
                                else if (argType == typeof(uint))
                                {
                                    valueObj = (uint)nud.Value;
                                }
                                else if (argType == typeof(ulong))
                                {
                                    valueObj = (ulong)nud.Value;
                                }
                                else if (argType == typeof(float))
                                {
                                    valueObj = (float)nud.Value;
                                }
                                else if (argType == typeof(double))
                                {
                                    valueObj = (double)nud.Value;
                                }
                                else if (argType == typeof(decimal))
                                {
                                    valueObj = nud.Value;
                                }
                                else
                                {
                                    // fallback: keep decimal
                                    valueObj = nud.Value;
                                }
                            }
                        }
                        catch
                        {
                            valueObj = null;
                        }

                        if (valueObj != null)
                        {
                            args[argName] = valueObj;
                        }
                    }
                }
            }

            // --- Wrapper params (optional via args) ---
            int chunkSize = 0;
            float overlap = 0.0f;

            // allow both "chunkSize" and "__chunkSize"
            if (args.TryGetValue("chunkSize", out var cs) || args.TryGetValue("__chunkSize", out cs))
            {
                try { chunkSize = Convert.ToInt32(cs); } catch { chunkSize = 0; }
                args.Remove("chunkSize"); args.Remove("__chunkSize");
            }

            if (args.TryGetValue("overlap", out var ov) || args.TryGetValue("__overlap", out ov))
            {
                try { overlap = Convert.ToSingle(ov); } catch { overlap = 0.0f; }
                args.Remove("overlap"); args.Remove("__overlap");
            }

            // clamp rules
            chunkSize = Math.Max(0, chunkSize);
            overlap = Math.Clamp(overlap, 0.0f, 0.95f);

            bool ctrl = ModifierKeys.HasFlag(Keys.Control);

            try
            {
                // Ctrl only: show argument summary (does not change execution mode)
                if (ctrl)
                {
                    var lines = new List<string>();
                    lines.Add($"Kernel: {kernelName}");
                    lines.Add($"chunkSize: {chunkSize}");
                    lines.Add($"overlap: {overlap}");
                    foreach (var kv in args)
                    {
                        lines.Add($"{kv.Key} = {kv.Value}");
                    }
                    MessageBox.Show(string.Join(Environment.NewLine, lines), "CUDA Args", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }

                // Determine execution type dynamically
                string kType = this.Cuda.GetKernelExecutionType(kernelName);

                // Helper to infer output element type (2nd pointer arg base type if present)
                static Type ResolveOutputElementType(Dictionary<string, Type>? defs)
                {
                    if (defs == null || defs.Count == 0)
                    {
                        return typeof(float);
                    }
                    var ptrs = defs.Where(kv => kv.Value.IsPointer).Select(kv => kv.Value.GetElementType()).Where(t => t != null).Cast<Type>().ToList();
                    if (ptrs.Count >= 2)
                    {
                        return ptrs[1];
                    }
                    if (ptrs.Count == 1)
                    {
                        return ptrs[0];
                    }
                    return typeof(float);
                }

                var outElemType = ResolveOutputElementType(this.KernelArgumentDefinitions);

                switch (kType.ToLowerInvariant())
                {
                    case "in-place":
                        await this.Cuda.ExecuteAudioKernelInPlaceAsync(
                            audio, kernelName, chunkSize: chunkSize, overlap: overlap, arguments: args).ConfigureAwait(true);
                        CudaLog.Info("InPlace kernel execution finished.", kernelName);
                        break;

                    case "out-of-place":
                        {
                            var outAudio = await this.Cuda.ExecuteAudioKernelOutOfPlaceAsync(
                                audio, kernelName, chunkSize: chunkSize, overlap: overlap, arguments: args).ConfigureAwait(true);
                            if (outAudio == null)
                            {
                                MessageBox.Show("Kernel execution failed (no output).", "CUDA", MessageBoxButtons.OK, MessageBoxIcon.Error);
                                return;
                            }
                            if (this.checkBox_autoApply.Checked)
                            {
                                try
                                {
                                    audio.CopyAudioObj(outAudio);
                                    CudaLog.Info("Applied OutOfPlace result back to current audio.", kernelName);
                                }
                                catch (Exception ex)
                                {
                                    CudaLog.Error(ex, "Failed to apply OutOfPlace result back to current audio.");
                                }
                            }
                            CudaLog.Info("OutOfPlace kernel execution finished.", kernelName);
                        }
                        break;

                    case "getvalue":
                        {
                            var mi = typeof(CudaService).GetMethod(nameof(CudaService.ExecuteAudioKernelGetValueAsync))!;
                            var gmi = mi.MakeGenericMethod(outElemType);
                            var taskObj = (Task)gmi.Invoke(this.Cuda, [audio, kernelName, chunkSize, overlap, args, CancellationToken.None])!;
                            await taskObj.ConfigureAwait(true);
                            var resultProp = taskObj.GetType().GetProperty("Result");
                            var value = resultProp?.GetValue(taskObj);
                            MessageBox.Show($"Kernel '{kernelName}' returned: {value ?? "<null>"}", $"CUDA GetValue<{outElemType.Name}>", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }
                        break;

                    case "getdata":
                        {
                            var mi = typeof(CudaService).GetMethod(nameof(CudaService.ExecuteAudioKernelGetDataAsync))!;
                            var gmi = mi.MakeGenericMethod(outElemType);
                            var taskObj = (Task)gmi.Invoke(this.Cuda, new object?[] { audio, kernelName, chunkSize, overlap, args, CancellationToken.None })!;
                            await taskObj.ConfigureAwait(true);
                            var resultProp = taskObj.GetType().GetProperty("Result");
                            var data = resultProp?.GetValue(taskObj) as Array;
                            if (data == null || data.Length == 0)
                            {
                                MessageBox.Show("Kernel returned no data.", $"CUDA GetData<{outElemType.Name}>", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                                return;
                            }
                            MessageBox.Show($"Kernel returned {data.Length} elements of {outElemType.Name}.", $"CUDA GetData<{outElemType.Name}>", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }
                        break;

                    default:
                        // Fallback: InPlace
                        await this.Cuda.ExecuteAudioKernelInPlaceAsync(
                            audio, kernelName, chunkSize: chunkSize, overlap: overlap, arguments: args).ConfigureAwait(true);
                        CudaLog.Info("InPlace kernel execution finished.", kernelName);
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                CudaLog.Info("Kernel execution canceled.", kernelName);
            }
            catch (Exception ex)
            {
                CudaLog.Error(ex, $"Kernel execution failed: {kernelName}");
                MessageBox.Show(ex.Message, "CUDA Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }



    }
}
