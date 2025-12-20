using LPAP.Audio;
using LPAP.OpenVino;
using LPAP.OpenVino.Util;
using LPAP.Forms;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using LPAP.Cuda;
using static LPAP.OpenVino.Util.HuggingfaceBrowser;
using Timer = System.Windows.Forms.Timer;
using System.Diagnostics;

namespace LPAP.Forms.Dialogs
{
	public partial class OpenVinoDialog : Form
	{
		private readonly AudioObj Audio;

		private OpenVinoService _service = new(new OpenVinoServiceOptions { ModelsRootDirectory = @"D:\Models", DefaultDevice = "AUTO" });
		private HuggingfaceBrowser _hfBrowser = new();

		private CancellationTokenSource? _searchCts;
		private CancellationTokenSource? _downloadCts;
		private readonly Timer _dlTimer = new();
		private DateTime? _dlStartUtc;
		private double _lastProgress01;
		private readonly Queue<(double tSec, double p)> _progressSamples = new();
		private double _estimatedTotalSec;
		private bool _hadAnyProgress;
		private long _expectedTotalBytes;
		private double _avgMbPerSec;

		internal string? SelectedModel => this.comboBox_models.SelectedItem as string;


		public OpenVinoDialog(AudioObj audio)
		{
			this.InitializeComponent();
			this.Audio = audio.Clone();
			this.Text = "OpenVINO: '" + this.Audio.Name + "'";

			this.ComboBox_FillDevices();
			this.ComboBox_FillModels();
			this.label_directory.Text = WindowMain.ShortenPathForDisplay(this._hfBrowser.DownloadDirectory, 1, 1);

			// listBox owner draw for HF search results
			this.listBox_modelsBrowser.DrawMode = DrawMode.OwnerDrawFixed;
			this.listBox_modelsBrowser.ItemHeight = Math.Max(this.listBox_modelsBrowser.ItemHeight, 18);
			this.listBox_modelsBrowser.DrawItem += this.listBox_models_DrawItem;

			// Enter key triggers search
			this.textBox_query.KeyDown += (s, e) =>
			{
				if (e.KeyCode == Keys.Enter)
				{
					e.Handled = true;
					e.SuppressKeyPress = true;
					this.button_search_Click(this.button_search, EventArgs.Empty);
				}
			};

			this.SetupDlTimer();
		}



		// Setup UI with (info)
		private void ComboBox_FillDevices()
		{
			this.comboBox_devices.SuspendLayout();
			this.comboBox_devices.Items.Clear();

			this.comboBox_devices.Items.AddRange(this._service.GetAvailableDeviceInfos().Select(i => "[" + i.DeviceId + "] '" + i.DeviceName + "'").ToArray());
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
			this.comboBox_models.Items.AddRange(this._service.ListModels().ToArray());
			if (this.comboBox_models.Items.Count > 0)
			{
				this.comboBox_models.SelectedIndex = 0;
			}
			this.comboBox_models.ResumeLayout();
		}

