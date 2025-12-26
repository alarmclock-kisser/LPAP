using System;
using System.Windows.Forms;
using MathNet.Numerics;
using System.Threading;
using LPAP.Audio;
using LPAP.Forms.Views;
using LPAP.Audio.Processing;
using System.Reflection;
using Control = System.Windows.Forms.Control;
using LPAP.Cuda;

namespace LPAP.Forms.Dialogs
{
	public partial class TimeStretchDialog : Form
	{
		internal IEnumerable<AudioObj> Tracks;
		private readonly TrackView? trackView;

		internal Dictionary<MethodInfo, string> AvailableMethods = AudioProcessor.GetTimeStretchMethods_DisplayMap();
		internal MethodInfo? SelectedMethod => this.comboBox_versions.SelectedIndex >= 0 ? this.AvailableMethods.Keys.ElementAt(this.comboBox_versions.SelectedIndex) : null;
		internal Dictionary<string, object?> ExcludedParameters
		{
			get
			{
				// NOTE:
				// Auto-Chunking => chunkSize / overlap NICHT setzen,
				// damit die Default-Parameter der ausgewählten Methode greifen.
				var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
				{
					["obj"] = null,
					["keepData"] = false,
					["normalize"] = this.Normalize,
					["maxWorkers"] = this.MaxWorkers,
					["progress"] = this.Progress,
					["chunkSize"] = this.ChunkSize,
					["overlap"] = this.Overlap,
					["factor"] = this.StretchFactor,
					// CancellationToken nicht als UI-Input bauen; intern übergeben
					["ct"] = this.ProcessingCancellationSource?.Token ?? CancellationToken.None,
					["token"] = this.ProcessingCancellationSource?.Token ?? CancellationToken.None,
					["cancellationToken"] = this.ProcessingCancellationSource?.Token ?? CancellationToken.None
				};

				if (this.checkBox_autoChunking.Checked)
				{
					dict["chunkSize"] = null;
					dict["overlap"] = null;
				}

				return dict;
			}
		}

		internal Dictionary<string, Type> MethodParameters => this.SelectedMethod?.GetParameters().Where(p => !this.ExcludedParameters.ContainsKey(p.Name ?? "N/A")).ToDictionary(p => p.Name ?? string.Empty, p => p.ParameterType) ?? [];
		private Dictionary<string, Control> ParameterControls = [];
		internal Dictionary<string, object?> ParameterValues
		{
			get
			{
				var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

				if (this.ParameterControls == null || this.ParameterControls.Count == 0)
				{
					return result;
				}

				foreach (var kvp in this.ParameterControls)
				{
					string name = kvp.Key;
					Control ctrl = kvp.Value;

					if (string.IsNullOrWhiteSpace(name))
					{
						continue;
					}

					if (!this.MethodParameters.TryGetValue(name, out var paramType) || paramType == null)
					{
						continue;
					}

					object? value = GetControlValueTyped(ctrl, paramType);
					result[name] = value;
				}

				return result;
			}
		}




		private double StretchFactor => (double) this.numericUpDown_stretchFactor.Value;
		private int ChunkSize => this.checkBox_autoChunking.Checked ? 0 : (int) this.numericUpDown_chunkSize.Value;
		private float Overlap => this.checkBox_autoChunking.Checked ? 0f : (float) this.numericUpDown_overlap.Value;
		private float Normalize => (float) this.numericUpDown_normalize.Value;
		private int MaxWorkers => (int) this.numericUpDown_threads.Value;


		private bool isProcessing;
		private IProgress<double> Progress => new Progress<double>(percent =>
		{
			int scaled = (int) Math.Round(percent * this.progressBar_stretching.Maximum);
			this.progressBar_stretching.Value = Math.Clamp(scaled, this.progressBar_stretching.Minimum, this.progressBar_stretching.Maximum);
		});
		private CancellationTokenSource? ProcessingCancellationSource = null;

