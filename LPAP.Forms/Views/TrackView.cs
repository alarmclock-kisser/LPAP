using LPAP.Audio;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;

namespace LPAP.Forms.Views
{
    public partial class TrackView : Form
    {
        internal readonly AudioObj Audio;
        internal readonly AudioCollection? SourceAudioCollection = null;

        internal readonly DateTime CreatedAt = DateTime.Now;

        internal bool Synced => this.checkBox_sync.Checked;
        internal bool Muted => this.checkBox_mute.Checked;
        internal float Volume => 1f - (this.vScrollBar_volume.Value / (float) this.vScrollBar_volume.Maximum);

        private const int MinSamplesPerPixel = 1;
        private const int MaxSamplesPerPixel = 16384;

        private int _samplesPerPixel = 128;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        internal int SamplesPerPixel
        {
            get => this._samplesPerPixel;
            set
            {
                int clamped = Math.Clamp(value, MinSamplesPerPixel, MaxSamplesPerPixel);
                if (clamped == this._samplesPerPixel)
                {
                    return;
                }

                this._samplesPerPixel = clamped;
                this.RenameForm(addZoomLevel: true);
            }
        }

        public static int InitialWidth { get; set; } = 600;
        public static int MinimumWidth { get; set; } = 200;
        public static int MaximumWidth { get; set; } = 4096;
        public static bool AutoApplyOnClose { get; set; } = false;

        // ---- Indicators ----
        public bool IsPlaying => this.Audio.PlaybackState == PlaybackState.Playing;
        public bool IsPaused => this.Audio.PlaybackState == PlaybackState.Paused;
        public bool IsStopped => this.Audio.PlaybackState == PlaybackState.Stopped;


        // ---- interne Felder ----
        private readonly Timer _waveTimer = new();
        private bool _renderInProgress;
        private long _viewOffsetSamples;     // linke Kante in Samples (interleaved)
        private long _maxOffsetSamples;      // max. scrollbarer Offset
        private bool _timerInitialized;
        private readonly Color _pauseDefaultForeColor;
        private PlaybackState _lastPlaybackState;
        private PlaybackState _uiPlaybackState;
        private CancellationTokenSource? _renderCts;
        private long _pendingStartSample;
        private static readonly BindingList<AudioObj> StaticClipboard = [];
        private const float DefaultCaretPosition = 0.5f;
        private bool _isSelecting;
        private Point _mouseDownPoint;
        private long _mouseDownSample;
        private const int ClickMoveTolerance = 3;

        public TrackView(AudioObj audio, AudioCollection? audioCollection = null)
        {
            this.InitializeComponent();
            WindowMain.OpenTrackViews.Add(this);

            this.Audio = audio.Clone();
            this.SourceAudioCollection = audioCollection;

            this._pauseDefaultForeColor = this.button_pause.ForeColor;
            this.Audio.PropertyChanged += this.Audio_PropertyChanged;
            this._lastPlaybackState = this.Audio.PlaybackState;
            this._uiPlaybackState = this.Audio.PlaybackState;
            this._pendingStartSample = 0;

            // Grundlayout / Größenlimits
            this.Width = InitialWidth;
            this.MinimumSize = new Size(MinimumWidth, this.Height);
            this.MaximumSize = new Size(MaximumWidth, int.MaxValue);

            // Anchor für dynamisches Width-Resizing
            this.pictureBox_waveform.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            this.hScrollBar_offset.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;

            // Scrollen + Zoom via Wheel
            this.pictureBox_waveform.MouseWheel += this.PictureBox_waveform_MouseWheel;
            this.pictureBox_waveform.MouseDown += this.PictureBox_waveform_MouseDown;
            this.pictureBox_waveform.MouseMove += this.PictureBox_waveform_MouseMove;
            this.pictureBox_waveform.MouseUp += this.PictureBox_waveform_MouseUp;
            this.pictureBox_waveform.MouseEnter += (_, _) => this.pictureBox_waveform.Focus();
            this.MouseWheel += this.TrackView_MouseWheel;

            // Initial scroll / Timer
            this.InitializeScrolling();
            this.InitializeTimer(true);

            // Position & Anzeigen
            this.PositionAndShowSelf();
            this.RenameForm();

            // Auf Resize reagiert Scrolling (sichtbarer Bereich ändert sich)
            this.Resize += (_, _) => this.InitializeScrolling();

            this.FormClosing += (s, e) =>
            {
                WindowMain.OpenTrackViews.Remove(this);
                if (AutoApplyOnClose)
                {
                    if (this.SourceAudioCollection == null)
                    {
                        _ = new AudioCollectionView([this.Audio]);
                    }
                    else
                    {
                        this.SourceAudioCollection.Update(this.Audio, false);
                    }
                }
                else
                {
                    this.Audio.Dispose();
                }

                ReflowAllTrackViews();
            };
        }


