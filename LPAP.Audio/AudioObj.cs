using System;
using System.ComponentModel;
using System.Drawing;

namespace LPAP.Audio
{
    public enum PlaybackState
    {
        Stopped,
        Playing,
        Paused
    }

    public partial class AudioObj : INotifyPropertyChanged, IDisposable
    {
        public Guid Id { get; } = Guid.NewGuid();

        public string Name { get; set; } = string.Empty;
        public string? FilePath { get; private set; }

        public float[] Data { get; internal set; } = [];
        public int SampleRate { get; internal set; }
        public int Channels { get; internal set; }
        public int BitDepth { get; internal set; }

        public long LengthSamples => this.Data?.LongLength ?? 0;
        public TimeSpan Duration => this.SampleRate > 0 && this.Channels > 0
            ? TimeSpan.FromSeconds(this.LengthSamples / (double) (this.SampleRate * this.Channels))
            : TimeSpan.Zero;

        public PlaybackState PlaybackState { get; internal set; } = PlaybackState.Stopped;
        public TimeSpan CurrentPlaybackTimestamp => (this.SampleRate > 0 && this.Channels > 0)
        ? TimeSpan.FromSeconds(this.PlaybackPositionSamples / (double) (this.SampleRate * this.Channels))
        : TimeSpan.Zero;



        private float _volume = 1.0f;
        public float Volume
        {
            get => this._volume;
            set
            {
                var v = Math.Clamp(value, 0f, 1f);
                if (Math.Abs(v - this._volume) < 0.0001f)
                {
                    return;
                }

                this._volume = v;
                this.OnPropertyChanged(nameof(this.Volume));

                // Wenn gerade abgespielt wird, an Engine weitergeben
                AudioPlaybackEngine.Instance.SetVolume(this, v);
            }
        }


        public double BeatsPerMinute { get; set; } = 0.0;
        public int SamplesPerBeat
        {
            get
            {
                if (this.SampleRate <= 0 || this.Channels <= 0)
                {
                    return 0;
                }

                double bpm = this.BeatsPerMinute;
                if (bpm <= 0.0001)
                {
                    bpm = 60.0; // Fallback: 60 BPM
                }

                double secondsPerBeat = 60.0 / bpm;
                double samplesPerBeat = secondsPerBeat * this.SampleRate * this.Channels;

                return (int) Math.Round(samplesPerBeat);
            }
        }

        public string InitialKey { get; set; } = "C";




        public event PropertyChangedEventHandler? PropertyChanged;



        // --- Playback-Status-Output ---

        internal PositionTrackingSampleProvider? PlaybackTracking { get; private set; }

        public long PlaybackPositionSamples =>
            this.PlaybackTracking?.SamplesRead ?? 0;

        public long PlaybackPositionBytes =>
            this.PlaybackPositionSamples * (this.BitDepth / 8);

        public Func<PlaybackState> PlaybackStateGetter => () => this.PlaybackState;

        public Func<long> PlaybackSamplesGetter => () => this.PlaybackPositionSamples;

        public Func<long> PlaybackBytesGetter => () => this.PlaybackPositionBytes;

        public CustomTags CustomTags { get; set; } = new();

        internal void AttachPlaybackTracking(PositionTrackingSampleProvider tracking)
        {
            this.PlaybackTracking = tracking;
            this.PlaybackState = PlaybackState.Playing;
            this.OnPropertyChanged(nameof(this.PlaybackState));
        }

        internal void DetachPlaybackTracking(PositionTrackingSampleProvider tracking)
        {
            if (this.PlaybackTracking == tracking)
            {
                this.PlaybackTracking = null;
                this.PlaybackState = PlaybackState.Stopped;
                this.OnPropertyChanged(nameof(this.PlaybackState));
            }
        }

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public void DataChanged()
        {
            this.OnPropertyChanged(nameof(this.Data));
            this.OnPropertyChanged(nameof(this.LengthSamples));
            this.OnPropertyChanged(nameof(this.Duration));
        }

        internal void SetPlaybackState(PlaybackState state)
        {
            if (this.PlaybackState != state)
            {
                this.PlaybackState = state;
                this.OnPropertyChanged(nameof(this.PlaybackState));
            }
        }

        public void Dispose()
        {
            AudioPlaybackEngine.Instance.Stop(this);
        }
    }
}