		private System.Windows.Forms.Timer? ProcessingTimer = null;
		private DateTime ProcessingStarted = DateTime.MinValue;
		private readonly Lock EtaGate = new();
		private double ProgressGlobal01 = 0.0;     // 0..1 overall
		private int ProcessingTimerIntervalMs = 100;
		private readonly Queue<(double tSec, double p01)> EtaSamples = new(); // recent window samples
		private double EtaSecondsRemainingSmoothed = -1.0;
		private const double EtaWindowSeconds = 6.0;
		private const double EtaRemainTauSeconds = 1.5;



		private static float LastTargetBpm = 120f;
		private static float LastInitialBpm = 120f;




		public TimeStretchDialog(TrackView? trackView = null, IEnumerable<AudioObj>? audios = null)
		{
			this.InitializeComponent();
			if (audios?.Count() > 0)
			{
				this.Tracks = audios;
			}
			else if (trackView != null)
			{
				this.trackView = trackView;
				this.Tracks = [trackView.Audio];
			}
			else
			{
				// Close if no valid input
				this.Tracks = [];
				this.Close();
			}

			this.Text = $"Time Stretch - {trackView?.Name ?? (this.Tracks.Count() > 1 ? this.Tracks.Count() + " Tracks" : this.Tracks.First().Name)}";
			this.StartPosition = FormStartPosition.Manual;
			this.Location = WindowsScreenHelper.GetCornerPosition(this, false, false);

			this.numericUpDown_chunkSize.Tag = (int) this.numericUpDown_chunkSize.Value;
			this.numericUpDown_initialBpm.Value = this.Tracks.First().BeatsPerMinute > 0 ? (decimal) this.Tracks.First().BeatsPerMinute : this.Tracks.First().ScannedBeatsPerMinute > 30 ? this.Tracks.First().ScannedBeatsPerMinute : (decimal) LastInitialBpm;
			this.numericUpDown_threads.Minimum = 1;
			this.numericUpDown_threads.Maximum = Math.Max(Environment.ProcessorCount, 1);
			this.numericUpDown_threads.Value = Math.Max(Environment.ProcessorCount - 1, 1);
			this.numericUpDown_targetBpm.Value = (decimal) LastTargetBpm;

			this.ComboBox_FillMethods();

			this.FormClosing += this.TimeStretchDialog_FormClosing;
		}


		// UI Building
		private void ComboBox_FillMethods()
		{
			this.comboBox_versions.SuspendLayout();
			this.comboBox_versions.Items.Clear();

			this.comboBox_versions.Items.AddRange(this.AvailableMethods.Values.ToArray());

			this.comboBox_versions.ResumeLayout();

			if (this.comboBox_versions.Items.Count > 0)
			{
				this.comboBox_versions.SelectedIndex = 0;
			}
		}

