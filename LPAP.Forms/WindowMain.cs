using LPAP.Audio;
using LPAP.Cuda;
using LPAP.Forms.Dialogs;
using LPAP.Forms.Views;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Timer = System.Windows.Forms.Timer;

namespace LPAP.Forms
{
	public partial class WindowMain : Form
	{
		internal static WindowMain? Instance { get; private set; }

		internal static TrackView? LastSelectedTrackView
		{
			get => _lastSelectedTrackView;
			set
			{
				if (_lastSelectedTrackView != value)
				{
					_lastSelectedTrackView = value;
					LoopControlWindow?.UpdateLoopButtonsState();
				}
			}
		}
		internal static TrackView? _lastSelectedTrackView = null;
		internal static LoopControl? LoopControlWindow { get; set; } = null;

		internal static readonly BindingList<AudioCollectionView> OpenAudioCollectionViews = [];
		internal static readonly BindingList<TrackView> OpenTrackViews = [];

		internal static string LastImportDirectory { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);

		private readonly AudioCollection AudioC = new();


		internal static bool AutoApplyOnClose { get; set; } = false;


		public WindowMain()
		{
			Instance = this;
			this.InitializeComponent();
			WindowsScreenHelper.SetWindowScreenPosition(this, [AnchorStyles.Right, AnchorStyles.Top]);

			AutoApplyOnClose = this.checkBox_autoApply.Checked;

			// Fill cuda devices
			this.ComboBox_FillCudaDevices();
			this.ListBox_Bind_CudaLog();

			// initialize statistics monitoring and assign mandatory attribute
			this._statisticsTimer = this.InitializeStatisticsTimer();
			this.StatisticsUpdateDelayMs = (int) this.numericUpDown_statisticsUpdateDelay.Value;


		}



		internal static void UpdateAllCollectionViews()
		{
			foreach (var acv in OpenAudioCollectionViews)
			{
				acv.RefreshListBox();
			}
		}


		private async void button_import_Click(object sender, EventArgs e)
		{
			using var ofd = new OpenFileDialog
			{
				Title = "Import audio files",
				Filter = "Audio Files|*.mp3;*.wav;*.flac|All Files|*.*",
				Multiselect = true,
				InitialDirectory = LastImportDirectory,
			};

			if (ofd.ShowDialog(this) != DialogResult.OK)
			{
				return;
			}

			LastImportDirectory = Path.GetDirectoryName(ofd.FileNames[0]) ?? LastImportDirectory;

			var files = ofd.FileNames;
			if (files == null || files.Length == 0)
			{
				return;
			}

			// Ordnername für Titel der ACV
			string? folder = Path.GetDirectoryName(files[0]);
			string windowTitle = string.IsNullOrWhiteSpace(folder)
				? "_Audio"
				: Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

			// Parallele Imports begrenzen, damit Playback-Threads Luft haben
			int maxParallel = Math.Max(1, Environment.ProcessorCount / 2);

			// In die zentrale AudioCollection des Main-Fensters importieren
			var imported = await this.AudioC.AddFromFilesAsync(files, maxParallelImports: maxParallel);

			if (imported.Count == 0)
			{
				return;
			}

			// Neue ACV mit genau diesen AudioObj-Referenzen
			var view = new AudioCollectionView(imported);

		}

		private void button_reflow_Click(object sender, EventArgs e)
		{
			TrackView.ReflowAllTrackViews();
		}

		private void checkBox_enableMonitoring_CheckedChanged(object sender, EventArgs e)
		{
			this.StatisticsEnabled = this.checkBox_enableMonitoring.Checked;
			this.numericUpDown_statisticsUpdateDelay.Enabled = this.checkBox_enableMonitoring.Checked;
			if (!this.StatisticsEnabled)
			{
				this.pictureBox_cores.Image = null;
				this.progressBar_memory.Value = 0;
				this.label_memory.Text = "Memory: N/A";
			}
			else
			{
				this.StatisticsUpdateDelayMs = (int) this.numericUpDown_statisticsUpdateDelay.Value;
			}
		}

		private void numericUpDown_statisticsUpdateDelay_ValueChanged(object sender, EventArgs e)
		{
			this.StatisticsUpdateDelayMs = (int) this.numericUpDown_statisticsUpdateDelay.Value;
		}

		private void checkBox_autoApply_CheckedChanged(object sender, EventArgs e)
		{
			AutoApplyOnClose = this.checkBox_autoApply.Checked;
		}

		private void button_looping_Click(object sender, EventArgs e)
		{
			LoopControlWindow ??= new LoopControl();
			LoopControlWindow.Show();
		}

	}
}
