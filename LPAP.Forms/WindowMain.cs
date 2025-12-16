using LPAP.Audio;
using LPAP.Cuda;
using LPAP.Forms.Dialogs;
using LPAP.Forms.Views;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Runtime.CompilerServices;
using Timer = System.Windows.Forms.Timer;

namespace LPAP.Forms
{
    public partial class WindowMain : Form
    {
        internal static WindowMain? Instance { get; private set; }
        internal static string ExportDirectory { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);

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

            // Set export directory
            ExportDirectory = NvencVideoRenderer.ReadExportPath_From_LocalStats(true) ?? ExportDirectory;
            this.label_exportDirectory.Text = "Dir: " + ShortenPathForDisplay(ExportDirectory, 2, 1);
            NvencVideoRenderer.WriteExportPath_To_LocalStats(ExportDirectory);

            // initialize statistics monitoring and assign mandatory attribute
            this._statisticsTimer = this.InitializeStatisticsTimer();
            this.StatisticsUpdateDelayMs = (int) this.numericUpDown_statisticsUpdateDelay.Value;

            this.Setup_UiToolTips();
        }



        internal static void UpdateAllCollectionViews()
        {
            foreach (var acv in OpenAudioCollectionViews)
            {
                acv.RefreshListBox();
            }
        }


        private void Setup_UiToolTips()
        {
            var toolTip = new ToolTip();

            // Button info
            toolTip.SetToolTip(this.button_cudaInfo, $" ~ Hardware Info ~ \n\n - Click for CUDA info\n\n - Ctrl-Click for system info");
        }

        internal static string ShortenPathForDisplay(string fullPath, int firstDirectiries = 2, int lastDirectories = 1)
        {
            firstDirectiries = Math.Max(1, firstDirectiries);
            lastDirectories = Math.Max(1, lastDirectories);
            var parts = fullPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (parts.Length <= firstDirectiries + lastDirectories + 1)
            {
                return fullPath;
            }

            return string.Join(Path.DirectorySeparatorChar.ToString(), parts.Take(firstDirectiries))
                + Path.DirectorySeparatorChar + "..." + Path.DirectorySeparatorChar
                + string.Join(Path.DirectorySeparatorChar.ToString(), parts.Skip(parts.Length - lastDirectories));
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

        private void button_browse_Click(object sender, EventArgs e)
        {
            bool ctrlFlag = (ModifierKeys & Keys.Control) == Keys.Control;

            if (ctrlFlag)
            {
                // Open Explorer at export directory
                try
                {
                    Process.Start("explorer.exe", ExportDirectory);
                }
                catch (Exception ex)
                {
                    CudaLog.Error(ex, "Failed to open export directory in Explorer", "UI");
                }

                return;
            }

            // Select export directory
            using var fbd = new FolderBrowserDialog
            {
                Description = "Select Export Directory",
                SelectedPath = ExportDirectory,
                ShowNewFolderButton = true,
            };
            if (fbd.ShowDialog(this) == DialogResult.OK)
            {
                ExportDirectory = fbd.SelectedPath;
                this.label_exportDirectory.Text = "Dir: " + ShortenPathForDisplay(ExportDirectory, 2, 1);
                NvencVideoRenderer.WriteExportPath_To_LocalStats(ExportDirectory);
                CudaLog.Info($"Set export directory to: {ExportDirectory}", "", "UI");
            }
        }
    }
}