		private Task BuildArgumentsAsync(float inputWidthPart = 0.6f)
		{
			this.panel_parameters.SuspendLayout();
			try
			{
				this.ParameterControls = [];
				this.panel_parameters.Controls.Clear();

				if (this.SelectedMethod == null)
				{
					return Task.CompletedTask;
				}

				var argDefs = this.MethodParameters;
				if (argDefs == null || argDefs.Count == 0)
				{
					return Task.CompletedTask;
				}

				// Layout-Berechnung ohne Magic Numbers
				inputWidthPart = Math.Clamp(inputWidthPart, 0.1f, 0.95f);
				float labelWidthPart = 1.0f - inputWidthPart;

				int marginLeft = 8;
				int marginRight = 8;
				int marginTop = 5;
				int rowHeight = 28;
				int spacingX = 8;

				int contentWidth = Math.Max(0, this.panel_parameters.ClientSize.Width - (marginLeft + marginRight));
				int labelWidth = (int) Math.Round(contentWidth * labelWidthPart);
				int inputWidth = contentWidth - labelWidth;

				int yOffset = marginTop;

				foreach (var (argName, argType) in argDefs)
				{
					// Label
					var lbl = new Label
					{
						Text = $"{argName} ({argType.Name})",
						Location = new Point(marginLeft, yOffset),
						AutoSize = false,
						Size = new Size(labelWidth, 22)
					};

					// Input control
					Control input;
					if (argType.IsPointer)
					{
						input = new TextBox
						{
							ReadOnly = true,
							Text = "auto",
							Enabled = false
						};
					}
					else if (argType == typeof(bool))
					{
						input = new CheckBox();
					}
					else if (argType.IsEnum)
					{
						var combo = new ComboBox
						{
							DropDownStyle = ComboBoxStyle.DropDownList
						};
						combo.Items.AddRange(Enum.GetNames(argType));
						try
						{
							var defObj = this.SelectedMethod?.GetParameters().FirstOrDefault(p => p.Name?.Equals(argName, StringComparison.OrdinalIgnoreCase) == true)?.DefaultValue;
							if (defObj != null)
							{
								combo.SelectedItem = defObj.ToString();
							}
							else
							{
								combo.SelectedIndex = 0;
							}
						}
						catch
						{
							combo.SelectedIndex = 0;
						}
						input = combo;
					}
					else
					{
						var nud = new NumericUpDown
						{
							DecimalPlaces = (argType == typeof(double) || argType == typeof(decimal)) ? 12 : argType == typeof(float) ? 6 : 0,
							Minimum = -1000000,
							Maximum = 1000000,
							Increment = (argType == typeof(float) ? 0.1m : argType == typeof(double) || argType == typeof(decimal) ? 0.001m : 1m)
						};
						try
						{
							var defObj = this.SelectedMethod?.GetParameters().FirstOrDefault(p => p.Name?.Equals(argName, StringComparison.OrdinalIgnoreCase) == true)?.DefaultValue;
							decimal defVal = 0m;
							if (defObj is decimal dm)
							{
								defVal = dm;
							}
							else if (defObj != null)
							{
								defVal = Convert.ToDecimal(defObj, System.Globalization.CultureInfo.InvariantCulture);
							}

							defVal = Math.Clamp(defVal, nud.Minimum, nud.Maximum);
							nud.Value = defVal;
						}
						catch { }
						input = nud;
					}

					input.Tag = (argName, argType);
					input.Location = new Point(marginLeft + labelWidth + spacingX, yOffset - 2);
					input.Size = new Size(Math.Max(20, inputWidth - spacingX), 23);

					this.panel_parameters.Controls.Add(lbl);
					this.panel_parameters.Controls.Add(input);
					this.ParameterControls[argName] = input;

					yOffset += rowHeight;
				}

				int contentHeight = yOffset + marginTop;
				this.panel_parameters.AutoScrollMinSize = new Size(0, contentHeight);

				bool needVScroll = contentHeight > this.panel_parameters.ClientSize.Height;
				if (needVScroll)
				{
					int sbw = SystemInformation.VerticalScrollBarWidth;
					foreach (var item in this.panel_parameters.Controls.OfType<Control>())
					{
						if (item is Label)
						{
							continue;
						}

						item.Width = Math.Max(20, item.Width - sbw);
					}
				}

				return Task.CompletedTask;
			}
			finally
			{
				this.panel_parameters.ResumeLayout();
				this.panel_parameters.PerformLayout();
			}
		}

		private static object? GetControlValueTyped(Control ctrl, Type paramType)
		{
			// Handle Nullable<T>
			Type effectiveType = Nullable.GetUnderlyingType(paramType) ?? paramType;

			// Pointer / unsupported -> null (lets default apply if method has default value)
			if (effectiveType.IsPointer)
			{
				return null;
			}

			if (ctrl is CheckBox cb)
			{
				if (effectiveType == typeof(bool))
				{
					return cb.Checked;
				}

				// If someone mapped a non-bool to CheckBox, ignore
				return null;
			}

