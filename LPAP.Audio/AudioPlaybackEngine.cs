using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System;
using System.Collections.Concurrent;
using System.Threading;

namespace LPAP.Audio
{
	internal sealed class AudioPlaybackEngine : IDisposable
	{
        private sealed class TrackHandle
        {
            public LoopingRegionSampleProvider Looping { get; }
            public VolumeSampleProvider VolumeProvider { get; }
            public PositionTrackingSampleProvider Tracking { get; }
            public PausableSampleProvider Pausable { get; }

            // This is the provider right after pause (pre channel-conv + resample)
            public ISampleProvider BaseProvider { get; }

            // This one gets added to the mixer and can be swapped when output format changes
            public ISampleProvider MixerInput { get; set; }

            public TrackHandle(
                LoopingRegionSampleProvider looping,
                VolumeSampleProvider volumeProvider,
                PositionTrackingSampleProvider tracking,
                PausableSampleProvider pausable,
                ISampleProvider baseProvider,
                ISampleProvider mixerInput)
            {
                this.Looping = looping;
                this.VolumeProvider = volumeProvider;
                this.Tracking = tracking;
                this.Pausable = pausable;
                this.BaseProvider = baseProvider;
                this.MixerInput = mixerInput;
            }
        }


        private static readonly Lazy<AudioPlaybackEngine> _instance =
			new(() => new AudioPlaybackEngine());

		public static AudioPlaybackEngine Instance => _instance.Value;

        private WaveOutEvent _outputDevice;
        private MixingSampleProvider _mixer;

        private readonly object _engineLock = new();


        private readonly ConcurrentDictionary<Guid, TrackHandle> _tracks = new();

		private AudioPlaybackEngine(int sampleRate = 48000, int channels = 2)
		{
			AudioScheduling.ConfigureProcessForAudio();

			var format = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);
			this._mixer = new MixingSampleProvider(format)
			{
				ReadFully = true
			};

			this._outputDevice = new WaveOutEvent
			{
				DesiredLatency = 80
			};
			this._outputDevice.Init(this._mixer);
			this._outputDevice.Play();
		}

		public void Play(AudioObj audio, bool loop = false, long startSample = 0)
		{
			if (audio.Data == null || audio.Data.Length == 0)
			{
				return;
			}

			this.Stop(audio);

			var format = WaveFormat.CreateIeeeFloatWaveFormat(audio.SampleRate, audio.Channels);

			// 1) Looping-Provider über dem Roh-Array
			var looping = new LoopingRegionSampleProvider(audio.Data, format);

			if (loop)
			{
				// Standardloop: kompletter Bereich
				looping.SetLoop(0, audio.Data.LongLength, 1.0f);
			}

			// 2) Volume pro Track
			var volumeProvider = new VolumeSampleProvider(looping)
			{
				Volume = Math.Clamp(audio.Volume, 0f, 1f)
			};

			// 3) Positiontracking (vor Resampling, damit SamplesRead auf Originalrate basiert)
			var tracking = new PositionTrackingSampleProvider(volumeProvider);

            // 4) Pausieren
            var pausable = new PausableSampleProvider(tracking);

            // base provider is the graph up to pause (kept stable across output rebuild)
            ISampleProvider baseProvider = pausable;

            // tail provider depends on mixer format (can be rebuilt)
            ISampleProvider pipeline = this.BuildMixerInputForCurrentOutput(baseProvider);

            var handle = new TrackHandle(looping, volumeProvider, tracking, pausable, baseProvider, pipeline);


            if (this._tracks.TryAdd(audio.Id, handle))
			{
				if (startSample > 0)
				{
					looping.Seek(startSample);
					tracking.SetSamplePosition(startSample);
				}

				this._mixer.AddMixerInput(pipeline);
				audio.AttachPlaybackTracking(tracking);
				audio.SetPlaybackState(PlaybackState.Playing);
			}
		}

