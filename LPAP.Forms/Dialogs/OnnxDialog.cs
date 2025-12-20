using LPAP.Audio;
using LPAP.Cuda;
using LPAP.Forms.Views;
using LPAP.Onnx.Demucs;
using LPAP.Onnx.Runtime;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Globalization;
using System.Management;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;

namespace LPAP.Forms.Dialogs
{
	public partial class OnnxDialog : Form
	{
		private const string DefaultModelPath = @"D:\Models";
		private DemucsModel? _model;
		private DemucsService? _service;

		internal readonly AudioObj Audio;
		private CancellationTokenSource? _cts = null;

		// --- Timer/ETA wie im VisualizerDialog ---
		private readonly Timer _timeTimer = new();
		private DateTime? _inferStarted = null;
		private double _lastProgress01 = 0.0;
		private readonly Queue<(double tSec, double p)> _progressSamples = new();
		private double _estimatedTotalSec = 0.0;
		private bool _hadAnyProgress = false;

		// Steps (Chunks)
		private int _totalSteps = 0;
		private int _doneSteps = 0;

		public OnnxDialog(AudioObj audio)
		{
			this.InitializeComponent();
			this.Audio = audio.Clone();
			this.Text = "ONNX-Control '" + this.Audio.Name + "'";


			this.ComboBox_FillDevices();
			this.ComboBox_FillModels();

			this.SetupTimeTimer();
			this.ResetTimeAndStepsUI();
		}


		private void SetupTimeTimer()
		{
			this._timeTimer.Interval = 250;
			this._timeTimer.Tick += (_, __) => this.TimeTimer_Tick();
		}