			if (ctrl is ComboBox combo)
			{
				// Enums: parse by selected item string
				if (effectiveType.IsEnum)
				{
					string? s = combo.SelectedItem?.ToString();
					if (string.IsNullOrWhiteSpace(s))
					{
						// If enum param is nullable, allow null; otherwise return first enum value
						if (Nullable.GetUnderlyingType(paramType) != null)
						{
							return null;
						}

						var vals = Enum.GetValues(effectiveType);
						return vals.Length > 0 ? vals.GetValue(0) : Activator.CreateInstance(effectiveType);
					}

					return Enum.Parse(effectiveType, s, ignoreCase: true);
				}

				// Non-enum combobox: return string
				return combo.SelectedItem?.ToString();
			}

			if (ctrl is NumericUpDown nud)
			{
				decimal d = nud.Value;

				if (effectiveType == typeof(int))
				{
					return (int) d;
				}

				if (effectiveType == typeof(float))
				{
					return (float) d;
				}

				if (effectiveType == typeof(double))
				{
					return (double) d;
				}

				if (effectiveType == typeof(decimal))
				{
					return d;
				}

				if (effectiveType == typeof(long))
				{
					return (long) d;
				}

				if (effectiveType == typeof(short))
				{
					return (short) d;
				}

				if (effectiveType == typeof(byte))
				{
					return (byte) d;
				}

				// Fallback: attempt change type from decimal
				try
				{
					return Convert.ChangeType(d, effectiveType, System.Globalization.CultureInfo.InvariantCulture);
				}
				catch
				{
					return null;
				}
			}

			if (ctrl is TextBox tb)
			{
				if (effectiveType == typeof(string))
				{
					return tb.Text;
				}

				// If nullable, allow empty => null
				if (Nullable.GetUnderlyingType(paramType) != null && string.IsNullOrWhiteSpace(tb.Text))
				{
					return null;
				}

				try
				{
					return Convert.ChangeType(tb.Text, effectiveType, System.Globalization.CultureInfo.InvariantCulture);
				}
				catch
				{
					return null;
				}
			}

			// Unknown control type
			return null;
		}

		private static string FormatTime(double seconds)
		{
			if (seconds < 0)
			{
				seconds = 0;
			}

			if (seconds < 60.0)
			{
				return $"{seconds:0.00}s";
			}

			int total = (int) Math.Floor(seconds);
			int m = total / 60;
			int s = total % 60;
			return $"{m}:{s:00}m";
		}



		// UI Event Handlers
		private async void comboBox_versions_SelectedIndexChanged(object sender, EventArgs e)
		{
			await this.BuildArgumentsAsync();
		}

		private void checkBox_autoChunking_CheckedChanged(object sender, EventArgs e)
		{
			this.numericUpDown_chunkSize.Enabled = !this.checkBox_autoChunking.Checked;
			this.numericUpDown_overlap.Enabled = !this.checkBox_autoChunking.Checked;
		}

		private void numericUpDown_chunkSize_ValueChanged(object sender, EventArgs e)
		{
			int prev = this.numericUpDown_chunkSize.Tag is int val ? val : 128;
			int curr = (int) this.numericUpDown_chunkSize.Value;

			if (curr > prev)
			{
				this.numericUpDown_chunkSize.Value = Math.Clamp(prev * 2, this.numericUpDown_chunkSize.Minimum, this.numericUpDown_chunkSize.Maximum);
			}
			else
			{
				this.numericUpDown_chunkSize.Value = Math.Clamp(prev / 2, this.numericUpDown_chunkSize.Minimum, this.numericUpDown_chunkSize.Maximum);
			}

			this.numericUpDown_chunkSize.Tag = (int) this.numericUpDown_chunkSize.Value;
		}

