using LPAP.Audio;
using LPAP.Forms.Views;
using System.ComponentModel;

namespace LPAP.Forms
{
    public partial class WindowMain : Form
    {
        internal static readonly BindingList<AudioCollectionView> OpenAudioCollectionViews = [];

        internal static string LastImportDirectory { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);

        private readonly AudioCollection AudioC = new();


        public WindowMain()
        {
            this.InitializeComponent();
            WindowsScreenHelper.SetWindowScreenPosition(this, [AnchorStyles.Right, AnchorStyles.Top]);


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

    }
}