		private void ResetTimeAndStepsUI()
		{
			try
			{
				this.label_elapsed.Text = "Elapsed: 0.00s / ~ 0.00s";
				this.label_steps.Text = "0 / 0";
			}
			catch { }
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

		private void TimeTimer_Tick()
		{
			if (!this._inferStarted.HasValue)
			{
				this.ResetTimeAndStepsUI();
				return;
			}

			double elapsedSec = (DateTime.UtcNow - this._inferStarted.Value).TotalSeconds;
			string left = FormatTime(elapsedSec);

			string right;
			if (!this._hadAnyProgress || this._estimatedTotalSec <= 0.01)
			{
				right = "0.00s";
			}
			else
			{
				if (this._lastProgress01 >= 0.999999)
				{
					this._estimatedTotalSec = elapsedSec;
				}
				right = FormatTime(this._estimatedTotalSec);
			}

			this.label_elapsed.Text = $"Elapsed: {left} / ~ {right}";

			// Steps-Label direkt aus Feldern
			this.label_steps.Text = $"{this._doneSteps} / {this._totalSteps}";
		}

		private void PushProgressSample(double elapsedSec, double progress01)
		{
			this._progressSamples.Enqueue((elapsedSec, progress01));
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

		private void ComboBox_FillDevices()
		{
			this.comboBox_devices.SuspendLayout();
			this.comboBox_devices.Items.Clear();

			using var searcher = new ManagementObjectSearcher("SELECT Name, AdapterRAM FROM Win32_VideoController");
			foreach (ManagementObject obj in searcher.Get())
			{
				string name = obj["Name"]?.ToString() ?? "Unknown Device";
				var ramObj = obj["AdapterRAM"];
				ulong ramBytes = ramObj is null or DBNull ? 0UL : Convert.ToUInt64(ramObj, CultureInfo.InvariantCulture);
				double ramGB = ramBytes / (1024.0 * 1024.0 * 1024.0);
				string displayText = $"[{this.comboBox_devices.Items.Count}] {name} - {ramGB:F2} GB RAM";
				this.comboBox_devices.Items.Add(displayText);
			}

			if (this.comboBox_devices.Items.Count > 0)
			{
				this.comboBox_devices.SelectedIndex = 0;
			}

			this.comboBox_devices.ResumeLayout();
		}

		private void ComboBox_FillModels()
		{
			this.comboBox_models.SuspendLayout();
			this.comboBox_models.Items.Clear();

			var basePath = Environment.GetEnvironmentVariable("LPAP_DEMUCS_MODEL_DIR") ?? DefaultModelPath;

			var models = OnnxModelEnumerator.ListModels(basePath);
			if (models.Count == 0)
			{
				this.comboBox_models.Items.Add("(No .onnx models found)");
				this.comboBox_models.Enabled = false;
				this.comboBox_models.SelectedIndex = 0;
				return;
			}

			this.comboBox_models.Items.AddRange(models.ToArray());
			if (this.comboBox_models.Items.Count > 0)
			{
				this.comboBox_models.SelectedIndex = 0;
			}

			this.comboBox_models.ResumeLayout();
		}




		private void button_initialize_Click(object sender, EventArgs e)
		{
			this.button_initialize.Enabled = false;
			this.label_status.Text = "Initializing ONNX Demucs model '" + this.comboBox_models.SelectedItem?.ToString() + "'...";
			var cur = Cursor.Current;

			try
			{
				int deviceIndex = this.comboBox_devices.SelectedIndex;

				if (this.comboBox_models.SelectedItem is not OnnxModelItem modelItem)
				{
					throw new InvalidOperationException("No valid ONNX model selected.");
				}

				// DEMUCS models are typically trained for 44100 Hz stereo.
				// Keep model strict; we will auto-resample/transform in the Forms adapter.
				var demucsOpts = new DemucsOptions
				{
					ModelPath = modelItem.FullPath,
					ExpectedSampleRate = 44100,
					ExpectedChannels = 2
				};

				var onnxOpts = new OnnxOptions
				{
					PreferCuda = true,
					DeviceId = Math.Max(0, deviceIndex),
					WorkerCount = 1,
					QueueCapacity = 4
				};

				Cursor.Current = Cursors.WaitCursor;

				this._model = new DemucsModel(demucsOpts, onnxOpts);
				this._service = new DemucsService(this._model);
				// Bind logging to UI now that service exists
				this.listBox_log.DataSource = this._service.LogLines;

				// optional: show fixed frames for sanity
				this.label_status.Text = $"ONNX Demucs initialized on [{deviceIndex}] OK. FixedT={this._model.FixedInputFrames.ToString() ?? "dynamic"}";
				this.comboBox_devices.Enabled = false;
			}
			catch (Exception ex)
			{
				var dlgResult = MessageBox.Show($"Failed to initialize ONNX Demucs model:\n{ex.Message}\n\n - Copy to Clipboard?", "Error", MessageBoxButtons.YesNo, MessageBoxIcon.Error);
				this.button_initialize.Enabled = true;
				if (dlgResult == DialogResult.Yes)
				{
					Clipboard.SetText(ex.ToString());
				}
			}
			finally
			{
				Cursor.Current = cur;
			}
		}

		private async void button_inference_Click(object sender, EventArgs e)
		{
			if (this._service is null)
			{
				MessageBox.Show("Please initialize the ONNX model first.", "ONNX", MessageBoxButtons.OK, MessageBoxIcon.Information);
				return;
			}

			bool ctrlFlag = (ModifierKeys & Keys.Control) == Keys.Control;

			this.button_inference.Text = "Cancel";
			this.button_inference.BackColor = Color.IndianRed;
			this.button_initialize.Enabled = false;

			Action cancelAction = new(() =>
			{
				try
				{
					this._cts?.Cancel();
					this.button_inference.Enabled = false;
					this.button_inference.Text = "Stopping";
				}
				catch (Exception ex)
				{
					CudaLog.Error(ex, "Error while cancelling ONNX Demucs inference", "ONNX");
				}
			});
			this.button_inference.Click += (_, __) => cancelAction();

			this.comboBox_models.Enabled = false;
			this.progressBar_inferencing.Minimum = 0;
			this.progressBar_inferencing.Maximum = 100;
			this.progressBar_inferencing.Value = 0;
			this.label_status.Text = "Inferencing…";

			// Timer/ETA Start
			this._inferStarted = DateTime.UtcNow;
			this._lastProgress01 = 0.0;
			this._estimatedTotalSec = 0.0;
			this._hadAnyProgress = false;
			this._progressSamples.Clear();
			this._totalSteps = 0;
			this._doneSteps = 0;
			this.ResetTimeAndStepsUI();
			this._timeTimer.Start();

			try
			{
				// Progress (0..1) -> UI-Bar + ETA
				var uiProgress = new Progress<double>(p =>
				{
					p = Math.Clamp(p, 0.0, 1.0);
					this.progressBar_inferencing.Value = Math.Clamp((int) Math.Round(p * 100.0), 0, 100);

					if (p > this._lastProgress01 + 1e-6)
					{
						double nowSec = (DateTime.UtcNow - this._inferStarted!.Value).TotalSeconds;
						this._lastProgress01 = p;
						this._hadAnyProgress = true;

						this.PushProgressSample(nowSec, p);
						this.RecomputeEtaFromSamples(nowSec, p);
					}
				});

				// Steps (Chunks): initial total + Fortschritt (done/total)
				// ETA aus Schrittmittelwert ableiten und sanft glätten
				var stepProgress = new Progress<(int done, int total)>(st =>
				{
					this._totalSteps = Math.Max(0, st.total);
					this._doneSteps = Math.Clamp(st.done, 0, this._totalSteps);
					this.label_steps.Text = $"{this._doneSteps} / {this._totalSteps}";

					if (this._inferStarted.HasValue && this._totalSteps > 0)
					{
						double nowSec = (DateTime.UtcNow - this._inferStarted.Value).TotalSeconds;
						double p = Math.Clamp((double) this._doneSteps / this._totalSteps, 0.0, 1.0);
						this._lastProgress01 = Math.Max(this._lastProgress01, p);
						this._hadAnyProgress = this._hadAnyProgress || p > 0.0;
						this.PushProgressSample(nowSec, p);

						if (this._doneSteps > 0)
						{
							// Correct total estimate: elapsed / progress
							double totalEstimate = p > 0.0 ? (nowSec / p) : 0.0;
							this.RecomputeEtaFromSamples(nowSec, p);
							this._estimatedTotalSec = this._estimatedTotalSec <= 0.01
								? totalEstimate
								: this._estimatedTotalSec * 0.5 + totalEstimate * 0.5;
						}
					}
				});

				var adapter = new Adapters.DemucsAudioObjAdapter(this._service);

				this._cts = new();
				var (drums, bass, other, vocals) = await adapter.SeparateAsync(
					this.Audio,
					chunkSeconds: 6.0,
					overlapFraction: 0.25f,
					progress: uiProgress,
					stepProgress: stepProgress,
					ct: this._cts.Token).ConfigureAwait(true);

				this.label_status.Text = "Inference done.";
				this.progressBar_inferencing.Value = 100;

				var acv = new AudioCollectionView([drums, bass, other, vocals]);
				acv.Rename("Stems - " + this.Audio.Name);
			}
			catch (Exception ex)
			{
				this.label_status.Text = "Inference failed.";
				var dlgResult = MessageBox.Show(ex.ToString() + "\n\n - Copy to Clipboard?", "Inference Error", MessageBoxButtons.YesNo, MessageBoxIcon.Error);
				if (dlgResult == DialogResult.Yes)
				{
					Clipboard.SetText(ex.ToString());
				}
			}
			finally
			{
				try { this._timeTimer.Stop(); } catch { }
				this.button_inference.Enabled = true;
				this.button_inference.Text = "Inference";
				this.button_inference.Click -= (_, __) => cancelAction();
				this.button_inference.ForeColor = SystemColors.ControlText;
				this.button_initialize.Enabled = true;
				this.comboBox_models.Enabled = true;

				if (ctrlFlag)
				{
					this.Close();
				}
			}

			CudaLog.Info($"ONNX Demucs inference finished ({(DateTime.UtcNow - this._inferStarted.Value).TotalSeconds:F1} sec. elapsed)", "", "ONNX");
			WindowMain.MsgBox_ShowLastCudaSession(true);
		}


		private void OnnxControlView_FormClosing(object? sender, FormClosingEventArgs e)
		{
			try
			{
				this._timeTimer.Stop();
				this._cts?.Cancel();
			}
			catch { }
			// Detach logging source on close
			this.listBox_log.DataSource = null;
			this.Close();
		}

		private void listBox_log_MouseDoubleClick(object sender, MouseEventArgs e)
		{
			bool ctrlFlag = (ModifierKeys & Keys.Control) == Keys.Control;

			string text = string.Empty;
			if (ctrlFlag)
			{
				text = string.Join(Environment.NewLine, this.listBox_log.Items.Cast<string>());
			}
			else
			{
				text = this.listBox_log.SelectedItem as string ?? string.Empty;
			}

			if (!string.IsNullOrWhiteSpace(text))
			{
				Clipboard.SetText(text);
				CudaLog.Info("Copied log text to clipboard.", "", "ONNX");
			}
		}
	}
}