		private void numericUpDown_initialBpm_ValueChanged(object sender, EventArgs e)
		{
			double factor = (double) this.numericUpDown_initialBpm.Value / (double) this.numericUpDown_targetBpm.Value;
			this.numericUpDown_stretchFactor.Value = Math.Clamp((decimal) factor, this.numericUpDown_stretchFactor.Minimum, this.numericUpDown_stretchFactor.Maximum);
			LastInitialBpm = (float) this.numericUpDown_initialBpm.Value;
		}

		private void numericUpDown_targetBpm_ValueChanged(object sender, EventArgs e)
		{
			double factor = (double) this.numericUpDown_initialBpm.Value / (double) this.numericUpDown_targetBpm.Value;
			this.numericUpDown_stretchFactor.Value = Math.Clamp((decimal) factor, this.numericUpDown_stretchFactor.Minimum, this.numericUpDown_stretchFactor.Maximum);
			LastTargetBpm = (float) this.numericUpDown_targetBpm.Value;
		}

		private void numericUpDown_stretchFactor_ValueChanged(object sender, EventArgs e)
		{
			double targetBpm = (double) this.numericUpDown_initialBpm.Value / (double) this.numericUpDown_stretchFactor.Value;
			this.numericUpDown_targetBpm.Value = Math.Clamp((decimal) targetBpm, this.numericUpDown_targetBpm.Minimum, this.numericUpDown_targetBpm.Maximum);
		}


