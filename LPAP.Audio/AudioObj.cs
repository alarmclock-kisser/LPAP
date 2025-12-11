using System;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;

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
        public long StartingSample { get; set; }

        public long LengthSamples => this.Data?.LongLength ?? 0;
        public TimeSpan Duration => this.SampleRate > 0 && this.Channels > 0
            ? TimeSpan.FromSeconds(this.LengthSamples / (double) (this.SampleRate * this.Channels))
            : TimeSpan.Zero;

        public PlaybackState PlaybackState { get; internal set; } = PlaybackState.Stopped;
        public TimeSpan CurrentPlaybackTimestamp => (this.SampleRate > 0 && this.Channels > 0)
        ? TimeSpan.FromSeconds(this.PlaybackPositionSamples / (double) (this.SampleRate * this.Channels))
        : TimeSpan.Zero;

        // --- Selection ---
        private long _selectionStart;
        private long _selectionEnd;
        public long SelectionStart
        {
            get => this._selectionStart;
            set
            {
                long v = Math.Max(0, value);
                if (v == this._selectionStart)
                {
                    return;
                }

                this._selectionStart = v;
                this.OnPropertyChanged(nameof(this.SelectionStart));
            }
        }
        public long SelectionEnd
        {
            get => this._selectionEnd;
            set
            {
                long v = Math.Max(0, value);
                if (v == this._selectionEnd)
                {
                    return;
                }

                this._selectionEnd = v;
                this.OnPropertyChanged(nameof(this.SelectionEnd));
            }
        }

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

        // --- Editing operations ---
        public async Task<AudioObj> CopyFromSelectionAsync(long selectionStart, long selectionEnd)
        {
            return await Task.Run(() =>
            {
                long s0 = Math.Max(0, Math.Min(selectionStart, selectionEnd));
                long s1 = Math.Max(0, Math.Max(selectionStart, selectionEnd));
                s0 = Math.Min(s0, this.LengthSamples);
                s1 = Math.Min(s1, this.LengthSamples);
                long len = Math.Max(0, s1 - s0);

                var copy = new AudioObj
                {
                    Name = this.Name + " (copy)",
                    SampleRate = this.SampleRate,
                    Channels = this.Channels,
                    BitDepth = this.BitDepth,
                    Data = len > 0 ? new float[len] : Array.Empty<float>()
                };

                if (len > 0)
                {
                    Array.Copy(this.Data, s0, copy.Data, 0, len);
                }

                copy.DataChanged();
                return copy;
            });
        }

        public async Task RemoveSelectionAsync(long? selectionStart = null, long? selectionEnd = null)
        {
            await Task.Run(() =>
            {
                long s0 = selectionStart ?? this.SelectionStart;
                long s1 = selectionEnd ?? this.SelectionEnd;
                s0 = Math.Max(0, Math.Min(s0, s1));
                s1 = Math.Max(0, Math.Max(s0, s1));
                s0 = Math.Min(s0, this.LengthSamples);
                s1 = Math.Min(s1, this.LengthSamples);
                long removeLen = Math.Max(0, s1 - s0);
                if (removeLen <= 0 || this.Data.Length == 0)
                {
                    return;
                }

                long newLen = this.LengthSamples - removeLen;
                var newData = new float[newLen];
                // copy before selection
                if (s0 > 0)
                {
                    Array.Copy(this.Data, 0, newData, 0, s0);
                }
                // copy after selection
                long tailLen = this.LengthSamples - s1;
                if (tailLen > 0)
                {
                    Array.Copy(this.Data, s1, newData, s0, tailLen);
                }

                this.Data = newData;
                this.SelectionStart = 0;
                this.SelectionEnd = 0;
                this.DataChanged();
            });
        }

        public async Task InsertAudioAtAsync(AudioObj insertItem, long insertIndex = 0, bool mixInsteadOfInsert = false)
        {
            if (insertItem == null || insertItem.Data == null || insertItem.Data.Length == 0)
            {
                return;
            }

            if (mixInsteadOfInsert)
            {
                await Task.Run(() =>
                {
                    long idx = Math.Clamp(insertIndex, 0, this.LengthSamples);
                    long mixLen = Math.Min(insertItem.LengthSamples, this.LengthSamples - idx);
                    for (long i = 0; i < mixLen; i++)
                    {
                        this.Data[idx + i] += insertItem.Data[i];
                    }
                    this.DataChanged();
                });
                return;
            }

            await Task.Run(() =>
            {
                long idx = Math.Clamp(insertIndex, 0, this.LengthSamples);
                long newLen = this.LengthSamples + insertItem.LengthSamples;
                var newData = new float[newLen];
                // copy head
                if (idx > 0)
                {
                    Array.Copy(this.Data, 0, newData, 0, idx);
                }
                // copy insert
                Array.Copy(insertItem.Data, 0, newData, idx, insertItem.LengthSamples);
                // copy tail
                long tailLen = this.LengthSamples - idx;
                if (tailLen > 0)
                {
                    Array.Copy(this.Data, idx, newData, idx + insertItem.LengthSamples, tailLen);
                }

                this.Data = newData;
                this.DataChanged();
            });
        }

        public async Task ConcatSelfAsync(bool useSelection = false, int iterations = 1)
        {
            iterations = Math.Max(1, iterations);
            await Task.Run(() =>
            {
                long s0 = 0;
                long s1 = this.LengthSamples;
                if (useSelection)
                {
                    s0 = Math.Max(0, Math.Min(this.SelectionStart, this.SelectionEnd));
                    s1 = Math.Max(0, Math.Max(this.SelectionStart, this.SelectionEnd));
                    s0 = Math.Min(s0, this.LengthSamples);
                    s1 = Math.Min(s1, this.LengthSamples);
                }

                long segmentLen = Math.Max(0, s1 - s0);
                if (segmentLen <= 0)
                {
                    return;
                }

                long newLen = this.LengthSamples + segmentLen * iterations;
                var newData = new float[newLen];

                // original
                Array.Copy(this.Data, 0, newData, 0, this.LengthSamples);

                // segment to repeat
                for (int i = 0; i < iterations; i++)
                {
                    Array.Copy(this.Data, s0, newData, this.LengthSamples + i * segmentLen, segmentLen);
                }

                this.Data = newData;
                this.DataChanged();
            });
        }

        public async Task NormalizeAsync(float targetAmplitude, long? selectionStart = null, long? selectionEnd = null)
        {
            targetAmplitude = Math.Clamp(targetAmplitude, 0f, 1f);
            await Task.Run(() =>
            {
                long s0 = selectionStart ?? this.SelectionStart;
                long s1 = selectionEnd ?? this.SelectionEnd;
                s0 = Math.Max(0, Math.Min(s0, s1));
                s1 = Math.Max(0, Math.Max(s0, s1));
                s0 = Math.Min(s0, this.LengthSamples);
                s1 = Math.Min(s1, this.LengthSamples);
                if (s0 == s1)
                {
                    s0 = 0; s1 = this.LengthSamples;
                }

                float maxAbs = 0f;
                for (long i = s0; i < s1; i++)
                {
                    float a = Math.Abs(this.Data[i]);
                    if (a > maxAbs) maxAbs = a;
                }
                if (maxAbs <= 0f) return;

                float scale = targetAmplitude / maxAbs;
                for (long i = s0; i < s1; i++)
                {
                    this.Data[i] *= scale;
                }
            });
            this.DataChanged();
        }

        public async Task FadeInAsync(float targetAmplitude, long? selectionStart = null, long? selectionEnd = null)
        {
            targetAmplitude = Math.Clamp(targetAmplitude, 0f, 1f);
            await Task.Run(() =>
            {
                long s0 = selectionStart ?? this.SelectionStart;
                long s1 = selectionEnd ?? this.SelectionEnd;
                s0 = Math.Max(0, Math.Min(s0, s1));
                s1 = Math.Max(0, Math.Max(s0, s1));
                s0 = Math.Min(s0, this.LengthSamples);
                s1 = Math.Min(s1, this.LengthSamples);
                if (s0 == s1)
                {
                    s0 = 0; s1 = this.LengthSamples;
                }
                long len = Math.Max(1, s1 - s0);
                for (long i = 0; i < len; i++)
                {
                    float t = i / (float) (len - 1);
                    float amp = (1 - t) * 0f + t * targetAmplitude;
                    long idx = s0 + i;
                    this.Data[idx] *= amp;
                }
            });
            this.DataChanged();
        }

        public async Task FadeOutAsync(float targetAmplitude, long? selectionStart = null, long? selectionEnd = null)
        {
            targetAmplitude = Math.Clamp(targetAmplitude, 0f, 1f);
            await Task.Run(() =>
            {
                long s0 = selectionStart ?? this.SelectionStart;
                long s1 = selectionEnd ?? this.SelectionEnd;
                s0 = Math.Max(0, Math.Min(s0, s1));
                s1 = Math.Max(0, Math.Max(s0, s1));
                s0 = Math.Min(s0, this.LengthSamples);
                s1 = Math.Min(s1, this.LengthSamples);
                if (s0 == s1)
                {
                    s0 = 0; s1 = this.LengthSamples;
                }
                long len = Math.Max(1, s1 - s0);
                for (long i = 0; i < len; i++)
                {
                    float t = i / (float) (len - 1);
                    float amp = (1 - t) * targetAmplitude + t * 0f;
                    long idx = s0 + i;
                    this.Data[idx] *= amp;
                }
            });
            this.DataChanged();
        }
    }
}
