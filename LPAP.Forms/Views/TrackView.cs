using LPAP.Audio;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
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

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        internal int SamplesPerPixel { get; set; } = 256;

        public static int InitialWidth { get; set; } = 600;
        public static int MinimumWidth { get; set; } = 200;
        public static int MaximumWidth { get; set; } = 4096;
        public static bool AutoApplyOnClose { get; set; } = false;

        // ---- interne Felder ----
        private readonly Timer _waveTimer = new();
        private bool _renderInProgress;
        private long _viewOffsetSamples;     // linke Kante in Samples (interleaved)
        private long _maxOffsetSamples;      // max. scrollbarer Offset
        private bool _timerInitialized;

        public TrackView(AudioObj audio, AudioCollection? audioCollection = null)
        {
            this.InitializeComponent();
            WindowMain.OpenTrackViews.Add(this);

            this.Audio = audio.Clone();
            this.SourceAudioCollection = audioCollection;

            // Grundlayout / Größenlimits
            this.Width = InitialWidth;
            this.MinimumSize = new Size(MinimumWidth, this.Height);
            this.MaximumSize = new Size(MaximumWidth, int.MaxValue);

            // Anchor für dynamisches Width-Resizing
            this.pictureBox_waveform.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            this.hScrollBar_offset.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;

            // Scrollen + Zoom via Wheel
            this.pictureBox_waveform.MouseWheel += this.PictureBox_waveform_MouseWheel;
            this.pictureBox_waveform.MouseEnter += (_, _) => this.pictureBox_waveform.Focus();

            // Initial scroll / Timer
            this.InitializeScrolling();
            this.InitializeTimer(true);

            // Position & Anzeigen
            this.PositionAndShowSelf();

            // Auf Resize reagiert Scrolling (sichtbarer Bereich ändert sich)
            this.Resize += (_, _) => this.InitializeScrolling();

            this.FormClosing += (s, e) =>
            {
                WindowMain.OpenTrackViews.Remove(this);
                if (AutoApplyOnClose)
                {
                    if (this.SourceAudioCollection == null)
                    {
                        _ = new AudioCollectionView(new[] { this.Audio });
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
            };
        }


        // Positionierung: Grid-Verhalten
        internal void PositionAndShowSelf()
        {
            var screen = Screen.FromControl(this);
            var wa = screen.WorkingArea;
            int margin = 8;
            int spacing = 4;

            var others = WindowMain.OpenTrackViews
                .Where(tv => tv != this && !tv.IsDisposed)
                .OrderBy(tv => tv.CreatedAt)
                .ToList();

            this.StartPosition = FormStartPosition.Manual;

            if (others.Count == 0)
            {
                // Erste TrackView: ganz oben links
                this.Location = new Point(wa.Left + margin, wa.Top + margin);
            }
            else
            {
                var last = others.Last();

                int newX = last.Left;
                int newY = last.Bottom + spacing;

                // Passt nicht mehr nach unten → neue "Spalte" rechts
                if (newY + this.Height > wa.Bottom - margin)
                {
                    newX = last.Right + spacing;
                    newY = wa.Top + margin;
                }

                this.Location = new Point(newX, newY);
            }

            this.Show();
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
            if (this._renderInProgress)
            {
                return;
            }

            this._renderInProgress = true;
            try
            {
                // Timestamp aktualisieren
                this.textBox_timestamp.Text = this.Audio.CurrentPlaybackTimestamp.ToString(@"h\:mm\:ss\.fff");

                // Button-Text je nach PlaybackState
                this.button_playback.Text = this.Audio.PlaybackState == PlaybackState.Playing
                    ? (this.button_playback.Tag as string ?? "■")
                    : "▶";

                if (this.pictureBox_waveform.Width <= 0 || this.pictureBox_waveform.Height <= 0)
                {
                    return;
                }

                if (this.Audio.PlaybackState == PlaybackState.Playing)
                {
                    this.UpdateAutoScrollForPlayback();
                }

                // Sichtbarer Bereich hängt vom Scroll-Offset ab
                long offset = this._viewOffsetSamples;
                var bmp = await this.Audio.RenderWaveformAsync(
                    width: this.pictureBox_waveform.Width,
                    height: this.pictureBox_waveform.Height,
                    samplesPerPixel: this.SamplesPerPixel,
                    offsetSamples: offset,
                    separateChannels: false,
                    backColor: this.pictureBox_waveform.BackColor,
                    graphColor: Color.Black,
                    caretPosition: 0.5f,
                    caretWidth: 1,
                    timeMarkerIntervalSeconds: 0.0);

                var old = this.pictureBox_waveform.Image;
                this.pictureBox_waveform.Image = bmp;
                old?.Dispose();
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
            long overheadSamples = 5L * this.Audio.SampleRate * this.Audio.Channels;

            long visibleSamples = (long) width * this.SamplesPerPixel * this.Audio.Channels;
            this._maxOffsetSamples = Math.Max(0, totalSamples + overheadSamples - visibleSamples);

            this.hScrollBar_offset.Minimum = 0;
            this.hScrollBar_offset.Maximum = 1000;
            this.hScrollBar_offset.LargeChange = 100;
            this.hScrollBar_offset.SmallChange = 10;
            this.hScrollBar_offset.Value = 0;

            this._viewOffsetSamples = 0;
        }

        private void UpdateAutoScrollForPlayback()
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

            // Wie viele Samples sieht man aktuell horizontal?
            long visibleSamples = (long) width * this.SamplesPerPixel * this.Audio.Channels;

            if (visibleSamples <= 0)
            {
                return;
            }

            long playSample = this.Audio.PlaybackPositionSamples;

            // Wir wollen die Wiedergabeposition ungefähr in der Mitte des Views haben
            long targetOffset = playSample - visibleSamples / 2;

            if (targetOffset < 0)
            {
                targetOffset = 0;
            }

            if (targetOffset > this._maxOffsetSamples)
            {
                targetOffset = this._maxOffsetSamples;
            }

            this._viewOffsetSamples = targetOffset;

            // Scrollbar-Value anpassen – Scroll-Handler prüft "if Playing → return",
            // damit unser _viewOffsetSamples NICHT überschrieben wird.
            int scrollValue = this.OffsetToScrollValue(targetOffset);
            if (this.hScrollBar_offset.Value != scrollValue)
            {
                this.hScrollBar_offset.Value = scrollValue;
            }
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
            if (this.Audio.PlaybackState == PlaybackState.Playing)
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

                this._viewOffsetSamples = newOffset;
                this.hScrollBar_offset.Value = this.OffsetToScrollValue(newOffset);
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
                    this.button_playback.Text = "▶";
                }
                else
                {
                    await this.Audio.PlayAsync(loop: false);
                    this.button_playback.Text = this.button_playback.Tag as string ?? "■";
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
                    this.button_pause.ForeColor = SystemColors.ControlText;
                }
                else if (this.Audio.PlaybackState == PlaybackState.Playing)
                {
                    await this.Audio.PauseAsync();
                    // etwas "heller" / anders markieren
                    this.button_pause.ForeColor = Color.DimGray;
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
    }
}