		// TimeStretch Execution + Cancellation
		private async void button_stretch_Click(object sender, EventArgs e)
		{
			if (this.isProcessing || this.SelectedMethod == null)
			{
				return;
			}

			this.progressBar_stretching.Value = this.progressBar_stretching.Minimum;
			this.SetProcessingState(true);

			try
			{
				// Ensure Tracks
				if ((this.Tracks == null || !this.Tracks.Any()) && this.trackView != null)
				{
					// NOTE: TrackView.Audio ist readonly und wird im TrackView ctor bereits geklont. :contentReference[oaicite:1]{index=1}
					// Wir arbeiten in-place auf dieser Instanz.
					this.Tracks = [this.trackView.Audio];
				}
				if (this.Tracks == null || !this.Tracks.Any())
				{
					throw new InvalidOperationException("No valid tracks for Time-Stretch processing.");
				}

				MethodInfo method = this.SelectedMethod;
				ParameterInfo[] sig = method.GetParameters();

				// Base parameter dictionary:
				var callBase = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
				foreach (var kv in this.ExcludedParameters)
				{
					callBase[kv.Key] = kv.Value;
				}

				foreach (var kv in this.ParameterValues)
				{
					callBase[kv.Key] = kv.Value;
				}

				// Multi-track weighting for ETA & global progress
				var trackList = this.Tracks.ToList();

				long[] weights = new long[trackList.Count];
				long totalWeight = 0;

				for (int i = 0; i < trackList.Count; i++)
				{
					var a = trackList[i];
					long w = 1;

					try
					{
						if (a?.Data != null && a.Data.Length > 0)
						{
							int ch = Math.Max(1, a.Channels);
							w = Math.Max(1, a.Data.Length / ch); // frames weight
						}
					}
					catch { }

					weights[i] = w;
					totalWeight += w;
				}
				if (totalWeight <= 0)
				{
					totalWeight = 1;
				}

				// Start timer + reset ETA state
				StartProcessingTimer();

				long doneWeightBeforeTrack = 0;

				for (int ti = 0; ti < trackList.Count; ti++)
				{
					// Cooperative cancel before starting a track
					if (this.ProcessingCancellationSource?.IsCancellationRequested == true)
					{
						break;
					}
					var track = trackList[ti];
					long trackWeight = weights[ti];

					// Per-track progress -> global weighted progress
					var perTrackProgress = new Progress<double>(p =>
					{
						p = Math.Clamp(p, 0.0, 1.0);

						double global01 = (doneWeightBeforeTrack + (trackWeight * p)) / totalWeight;

						lock (this.EtaGate)
						{
							this.ProgressGlobal01 = Math.Clamp(global01, 0.0, 1.0);
						}

						int scaled = (int) Math.Round(global01 * this.progressBar_stretching.Maximum);
						this.progressBar_stretching.Value = Math.Clamp(
							scaled,
							this.progressBar_stretching.Minimum,
							this.progressBar_stretching.Maximum);
					});

					// Override progress for this track
					callBase["progress"] = perTrackProgress;

					// Build args in signature order
					object?[] args = new object?[sig.Length];

					for (int i = 0; i < sig.Length; i++)
					{
						var p = sig[i];

						// Audio parameter
						if (typeof(AudioObj).IsAssignableFrom(p.ParameterType))
						{
							args[i] = track;
							continue;
						}

						// factor parameter
						if ((p.Name?.Contains("factor", StringComparison.OrdinalIgnoreCase) ?? false) && (p.ParameterType == typeof(double) || p.ParameterType == typeof(float) || p.ParameterType == typeof(decimal)))
						{
							double f = this.StretchFactor;
							args[i] =
								p.ParameterType == typeof(float) ? (float) f :
								p.ParameterType == typeof(decimal) ? (decimal) f :
								 f;
							continue;
						}

						// Provided by dict
						if (p.Name != null && callBase.TryGetValue(p.Name, out var v))
						{
							args[i] = CoerceToParameterType(v, p.ParameterType, p);
							continue;
						}

						// Default or type default
						args[i] = p.HasDefaultValue ? p.DefaultValue : GetDefaultValue(p.ParameterType);
					}

					CudaLog.Info($"TimeStretch start: trackIndex={ti + 1}, totalTracks={trackList.Count}, method={method.Name}, chunkSize={(this.checkBox_autoChunking.Checked ? 0 : this.ChunkSize)}, overlap={(this.checkBox_autoChunking.Checked ? 0f : this.Overlap):0.###}, factor={this.StretchFactor:0.#####}, normalize={this.Normalize:0.###}, maxWorkers={this.MaxWorkers}, channels={track.Channels}, sampleRate={track.SampleRate}, samples={(track.Data == null ? 0L : track.Data.LongLength)}, frames={((track.Data == null ? 0L : track.Data.LongLength) / Math.Max(1, track.Channels))}, durationSec={(((track.Data == null ? 0L : track.Data.LongLength) / Math.Max(1, track.Channels)) / (double) Math.Max(1, track.SampleRate)):0.###}", null, "TimeStretch");


					// Invoke + await properly
					object? invokeResult = method.Invoke(null, args);

					if (invokeResult is Task<AudioObj> taskAudio)
					{
						AudioObj? returned = await taskAudio.ConfigureAwait(true);
						if (this.ProcessingCancellationSource?.IsCancellationRequested == true)
						{
							break;
						}

						// If a method returns a new instance, COPY into the existing one (TrackView holds readonly instance)
						if (returned != null)
						{
							track.CopyAudioObj(returned);

							// optional: if returned is a temporary instance
							try { returned.Dispose(); } catch { }
						}
					}
					else if (invokeResult is Task task)
					{
						await task.ConfigureAwait(true);
						if (this.ProcessingCancellationSource?.IsCancellationRequested == true)
						{
							break;
						}
					}
					else
					{
						throw new InvalidOperationException($"Selected method '{method.Name}' did not return Task or Task<AudioObj>.");
					}

					// Track done
					doneWeightBeforeTrack += trackWeight;

					lock (this.EtaGate)
					{
						this.ProgressGlobal01 = Math.Clamp(doneWeightBeforeTrack / (double) totalWeight, 0.0, 1.0);
					}

					CudaLog.Info($"TimeStretch done: trackIndex={ti + 1}, totalTracks={trackList.Count}, channels={track.Channels}, sampleRate={track.SampleRate}, samples={(track.Data == null ? 0L : track.Data.LongLength)}, frames={((track.Data == null ? 0L : track.Data.LongLength) / Math.Max(1, track.Channels))}, durationSec={(((track.Data == null ? 0L : track.Data.LongLength) / Math.Max(1, track.Channels)) / (double) Math.Max(1, track.SampleRate)):0.###}", null, "TimeStretch");
					WindowMain.UpdateAllCollectionViews();
					WindowMain.UpdateTrackDependentUi(track);
				}

				// Finalize (ETA ~= elapsed)
				lock (this.EtaGate)
				{
					this.ProgressGlobal01 = 1.0;
				}
				this.progressBar_stretching.Value = this.progressBar_stretching.Maximum;
				this.ProcessingTimer_Tick(this, EventArgs.Empty);

				// If cancelled, just stop processing without closing dialog
				if (this.ProcessingCancellationSource?.IsCancellationRequested == true)
				{
					return;
				}
			}
			catch (Exception ex)
			{
				MessageBox.Show(this, $"Fehler beim Time-Stretch: {ex.Message}", "Time Stretch", MessageBoxButtons.OK, MessageBoxIcon.Error);
				this.progressBar_stretching.Value = this.progressBar_stretching.Minimum;
			}
			finally
			{
				if (!this.IsDisposed)
				{
					this.SetProcessingState(false);
				}

				this.DialogResult = DialogResult.OK;
				this.Close();
			}

			static object? GetDefaultValue(Type t) => t.IsValueType ? Activator.CreateInstance(t) : null;

			static object? CoerceToParameterType(object? value, Type targetType, ParameterInfo p)
			{
				if (value == null)
				{
					if (!targetType.IsValueType || Nullable.GetUnderlyingType(targetType) != null)
					{
						return null;
					}

					return Activator.CreateInstance(targetType);
				}

				if (targetType.IsInstanceOfType(value))
				{
					return value;
				}

				Type effectiveTarget = Nullable.GetUnderlyingType(targetType) ?? targetType;

				try
				{
					if (effectiveTarget.IsEnum)
					{
						if (value is string s)
						{
							return Enum.Parse(effectiveTarget, s, ignoreCase: true);
						}

						object underlying = Convert.ChangeType(value, Enum.GetUnderlyingType(effectiveTarget), System.Globalization.CultureInfo.InvariantCulture);
						return Enum.ToObject(effectiveTarget, underlying);
					}

					return Convert.ChangeType(value, effectiveTarget, System.Globalization.CultureInfo.InvariantCulture);
				}
				catch
				{
					return p.HasDefaultValue ? p.DefaultValue : GetDefaultValue(targetType);
				}
			}
		}