        // Positionierung: Grid-Verhalten
        internal void PositionAndShowSelf()
        {
            ReflowAllTrackViews();
        }

        internal static void ReflowAllTrackViews()
        {
            // Main-Fenster als Referenz
            var main = WindowMain.Instance;
            if (main == null)
            {
                return;
            }

            var views = WindowMain.OpenTrackViews
                .Where(v => v != null && !v.IsDisposed)
                .OrderBy(v => v.CreatedAt)
                .ToList();


            if (views.Count == 0)
            {
                return;
            }

            var screen = Screen.FromControl(main);
            var wa = screen.WorkingArea;

            int margin = 8;
            int spacing = 4;

            int x = wa.Left + margin;
            int y = wa.Top + margin;
            int columnMaxRight = x;

            foreach (var tv in views)
            {
                // Neue Spalte, wenn unten kein Platz für dieses Fenster
                if (y + tv.Height > wa.Bottom - margin)
                {
                    x = columnMaxRight + spacing;
                    y = wa.Top + margin;
                    columnMaxRight = x;
                }

                tv.StartPosition = FormStartPosition.Manual;
                tv.Location = new Point(x, y);
                if (!tv.Visible)
                {
                    tv.Show(main);
                }

                y += tv.Height + spacing;

                int right = tv.Right;
                if (right > columnMaxRight)
                {
                    columnMaxRight = right;
                }
            }
        }



        // Timer für Waveform + Timestamp
        internal void InitializeTimer(bool autoGetInterval = true)
        {
            if (this._timerInitialized)
            {
                return;
            }

            float fps = autoGetInterval
                ? WindowsScreenHelper.GetScreenRefreshRate(this) ?? 48f
                : 60f;

            int interval = (int) (1000f / fps);
            if (interval < 15)
            {
                interval = 15;
            }

            this._waveTimer.Interval = interval;
            this._waveTimer.Tick += this.WaveTimer_Tick;
            this._waveTimer.Start();

            this._timerInitialized = true;
        }