		public void Stop(AudioObj audio)
		{
			if (this._tracks.TryRemove(audio.Id, out var handle))
			{
				this._mixer.RemoveMixerInput(handle.MixerInput);
				audio.DetachPlaybackTracking(handle.Tracking);
				audio.SetPlaybackState(PlaybackState.Stopped);
			}
		}

		public void Pause(AudioObj audio)
		{
			if (this._tracks.TryGetValue(audio.Id, out var handle))
			{
				handle.Pausable.IsPaused = true;
				audio.SetPlaybackState(PlaybackState.Paused);
			}
		}

		public void Resume(AudioObj audio)
		{
			if (this._tracks.TryGetValue(audio.Id, out var handle))
			{
				handle.Pausable.IsPaused = false;
				audio.SetPlaybackState(PlaybackState.Playing);
			}
		}

		public void Seek(AudioObj audio, long samplePosition)
		{
			if (this._tracks.TryGetValue(audio.Id, out var handle))
			{
				handle.Looping.Seek(samplePosition);
				handle.Tracking.SetSamplePosition(samplePosition);
			}
			else
			{
				// Wenn nicht aktiv spielend, nur Position merken
				audio.SetPlaybackState(PlaybackState.Stopped);
			}
		}

		public void SetVolume(AudioObj audio, float volume)
		{
			if (this._tracks.TryGetValue(audio.Id, out var handle))
			{
				handle.VolumeProvider.Volume = Math.Clamp(volume, 0f, 1f);
			}
		}

		public void SetLoop(AudioObj audio, long startSample, long endSample, float fraction)
		{
			if (this._tracks.TryGetValue(audio.Id, out var handle))
			{
				handle.Looping.SetLoop(startSample, endSample, fraction);
			}
		}

		public void UpdateLoopFraction(AudioObj audio, float fraction)
		{
			if (this._tracks.TryGetValue(audio.Id, out var handle))
			{
				handle.Looping.UpdateLoopFraction(fraction);
			}
		}

        public void SetOutputSampleRate(int newSampleRate)
        {
            newSampleRate = Math.Clamp(newSampleRate, 8000, 192000);

            lock (this._engineLock)
            {
                if (this._mixer.WaveFormat.SampleRate == newSampleRate)
                    return;

                // Snapshot active tracks
                var active = this._tracks.Values.ToList();

                // Remove old mixer inputs first (safe)
                foreach (var h in active)
                {
                    try { this._mixer.RemoveMixerInput(h.MixerInput); } catch { }
                }

                // Rebuild mixer + output device
                int channels = this._mixer.WaveFormat.Channels;
                var newFormat = WaveFormat.CreateIeeeFloatWaveFormat(newSampleRate, channels);

                try { this._outputDevice?.Stop(); } catch { }
                try { this._outputDevice?.Dispose(); } catch { }

                this._mixer = new MixingSampleProvider(newFormat) { ReadFully = true };

                this._outputDevice = new WaveOutEvent { DesiredLatency = 80 };
                this._outputDevice.Init(this._mixer);
                this._outputDevice.Play();

                // Rebuild each track tail (channel conv + resampler) to match new mixer format
                foreach (var h in active)
                {
                    try
                    {
                        var newInput = this.BuildMixerInputForCurrentOutput(h.BaseProvider);
                        h.MixerInput = newInput;
                        this._mixer.AddMixerInput(newInput);
                    }
                    catch { }
                }
            }
        }


        private ISampleProvider BuildMixerInputForCurrentOutput(ISampleProvider baseProvider)
        {
            ISampleProvider pipeline = baseProvider;

            // Channel adaptation to current mixer format
            if (pipeline.WaveFormat.Channels == 1 && this._mixer.WaveFormat.Channels == 2)
                pipeline = new MonoToStereoSampleProvider(pipeline);
            else if (pipeline.WaveFormat.Channels == 2 && this._mixer.WaveFormat.Channels == 1)
                pipeline = new StereoToMonoSampleProvider(pipeline);

            // Resample to current mixer rate
            if (pipeline.WaveFormat.SampleRate != this._mixer.WaveFormat.SampleRate)
                pipeline = new WdlResamplingSampleProvider(pipeline, this._mixer.WaveFormat.SampleRate);

            return pipeline;
        }