		private void button_cancel_Click(object sender, EventArgs e)
		{
			if (this.isProcessing)
			{
				try
				{
					this.ProcessingCancellationSource?.Cancel();
				}
				catch { }
				// keep dialog open; processing loop will exit and UI will re-enable controls
				return;
			}

			this.Close();
		}



		// Progress / ETA Timer
		private void SetProcessingState(bool processing)
		{
			this.isProcessing = processing;

			this.button_stretch.Enabled = !processing;
			// Allow cancelling while processing
			this.button_cancel.Enabled = true;

			// These 2 are controlled by autoChunking state outside processing; keep consistent:
			this.numericUpDown_chunkSize.Enabled = !processing && !this.checkBox_autoChunking.Checked;
			this.numericUpDown_overlap.Enabled = !processing && !this.checkBox_autoChunking.Checked;

			this.numericUpDown_initialBpm.Enabled = !processing;
			this.numericUpDown_targetBpm.Enabled = !processing;
			this.numericUpDown_stretchFactor.Enabled = !processing;
			this.numericUpDown_threads.Enabled = !processing;
			this.numericUpDown_normalize.Enabled = !processing;
			this.comboBox_versions.Enabled = !processing;
			this.panel_parameters.Enabled = !processing;
			this.checkBox_autoChunking.Enabled = !processing;

			this.UseWaitCursor = processing;

			if (!processing)
			{
				StopProcessingTimer();
			}
		}