        private async void WaveTimer_Tick(object? sender, EventArgs e)
        {
            // Laufenden State und Position abholen (immer, auch wenn Rendering beschäftigt)
            this._uiPlaybackState = this.Audio.PlaybackState;
            long playSample = this.Audio.PlaybackPositionSamples;

            // Ende erkannt: Engine liefert keine Samples mehr
            if (this._uiPlaybackState == PlaybackState.Playing && this.Audio.LengthSamples > 0 && playSample >= this.Audio.LengthSamples)
            {
                await this.Audio.StopAsync();
                this._uiPlaybackState = PlaybackState.Stopped;
                playSample = 0;
                this.SetOffsetSamples(0);
            }

            this.textBox_timestamp.Text = this.Audio.CurrentPlaybackTimestamp.ToString(@"h\:mm\:ss\.fff");
            this.UpdatePlaybackUiState();

            if (this.pictureBox_waveform.Width <= 0 || this.pictureBox_waveform.Height <= 0)
            {
                return;
            }

            if (this._uiPlaybackState == PlaybackState.Playing)
            {
                this.UpdateAutoScrollForPlayback(playSample);
            }

            // Wenn Engine den State noch nicht gesetzt hat, aber Position > 0, erzwinge Playing für UI
            if (this._uiPlaybackState != PlaybackState.Playing && playSample > 0)
            {
                this._uiPlaybackState = PlaybackState.Playing;
                this.UpdatePlaybackUiState();
            }

            if (this._renderInProgress)
            {
                return;
            }

            this._renderInProgress = true;
            this._renderCts?.Cancel();
            this._renderCts?.Dispose();
            var cts = new CancellationTokenSource();
            this._renderCts = cts;
            try
            {
                // Sichtbarer Bereich hängt vom Scroll-Offset ab
                long offset = this._viewOffsetSamples;
                float caretPos = this.CalculateCaretNormalized(offset, playSample);
                var bmp = await this.Audio.RenderWaveformAsync(
                    width: this.pictureBox_waveform.Width,
                    height: this.pictureBox_waveform.Height,
                    samplesPerPixel: this.SamplesPerPixel,
                    offsetSamples: offset,
                    separateChannels: false,
                    backColor: this.pictureBox_waveform.BackColor,
                    graphColor: Color.Black,
                    caretPosition: caretPos,
                    caretWidth: 2,
                    timeMarkerIntervalSeconds: 0.0,
                    selectionColor: null,
                    selectionAlpha: 0.33f,
                    ct: cts.Token);

                var old = this.pictureBox_waveform.Image;
                this.pictureBox_waveform.Image = bmp;
                old?.Dispose();
            }
            catch (OperationCanceledException)
            {
                // ignore
            }
            finally
            {
                this._renderInProgress = false;
            }
        }


        // Scroll / Zoom
        internal void InitializeScrolling()
        {
            if (this.Audio.SampleRate <= 0 || this.Audio.Channels <= 0)
            {
                return;
            }

            int width = this.pictureBox_waveform.Width;
            if (width <= 0)
            {
                width = 1;
            }

            long totalSamples = this.Audio.LengthSamples;
            long visibleSamples = (long) width * this.SamplesPerPixel * this.Audio.Channels;
            this._maxOffsetSamples = Math.Max(0, totalSamples - visibleSamples);

            this.hScrollBar_offset.Minimum = 0;
            this.hScrollBar_offset.Maximum = 1000;
            this.hScrollBar_offset.LargeChange = 100;
            this.hScrollBar_offset.SmallChange = 10;

            this.SetOffsetSamples(this._viewOffsetSamples);
        }

        private long GetCaretAnchorSamples()
        {
            int width = Math.Max(1, this.pictureBox_waveform.Width - 1);
            long anchorPixels = (long) Math.Round(width * DefaultCaretPosition);
            long sppInterleaved = (long) this.SamplesPerPixel * Math.Max(1, this.Audio.Channels);
            return anchorPixels * sppInterleaved;
        }

        private void UpdateAutoScrollForPlayback(long playSample)
        {
            if (this.Audio.SampleRate <= 0 || this.Audio.Channels <= 0)
            {
                return;
            }

            int width = this.pictureBox_waveform.Width;
            if (width <= 0)
            {
                width = 1;
            }

            long targetOffset = playSample - this.GetCaretAnchorSamples();

            if (targetOffset < 0)
            {
                targetOffset = 0;
            }

            if (targetOffset > this._maxOffsetSamples)
            {
                targetOffset = this._maxOffsetSamples;
            }

            this.SetOffsetSamples(targetOffset);
        }

        private long ScrollValueToOffset(int value)
        {
            if (this._maxOffsetSamples <= 0)
            {
                return 0;
            }

            double t = value / 1000.0;
            return (long) (this._maxOffsetSamples * t);
        }

        private int OffsetToScrollValue(long offset)
        {
            if (this._maxOffsetSamples <= 0)
            {
                return 0;
            }

            double t = offset / (double) this._maxOffsetSamples;
            int v = (int) System.Math.Round(t * 1000.0);
            return System.Math.Clamp(v, this.hScrollBar_offset.Minimum, this.hScrollBar_offset.Maximum);
        }