        public void Dispose()
		{
			this._outputDevice?.Stop();
			this._outputDevice?.Dispose();
		}
	}

	internal sealed class PositionTrackingSampleProvider : ISampleProvider
	{
		private readonly ISampleProvider _source;
		private long _samplesRead;

		public PositionTrackingSampleProvider(ISampleProvider source)
		{
			this._source = source ?? throw new ArgumentNullException(nameof(source));
		}

		public WaveFormat WaveFormat => this._source.WaveFormat;

		public long SamplesRead => Interlocked.Read(ref this._samplesRead);

		public void SetSamplePosition(long samples)
		{
			Interlocked.Exchange(ref this._samplesRead, Math.Max(0, samples));
		}

		public int Read(float[] buffer, int offset, int count)
		{
			int read = this._source.Read(buffer, offset, count);
			if (read > 0)
			{
				Interlocked.Add(ref this._samplesRead, read);
			}
			return read;
		}
	}

	internal sealed class ArraySampleProvider : ISampleProvider
	{
		private readonly float[] _data;
		private readonly WaveFormat _waveFormat;
		private long _position; // samples (interleaved)

		public ArraySampleProvider(float[] data, WaveFormat waveFormat, long startSample = 0)
		{
			this._data = data ?? throw new ArgumentNullException(nameof(data));
			this._waveFormat = waveFormat ?? throw new ArgumentNullException(nameof(waveFormat));
			this._position = Math.Clamp(startSample, 0, this._data.Length);
		}

		public WaveFormat WaveFormat => this._waveFormat;

		public int Read(float[] buffer, int offset, int count)
		{
			if (this._position >= this._data.Length)
			{
				return 0;
			}

			int available = (int) Math.Min(count, this._data.Length - this._position);
			Array.Copy(this._data, this._position, buffer, offset, available);
			this._position += available;
			return available;
		}
	}

	internal sealed class PausableSampleProvider : ISampleProvider
	{
		private readonly ISampleProvider _source;
		private volatile bool _isPaused;

		public PausableSampleProvider(ISampleProvider source)
		{
			this._source = source ?? throw new ArgumentNullException(nameof(source));
		}

		public bool IsPaused
		{
			get => this._isPaused;
			set => this._isPaused = value;
		}

		public WaveFormat WaveFormat => this._source.WaveFormat;

		public int Read(float[] buffer, int offset, int count)
		{
			if (this._isPaused)
			{
				// Liefere Stille, aber bleibe im Mixer registriert
				Array.Clear(buffer, offset, count);
				return count;
			}

			return this._source.Read(buffer, offset, count);
		}
	}

	internal sealed class LoopingRegionSampleProvider : ISampleProvider
	{
		private readonly float[] _data;
		private readonly WaveFormat _waveFormat;
		private readonly Lock _lock = new();

		private long _baseStart;     // Basis-Start (aus erstem SetLoop)
		private long _baseEnd;       // Basis-Ende (exklusiv)
		private long _start;         // aktueller Start
		private long _end;           // aktuelles Ende (exklusiv)
		private float _fraction = 1.0f;

		private long _position;      // aktueller Sample-Index (interleaved)
		private bool _loopEnabled;

		public LoopingRegionSampleProvider(float[] data, WaveFormat waveFormat)
		{
			this._data = data ?? throw new ArgumentNullException(nameof(data));
			this._waveFormat = waveFormat ?? throw new ArgumentNullException(nameof(waveFormat));

			this._baseStart = 0;
			this._baseEnd = this._data.LongLength;
			this._start = this._baseStart;
			this._end = this._baseEnd;
			this._position = this._start;
			this._loopEnabled = false;
		}

		public WaveFormat WaveFormat => this._waveFormat;

		public void Seek(long samplePosition)
		{
			lock (this._lock)
			{
				long clamped = Math.Clamp(samplePosition, 0, Math.Max(0, this._data.LongLength - 1));
				this._position = clamped;
			}
		}

		public int Read(float[] buffer, int offset, int count)
		{
			if (this._data == null || this._data.Length == 0 || count <= 0)
			{
				return 0;
			}

			int totalRead = 0;

			lock (this._lock)
			{
				while (totalRead < count)
				{
					if (this._position >= this._data.LongLength)
					{
						// Wenn kein Loop aktiv → stream zu Ende
						if (!this._loopEnabled)
						{
							break;
						}

						// bei Loop: immer in den Bereich zurückspringen
						this._position = this._start;
					}

					long regionStart = this._loopEnabled ? this._start : 0;
					long regionEnd = this._loopEnabled ? this._end : this._data.LongLength;

					if (this._position < regionStart || this._position >= regionEnd)
					{
						if (!this._loopEnabled)
						{
							// Stream zu Ende
							break;
						}

						// Bei aktivem Loop: zurück an Start
						this._position = regionStart;
					}

					long availableInRegion = regionEnd - this._position;
					if (availableInRegion <= 0)
					{
						if (this._loopEnabled)
						{
							this._position = regionStart;
							continue;
						}
						break;
					}

					int samplesToCopy = (int) Math.Min(count - totalRead, availableInRegion);

					Array.Copy(this._data, this._position, buffer, offset + totalRead, samplesToCopy);
					this._position += samplesToCopy;
					totalRead += samplesToCopy;

					if (this._loopEnabled && this._position >= regionEnd)
					{
						this._position = regionStart;
					}
				}
			}

			return totalRead;
		}

		public void SetLoop(long startSample, long endSample, float fraction)
		{
			lock (this._lock)
			{
				long length = this._data.LongLength;
				if (length <= 0)
				{
					this._loopEnabled = false;
					return;
				}

				// Clamp Basis-Range
				this._baseStart = Math.Clamp(startSample, 0, length - 1);
				this._baseEnd = Math.Clamp(endSample, this._baseStart + 1, length);

				this.ApplyFractionInternal(fraction, repositionIfOutOfRange: true);
			}
		}

		public void UpdateLoopFraction(float fraction)
		{
			lock (this._lock)
			{
				if (!this._loopEnabled)
				{
					// falls noch keine Basis gesetzt ist, nehmen wir die aktuelle Range als Basis
					this._baseStart = this._start;
					this._baseEnd = this._end > this._start ? this._end : this._data.LongLength;
				}

				this.ApplyFractionInternal(fraction, repositionIfOutOfRange: true);
			}
		}

		private void ApplyFractionInternal(float fraction, bool repositionIfOutOfRange)
		{
			this._fraction = fraction;
			this._loopEnabled = true;

			long length = this._data.LongLength;
			if (length <= 0)
			{
				return;
			}

			long baseLen = Math.Max(1, this._baseEnd - this._baseStart);
			float f = fraction;

			if (Math.Abs(f) < 1e-6f)
			{
				f = 1.0f;
			}

			long newStart;
			long newEnd;

			if (f >= 0.0f)
			{
				long newLen = (long) Math.Max(1, Math.Round(baseLen * f));
				newStart = this._baseStart;
				newEnd = newStart + newLen;
				if (newEnd > length)
				{
					newEnd = length;
				}
			}
			else
			{
				float fa = -f;
				long newLen = (long) Math.Max(1, Math.Round(baseLen * fa));
				newEnd = this._baseEnd;
				newStart = newEnd - newLen;
				if (newStart < 0)
				{
					newStart = 0;
				}
			}

			this._start = Math.Clamp(newStart, 0, length - 1);
			this._end = Math.Clamp(newEnd, this._start + 1, length);

			if (repositionIfOutOfRange)
			{
				if (this._position < this._start || this._position >= this._end)
				{
					this._position = this._start;
				}
			}
		}


	}

}