		private void StartProcessingTimer()
		{
			lock (this.EtaGate)
			{
				this.ProcessingStarted = DateTime.UtcNow;
				this.ProgressGlobal01 = 0.0;

				this.EtaSamples.Clear();
				this.EtaSecondsRemainingSmoothed = -1.0;
			}

			if (this.ProcessingTimer == null)
			{
				this.ProcessingTimer = new System.Windows.Forms.Timer();
				this.ProcessingTimer.Tick += this.ProcessingTimer_Tick;
			}

			this.ProcessingTimer.Interval = Math.Clamp(this.ProcessingTimerIntervalMs, 50, 2000);
			this.ProcessingTimer.Start();

			// immediate update
			this.ProcessingTimer_Tick(this.ProcessingTimer, EventArgs.Empty);
		}

		private void StopProcessingTimer()
		{
			try
			{
				this.ProcessingTimer?.Stop();
			}
			catch { }
		}

		private void ProcessingTimer_Tick(object? sender, EventArgs e)
		{
			if (!this.isProcessing)
			{
				return;
			}

			double elapsedSec = (DateTime.UtcNow - this.ProcessingStarted).TotalSeconds;

			double p01;
			double etaTotalSec;

			lock (this.EtaGate)
			{
				p01 = Math.Clamp(this.ProgressGlobal01, 0.0, 1.0);

				// Add sample to recent window
				this.EtaSamples.Enqueue((elapsedSec, p01));

				// Drop old samples beyond window
				while (this.EtaSamples.Count > 2)
				{
					var first = this.EtaSamples.Peek();
					if (elapsedSec - first.tSec <= EtaWindowSeconds)
					{
						break;
					}

					this.EtaSamples.Dequeue();
				}

				// Estimate speed using oldest->newest in window
				double speed = 0.0; // progress per second
				if (this.EtaSamples.Count >= 2)
				{
					var first = this.EtaSamples.Peek();
					var last = this.EtaSamples.Last();

					double dt = last.tSec - first.tSec;
					double dp = last.p01 - first.p01;
					if (dt > 1e-6 && dp > 0)
					{
						speed = dp / dt;
					}
				}

				double remain = -1.0;
				if (p01 >= 0.999999)
				{
					remain = 0.0;
				}
				else if (speed > 1e-9)
				{
					remain = (1.0 - p01) / speed;
				}

				// Smooth remaining seconds a bit (prevents jitter)
				if (remain >= 0)
				{
					if (this.EtaSecondsRemainingSmoothed < 0)
					{
						this.EtaSecondsRemainingSmoothed = remain;
					}
					else
					{
						// EMA alpha based on tick dt
						double dtTick = 0.001 * Math.Max(1, this.ProcessingTimerIntervalMs);
						double alpha = 1.0 - Math.Exp(-dtTick / EtaRemainTauSeconds);

						this.EtaSecondsRemainingSmoothed =
							(alpha * remain) + ((1.0 - alpha) * this.EtaSecondsRemainingSmoothed);
					}
				}

				etaTotalSec = (p01 >= 0.999999)
					? elapsedSec
					: (this.EtaSecondsRemainingSmoothed >= 0 ? elapsedSec + this.EtaSecondsRemainingSmoothed : elapsedSec);
			}

			this.label_processingTime.Text =
				$"Elapsed: {FormatTime(elapsedSec)} / ~{FormatTime(etaTotalSec)}";
		}



		// Form Closing
		private void TimeStretchDialog_FormClosing(object? sender, FormClosingEventArgs e)
		{
			if (this.isProcessing)
			{
				e.Cancel = true;
				return;
			}

			try
			{
				this.ProcessingCancellationSource?.Cancel();
			}
			catch { }
		}
	}
}