        private void hScrollBar_offset_Scroll(object sender, ScrollEventArgs e)
        {
            if (this._uiPlaybackState == PlaybackState.Playing)
            {
                return; // während Playback kein manuelles Scrollen
            }

            this._viewOffsetSamples = this.ScrollValueToOffset(this.hScrollBar_offset.Value);
        }

        private void PictureBox_waveform_MouseWheel(object? sender, MouseEventArgs e)
        {
            bool ctrl = (ModifierKeys & Keys.Control) == Keys.Control;

            if (ctrl)
            {
                // Zoom (SamplesPerPixel anpassen)
                int spp = this.SamplesPerPixel;
                int width = Math.Max(1, this.pictureBox_waveform.Width);
                int localX = Math.Clamp(e.X, 0, width - 1);
                long sampleUnderCursor = this._viewOffsetSamples + (long) localX * spp * Math.Max(1, this.Audio.Channels);

                if (e.Delta > 0)
                {
                    // reinzoomen → weniger Samples pro Pixel
                    spp = System.Math.Max(1, spp / 2);
                }
                else
                {
                    // rauszoomen
                    spp = System.Math.Min(16384, spp * 2);
                }

                if (spp != this.SamplesPerPixel)
                {
                    this.SamplesPerPixel = spp;
                    this.InitializeScrolling();

                    // Offset so anpassen, dass der Cursor möglichst auf derselben Stelle im Audio bleibt
                    long desiredOffset = sampleUnderCursor - (long) localX * this.SamplesPerPixel * Math.Max(1, this.Audio.Channels);
                    this.SetOffsetSamples(desiredOffset);
                }
            }
            else
            {
                // Scrollen nur, wenn nicht playing
                if (this.Audio.PlaybackState == PlaybackState.Playing)
                {
                    return;
                }

                if (this._maxOffsetSamples <= 0)
                {
                    return;
                }

                int direction = e.Delta > 0 ? -1 : 1;
                long delta = (long) (this._maxOffsetSamples * 0.1) * direction; // 10% pro Notch

                long newOffset = this._viewOffsetSamples + delta;
                if (newOffset < 0)
                {
                    newOffset = 0;
                }

                if (newOffset > this._maxOffsetSamples)
                {
                    newOffset = this._maxOffsetSamples;
                }

                this.SetOffsetSamples(newOffset);
            }
        }

        private void TrackView_MouseWheel(object? sender, MouseEventArgs e)
        {
            // Form-level Fallback, falls PictureBox das Wheel nicht fokussiert
            var screenPoint = this.PointToScreen(e.Location);
            var localToWave = this.pictureBox_waveform.PointToClient(screenPoint);
            if (this.pictureBox_waveform.ClientRectangle.Contains(localToWave))
            {
                var translated = new MouseEventArgs(e.Button, e.Clicks, localToWave.X, localToWave.Y, e.Delta);
                this.PictureBox_waveform_MouseWheel(this.pictureBox_waveform, translated);
            }
        }

        private async void PictureBox_waveform_MouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right && this._isSelecting)
            {
                this._isSelecting = false;
                this.Audio.SelectionStart = 0;
                this.Audio.SelectionEnd = 0;
                return;
            }

            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            int width = Math.Max(1, this.pictureBox_waveform.Width);
            int localX = Math.Clamp(e.X, 0, width - 1);
            long samplesPerPixel = (long) this.SamplesPerPixel * Math.Max(1, this.Audio.Channels);
            long targetSample = this._viewOffsetSamples + (long) localX * samplesPerPixel;
            targetSample = Math.Clamp(targetSample, 0, Math.Max(0, this.Audio.LengthSamples - 1));