		private async void button_modelInfo_Click(object sender, EventArgs e)
		{
			if (string.IsNullOrEmpty(this.SelectedModel))
			{
				MessageBox.Show("No Model selected. Info is not available.", "OpenVino Model Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
				return;
			}

			var info = await this._service.GetModelInfoAsync(this.SelectedModel);
			string i = string.Empty;
			if (info == null)
			{
				i = "ERROR getting OpenVino Model-Info";
			}
			else
			{
				i = "'" + info.Name + "'" + Environment.NewLine;
				i += "XML: " + info.XmlPath;
				i += "BIN: " + info.BinPath;
				i += "Inputs: " + string.Join(", ", info.Inputs.Select(x => x.ElementType + "<" + x.Shape + ">"));
				i += "Outputs: " + string.Join(", ", info.Outputs.Select(x => x.ElementType + "<" + x.Shape + ">"));
			}


		}



		// Model inference
		private async void button_inference_Click(object sender, EventArgs e)
		{
			if (string.IsNullOrWhiteSpace(this.SelectedModel))
			{
				MessageBox.Show("Please select an IR model.", "OpenVino", MessageBoxButtons.OK, MessageBoxIcon.Warning);
				return;
			}

			var cur = Cursor.Current;
			Cursor.Current = Cursors.WaitCursor;
			this.button_inference.Enabled = false;

			try
			{
				var opts = new OpenVino.Separation.MusicSeparationOptions
				{
					AutoConvertAudioFormat = true,
					TargetSampleRate = 48000,
					TargetChannels = this.Audio.Channels,
					ClampOutput = true,
					MatchInputPeak = true,
					EnableBatching = true,
					BatchSize = 2
				};

				IProgress<double> progress = new Progress<double>(p =>
				{
					p = Math.Clamp(p, 0.0, 1.0);
					try { this.progressBar_inferencing.Value = (int) Math.Round(p * this.progressBar_inferencing.Maximum); } catch { }
					try { this.label_percentage.Text = (p * 100.0).ToString("F1") + " %"; } catch { }
				});

				var stems = await this._service.SeparateToStemsAsync(
					input: this.Audio,
					modelNameOrPath: this.SelectedModel!,
					options: opts,
					device: null,
					progress: progress,
					ct: default).ConfigureAwait(true);

				// TODO: integrate into UI/collection; for now, notify
				MessageBox.Show($"Inference complete. Generated {stems.Length} stems.", "OpenVino", MessageBoxButtons.OK, MessageBoxIcon.Information);
			}
			catch (Exception ex)
			{
				CudaLog.Error(ex, null, "OpenVino-Inference");
				MessageBox.Show("Inference failed: " + ex.Message, "OpenVino", MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
			finally
			{
				this.button_inference.Enabled = true;
				Cursor.Current = cur;
			}
		}




		// HF model browser/download
		private async void button_search_Click(object sender, EventArgs e)
		{
			string query = this.textBox_query.Text.Trim();
			int limit = (int) this.numericUpDown_resultsLimit.Value;
			string[]? exts = null;
			if (this.checkBox_stemsOnly.Checked)
			{
				// Prefer OpenVINO IR models: include both .xml (network) and .bin (weights)
				exts = [".xml", ".bin"];
			}

			this._searchCts = new CancellationTokenSource();
			var cur = Cursor.Current;
			Cursor.Current = Cursors.WaitCursor;
			this.button_search.Enabled = false;
			Stopwatch sw = Stopwatch.StartNew();

			try
			{
				var results = await this._hfBrowser.SearchModelsAsync(query, this.checkBox_stemsOnly.Checked, limit, extensions: exts, ct: this._searchCts.Token);
				if (!results.Any())
				{
					// MsgBox + return
					MessageBox.Show("No HF models found.", "OpenVino", MessageBoxButtons.OK, MessageBoxIcon.Information);
					return;
				}

				// Bind to listBox_modelsBrowser
				this.listBox_modelsBrowser.BeginUpdate();
				try
				{
					this.listBox_modelsBrowser.Items.Clear();
					foreach (var r in results)
					{
						this.listBox_modelsBrowser.Items.Add(r);
					}
				}
				finally
				{
					this.listBox_modelsBrowser.EndUpdate();
					CudaLog.Info($"HF Search found {results.Count} ({limit} max.) models for query '{query}'", $"{Math.Round(sw.Elapsed.TotalMilliseconds, 0)} ms", "OpenVino");
				}
			}
			catch (Exception ex)
			{
				CudaLog.Error(ex, null, "OpenVino");
			}
			finally
			{
				sw.Stop();
				this.button_search.Enabled = true;
				Cursor.Current = cur;
			}
		}

		private async void button_download_Click(object sender, EventArgs e)
		{
			// Get selected Item in listBox_modelsBrowser as HfModelSearchResult
			var selected = this.listBox_modelsBrowser.SelectedItem as HfModelSearchResult;
			if (selected == null)
			{
				MessageBox.Show("Please select a model from the list.", "OpenVino", MessageBoxButtons.OK, MessageBoxIcon.Warning);
				return;
			}

			this._downloadCts = new CancellationTokenSource();
			var cur = Cursor.Current;
			Cursor.Current = Cursors.WaitCursor;
			this.button_download.Enabled = false;

			try
			{
				this.ResetDlStats();
				this._dlStartUtc = DateTime.UtcNow;
				this._dlTimer.Start();

				IProgress<double> progress = new Progress<double>(p =>
				{
					p = Math.Clamp(p, 0.0, 1.0);
					try { this.progressBar_inferencing.Value = (int) Math.Round(p * this.progressBar_inferencing.Maximum); } catch { }

					if (this._dlStartUtc.HasValue)
					{
						var nowSec = (DateTime.UtcNow - this._dlStartUtc.Value).TotalSeconds;
						this._lastProgress01 = p;
						this._hadAnyProgress = true;
						this.PushProgressSample(nowSec, p);
						this.RecomputeEtaFromSamples(nowSec, p);
						this.UpdateAvgSpeedFromSamples();
					}
				});

				// Estimate total known bytes
				try
				{
					var info = await this._hfBrowser.GetModelInfoAsync(selected.ModelId ?? string.Empty, this._downloadCts.Token).ConfigureAwait(true);
					this._expectedTotalBytes = info.Siblings?.Where(s => s?.SizeInBytes is > 0).Sum(s => s!.SizeInBytes!.Value) ?? 0;
				}
				catch { this._expectedTotalBytes = 0; }

				string dir = await this._hfBrowser.DownloadOpenVinoIrAsync(
					selected.ModelId ?? string.Empty,
					revision: selected.Sha ?? "main",
					progress: progress,
					ct: this._downloadCts.Token).ConfigureAwait(true);

				CudaLog.Info($"Downloaded to '{dir}'", "", "OpenVino-DL");
			}
			catch (Exception ex)
			{
				CudaLog.Error(ex, null, "OpenVino-DL");
			}
			finally
			{
				this.button_download.Enabled = true;
				Cursor.Current = cur;
				try { this._dlTimer.Stop(); } catch { }
				this.ComboBox_FillModels();
			}
		}

		// ----------- Owner draw for HF search results -----------
		private void listBox_models_DrawItem(object? sender, DrawItemEventArgs e)
		{
			if (e.Index < 0 || e.Index >= this.listBox_modelsBrowser.Items.Count)
			{
				return;
			}

			var item = this.listBox_modelsBrowser.Items[e.Index] as HfModelSearchResult;
			string text;
			if (item is null)
			{
				text = this.listBox_modelsBrowser.Items[e.Index]?.ToString() ?? string.Empty;
			}
			else
			{
				// Build display text: ID - 'Name' (x MB). We don't have name/size reliably; use ID and downloads as hint
				string id = item.ModelId ?? "<unknown>";
				text = $"{id} - '{(item.PipelineTag ?? "model")}'";
			}

			e.DrawBackground();
			using var br = new SolidBrush(e.ForeColor);
			var font = e.Font ?? this.listBox_modelsBrowser.Font; // Sicherstellen, dass Font nicht null ist
			e.Graphics.DrawString(text, font, br, e.Bounds);
			e.DrawFocusRectangle();
		}

		// ----------- DL timing / ETA / speed -----------
		private void SetupDlTimer()
		{
			this._dlTimer.Interval = 250;
			this._dlTimer.Tick += (_, __) => this.DlTimer_Tick();
		}

		private void ResetDlStats()
		{
			this._dlStartUtc = null;
			this._lastProgress01 = 0.0;
			this._estimatedTotalSec = 0.0;
			this._hadAnyProgress = false;
			this._progressSamples.Clear();
			this._expectedTotalBytes = 0;
			this._avgMbPerSec = 0.0;
			try { this.label_downloadSpeed.Text = "~ 0.00 MB/s"; } catch { }
			try { this.label_elapsed.Text = "Elapsed: 0.00s / ~ 0.00s"; } catch { }
		}

		private void DlTimer_Tick()
		{
			if (!this._dlStartUtc.HasValue)
			{
				return;
			}

			double elapsedSec = (DateTime.UtcNow - this._dlStartUtc.Value).TotalSeconds;
			string left = FormatTime(elapsedSec);

			string right = (!this._hadAnyProgress || this._estimatedTotalSec <= 0.01)
				? "0.00s"
				: FormatTime(this._estimatedTotalSec);

			try { this.label_elapsed.Text = $"Elapsed: {left} / ~ {right}"; } catch { }
			try { this.label_downloadSpeed.Text = "~ " + this._avgMbPerSec.ToString("F2") + " MB/s"; } catch { }
		}

		private static string FormatTime(double seconds)
		{
			if (seconds < 60.0)
			{
				return seconds.ToString("F2") + "s";
			}

			var ts = TimeSpan.FromSeconds(seconds);
			return ts.Minutes + "m " + ts.Seconds + "s";
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

			if (dt < 0.15 || dp < 0.0001) { return; }

			double speed = dp / dt;
			if (speed <= 1e-9) { return; }

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

		private void UpdateAvgSpeedFromSamples()
		{
			if (this._progressSamples.Count < 2 || this._expectedTotalBytes <= 0)
			{
				return;
			}

			var first = this._progressSamples.Peek();
			var last = this._progressSamples.Last();

			double dt = last.tSec - first.tSec;
			double dp = last.p - first.p;

			if (dt < 0.25 || dp <= 0) { return; }

			double bytesDone = dp * this._expectedTotalBytes;
			double mbPerSec = (bytesDone / dt) / (1024.0 * 1024.0);

			if (double.IsFinite(mbPerSec) && mbPerSec > 0)
			{
				if (this._avgMbPerSec <= 0.01)
				{
					this._avgMbPerSec = mbPerSec;
				}
				else
				{
					this._avgMbPerSec = this._avgMbPerSec * 0.65 + mbPerSec * 0.35;
				}
			}
		}

		private void button_browse_Click(object sender, EventArgs e)
		{
			// Open folder browser dialog at hfBrowser.DownloadsPath
			using (var fbd = new FolderBrowserDialog())
			{
				fbd.Description = "Select Huggingface Models Download Folder";
				fbd.SelectedPath = this._hfBrowser.DownloadDirectory;
				if (fbd.ShowDialog() == DialogResult.OK)
				{
					this._hfBrowser.DownloadDirectory = fbd.SelectedPath;
					this.label_directory.Text = WindowMain.ShortenPathForDisplay(this._hfBrowser.DownloadDirectory, 1, 1);
				}
			}
		}

		private void listBox_modelsBrowser_DoubleClick(object sender, EventArgs e)
		{
			bool ctrlFlag = (ModifierKeys & Keys.Control) == Keys.Control;
			bool shiftFlag = (ModifierKeys & Keys.Shift) == Keys.Shift;
			string text = string.Empty;

			if (ctrlFlag)
			{
				// Get all items as text (if shiftFlag: add URL too)
				var items = this.listBox_modelsBrowser.Items.Cast<HfModelSearchResult>().ToArray();
				text = string.Join(Environment.NewLine, items.Select(i => i.LibraryName + ": " + i.ModelId));
			}
			else
			{
				// Get single selected item
				var selected = this.listBox_modelsBrowser.SelectedItem as HfModelSearchResult;
				if (selected != null)
				{
					text = selected.LibraryName + ": " + selected.ModelId;
				}
			}

			Clipboard.SetText(text);
			CudaLog.Info("Copied model info to clipboard", "", "Huggingface");
		}
	}
}