            // start selection, record mouse-down state; do NOT seek or set playback yet
            this._isSelecting = true;
            this._mouseDownPoint = new Point(localX, e.Y);
            this._mouseDownSample = targetSample;
            this.Audio.SelectionStart = targetSample;
            this.Audio.SelectionEnd = targetSample;
        }

        private void PictureBox_waveform_MouseMove(object? sender, MouseEventArgs e)
        {
            if (!this._isSelecting)
            {
                return;
            }

            int width = Math.Max(1, this.pictureBox_waveform.Width);
            int localX = Math.Clamp(e.X, 0, width - 1);
            long spp = (long) this.SamplesPerPixel * Math.Max(1, this.Audio.Channels);
            long sample = this._viewOffsetSamples + (long) localX * spp;
            sample = Math.Clamp(sample, 0, Math.Max(0, this.Audio.LengthSamples - 1));
            this.Audio.SelectionEnd = sample;
        }

        private async void PictureBox_waveform_MouseUp(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                int dx = Math.Abs(e.X - this._mouseDownPoint.X);
                int dy = Math.Abs(e.Y - this._mouseDownPoint.Y);
                bool isClick = dx == 0 && dy == 0; // strictly same point for click

                this._isSelecting = false;

                if (isClick)
                {
                    this._pendingStartSample = this._mouseDownSample;
                    this.Audio.StartingSample = this._mouseDownSample;
                    await this.Audio.SeekAsync(this._mouseDownSample);

                    long desiredOffset = this._mouseDownSample - this.GetCaretAnchorSamples();
                    this.SetOffsetSamples(desiredOffset);

                    if (this.Audio.SampleRate > 0 && this.Audio.Channels > 0)
                    {
                        var ts = TimeSpan.FromSeconds(this._mouseDownSample / (double) (this.Audio.SampleRate * this.Audio.Channels));
                        this.textBox_timestamp.Text = ts.ToString(@"h\:mm\:ss\.fff");
                    }

                    this.UpdatePlaybackUiState();

                    this.Audio.SelectionStart = 0;
                    this.Audio.SelectionEnd = 0;
                }
                else
                {
                    // keep drag selection; do not seek
                }
            }
            else if (e.Button == MouseButtons.Right && this._isSelecting)
            {
                this._isSelecting = false;
                this.Audio.SelectionStart = 0;
                this.Audio.SelectionEnd = 0;
            }
        }


        // Playback Buttons / Volume / Mute
        private async void button_playback_Click(object sender, EventArgs e)
        {
            try
            {
                if (this.Audio.PlaybackState == PlaybackState.Playing)
                {
                    await this.Audio.StopAsync();
                    this.SetUiPlaybackState(PlaybackState.Stopped);
                }
                else if (this.Audio.PlaybackState == PlaybackState.Paused)
                {
                    await this.Audio.ResumeAsync();
                    this.SetUiPlaybackState(PlaybackState.Playing);
                }
                else
                {
                    await this.Audio.PlayAsync(loop: false, startSample: this._pendingStartSample);
                    this.SetUiPlaybackState(PlaybackState.Playing);
                    this._pendingStartSample = 0;
                }
            }
            catch
            {
                // Playback-Fehler ignorieren für jetzt
            }
        }

        private async void button_pause_Click(object sender, EventArgs e)
        {
            try
            {
                if (this.Audio.PlaybackState == PlaybackState.Paused)
                {
                    await this.Audio.ResumeAsync();
                    this.SetUiPlaybackState(PlaybackState.Playing);
                }
                else if (this.Audio.PlaybackState == PlaybackState.Playing)
                {
                    await this.Audio.PauseAsync();
                    this.SetUiPlaybackState(PlaybackState.Paused);
                }
                else
                {
                    await this.Audio.PlayAsync(loop: false, startSample: this._pendingStartSample);
                    this.SetUiPlaybackState(PlaybackState.Playing);
                    this._pendingStartSample = 0;
                }
            }
            catch
            {
                // ignore
            }
        }

        private async void checkBox_sync_CheckedChanged(object sender, EventArgs e)
        {
            // Sync-Logik kommt später, aktuell kein Verhalten notwendig
            await Task.CompletedTask;
        }

        private async void checkBox_mute_CheckedChanged(object sender, EventArgs e)
        {
            this.UpdateVolume();
            await Task.CompletedTask;
        }

        private async void vScrollBar_volume_Scroll(object sender, ScrollEventArgs e)
        {
            this.UpdateVolume();
            await Task.CompletedTask;
        }

        private void UpdateVolume()
        {
            float vol = this.Muted ? 0f : this.Volume;
            this.Audio.Volume = vol;
        }



        // ---- WindowHandle doubleclick rename
        protected override void WndProc(ref Message m)
        {
            const int WM_NCLBUTTONDBLCLK = 0x00A3;
            if (m.Msg == WM_NCLBUTTONDBLCLK)
            {
                try
                {
                    // Dialog auf UI-Thread öffnen
                    this.BeginInvoke(new Action(this.ShowTrackRenameDialog));
                }
                catch { }
                // Standardverhalten (Maximieren) unterdrücken
                return;
            }

            base.WndProc(ref m);
        }

        private void ShowTrackRenameDialog()
        {
            string current = this.Audio.Name;
            // Microsoft.VisualBasic.Interaction.InputBox wird bereits im Projekt genutzt
            string input = Microsoft.VisualBasic.Interaction.InputBox("Enter new name for this track:", "Rename Track", current);
            if (!string.IsNullOrWhiteSpace(input) && input != current)
            {
                this.Audio.Name = input;
                this.RenameForm();
            }
        }

        internal void RenameForm(string? name = null, bool addZoomLevel = true)
        {
            name ??= this.Audio.Name;
            string zoomInfo = addZoomLevel ? $" ({this.SamplesPerPixel} SPP)" : string.Empty;
            this.Text = $"'{name}'{zoomInfo}";
        }

        private void SetOffsetSamples(long offsetSamples)
        {
            if (offsetSamples < 0)
            {
                offsetSamples = 0;
            }

            if (offsetSamples > this._maxOffsetSamples)
            {
                offsetSamples = this._maxOffsetSamples;
            }

            this._viewOffsetSamples = offsetSamples;

            int scrollValue = this.OffsetToScrollValue(offsetSamples);
            if (this.hScrollBar_offset.Value != scrollValue)
            {
                this.hScrollBar_offset.Value = scrollValue;
            }
        }

        private void UpdatePlaybackUiState()
        {
            // Engine-Status
            bool isPlaying = this.Audio.PlaybackState == PlaybackState.Playing;
            bool isPaused = this.Audio.PlaybackState == PlaybackState.Paused;

            // UI-Status hat Vorrang, wenn Engine noch nicht aktualisiert hat
            if (this._uiPlaybackState == PlaybackState.Playing)
            {
                isPlaying = true;
                isPaused = false;
            }
            else if (this._uiPlaybackState == PlaybackState.Paused)
            {
                isPaused = true;
                isPlaying = false;
            }

            this.button_playback.Text = isPlaying
                ? (this.button_playback.Tag as string ?? "■")
                : "▶";

            this.button_pause.ForeColor = isPaused ? Color.DimGray : this._pauseDefaultForeColor;
            this._lastPlaybackState = this.Audio.PlaybackState;
        }

        private void SetUiPlaybackState(PlaybackState state)
        {
            this._uiPlaybackState = state;
            this.UpdatePlaybackUiState();
        }

        private void Audio_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(AudioObj.PlaybackState))
            {
                try
                {
                    this._uiPlaybackState = this.Audio.PlaybackState;
                    if (this.IsHandleCreated)
                    {
                        this.BeginInvoke(new Action(this.UpdatePlaybackUiState));
                    }
                }
                catch
                {
                    // ignore invoke issues during shutdown
                }
            }
        }

        private float CalculateCaretNormalized(long currentOffset, long playSample)
        {
            if (this.Audio.SampleRate <= 0 || this.Audio.Channels <= 0)
            {
                return 0.5f;
            }

            int width = Math.Max(1, this.pictureBox_waveform.Width);
            long visibleSamples = (long) width * this.SamplesPerPixel * this.Audio.Channels;
            if (visibleSamples <= 0)
            {
                return 0.5f;
            }

            long effectiveSample = playSample;

            // Wenn nicht gespielt wird, fällt PlaybackPosition evtl. auf 0 zurück.
            // Nutze dann den zuletzt angeklickten/gewünschten Start-Sample als Caret-Bezug.
            if (effectiveSample <= 0)
            {
                if (this._pendingStartSample > 0)
                {
                    effectiveSample = this._pendingStartSample;
                }
                else if (this.Audio.StartingSample > 0)
                {
                    effectiveSample = this.Audio.StartingSample;
                }
            }

            long relative = effectiveSample - currentOffset;
            double norm = relative / (double) visibleSamples;
            return (float) System.Math.Clamp(norm, 0.0, 1.0);
        }

        private async void copyToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (this.Audio == null)
            {
                return;
            }

            if (this.Audio.SelectionStart <= 0 && this.Audio.SelectionEnd <= 0)
            {
                return;
            }

            // Clear to only keep last copied item
            StaticClipboard.Clear();
            StaticClipboard.Add(await this.Audio.CopyFromSelectionAsync(this.Audio.SelectionStart, this.Audio.SelectionEnd));
        }

        private async Task PasteFromClipboardAsync()
        {
            if (StaticClipboard.Count == 0)
            {
                return;
            }

            bool shiftFlag = (ModifierKeys & Keys.Shift) == Keys.Shift;

            var item = StaticClipboard.Last();
            long insertIndex = this._pendingStartSample > 0 ? this._pendingStartSample : this.Audio.PlaybackPositionSamples;
            await this.Audio.InsertAudioAtAsync(item, insertIndex, !shiftFlag);
            this.InitializeScrolling();
        }

        private async void removeToolStripMenuItem_Click(object sender, EventArgs e)
        {
            await this.Audio.RemoveSelectionAsync();
            this.InitializeScrolling();
        }

        private async void normalizeToolStripMenuItem_Click(object sender, EventArgs e)
        {
            string input = Microsoft.VisualBasic.Interaction.InputBox("Normalize amplitude (0..1):", "Normalize", "0.85");
            if (string.IsNullOrWhiteSpace(input)) return;
            input = input.Trim();
            if (!float.TryParse(input.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var amp)) return;
            amp = Math.Clamp(amp, 0f, 1f);
            await this.Audio.NormalizeAsync(amp);
            this.InitializeScrolling();
        }

        private async void fadeInToolStripMenuItem_Click(object sender, EventArgs e)
        {
            string input = Microsoft.VisualBasic.Interaction.InputBox("Fade in to amplitude (0..1):", "Fade In", "0.0");
            if (string.IsNullOrWhiteSpace(input)) return;
            input = input.Trim();
            if (!float.TryParse(input.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var target)) return;
            target = Math.Clamp(target, 0f, 1f);
            await this.Audio.FadeInAsync(target);
            this.InitializeScrolling();
        }

        private async void fadeOutToolStripMenuItem_Click(object sender, EventArgs e)
        {
            string input = Microsoft.VisualBasic.Interaction.InputBox("Fade out to amplitude (0..1):", "Fade Out", "0.0");
            if (string.IsNullOrWhiteSpace(input)) return;
            input = input.Trim();
            if (!float.TryParse(input.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var target)) return;
            target = Math.Clamp(target, 0f, 1f);
            await this.Audio.FadeOutAsync(target);
            this.InitializeScrolling();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.C))
            {
                this.copyToolStripMenuItem_Click(this, EventArgs.Empty);
                return true;
            }
            if (keyData == (Keys.Control | Keys.V))
            {
                this.PasteFromClipboardAsync().ConfigureAwait(false);
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void pictureBox_waveform_Click(object sender, EventArgs e)
        {
            // If right clicked, show context menu
            if (MouseButtons.Right == Control.MouseButtons)
            {
                this.contextMenuStrip_waveform.Show(Cursor.Position);
            }
        }

        private void drawBeatGridToolStripMenuItem_CheckStateChanged(object sender, EventArgs e)
        {
            // TODO: implement later
        }

        private void v1ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            // TODO: implement later
        }

        private void v2ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            // TODO: implement later
        }

        private void v3ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            // TODO: implement later
        }
    }
}
