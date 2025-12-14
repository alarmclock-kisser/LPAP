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
		public Int32 OverlapSize { get; internal set; }
		public Double StretchFactor { get; internal set; }
		public Int32 ScannedBeatsPerMinute { get; set; }

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
			GC.SuppressFinalize(this);
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
					Data = len > 0 ? new float[len] : []
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
				if (s1 < s0)
				{
					(s0, s1) = (s1, s0);
				}

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
					if (a > maxAbs)
					{
						maxAbs = a;
					}
				}
				if (maxAbs <= 0f)
				{
					return;
				}

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

		public void CopyAudioObj(AudioObj source)
		{
			this.Name = source.Name;
			this.FilePath = source.FilePath;
			this.SampleRate = source.SampleRate;
			this.Channels = source.Channels;
			this.BitDepth = source.BitDepth;
			this.Data = source.Data != null ? (float[]) source.Data.Clone() : [];
			this.DataChanged();
		}



		internal async Task<IEnumerable<float[]>> GetChunksAsync(int chunkSize = 2048, float overlap = 0.5f, bool keepData = false, int maxWorkers = 0)
		{
			// Validierung ohne Blockieren
			chunkSize = Math.Max(1, chunkSize);
			overlap = Math.Clamp(overlap, 0f, 0.95f); // 0.95 als Obergrenze, um hop > 0 sicherzustellen

			return await Task.Run(() =>
			{
				try
				{
					var source = this.Data ?? [];
					if (source.Length == 0)
					{
						return [];
					}

					// Threadsicher: Arbeite auf lokalem Snapshot
					var data = source.AsSpan();
					int overlapSize = (int) Math.Round(chunkSize * overlap);
					overlapSize = Math.Clamp(overlapSize, 0, chunkSize - 1);
					int hopSize = Math.Max(1, chunkSize - overlapSize);

					var chunks = new List<float[]>();
					// Anzahl Chunks bestimmen
					long totalSamples = data.Length;
					long pos = 0;

					// Optionaler Parallelismus wird hier nicht benötigt – der Vorgang ist IO-frei und linear
					while (pos < totalSamples)
					{
						var chunk = new float[chunkSize];
						long remaining = totalSamples - pos;
						int copyCount = (int) Math.Min(remaining, chunkSize);
						data.Slice((int) pos, copyCount).CopyTo(chunk.AsSpan(0, copyCount));

						chunks.Add(chunk);
						pos += hopSize;
					}

					if (!keepData)
					{
						// Daten nicht behalten: leeren in einem Schritt und Events feuern
						// Threadsicherer Austausch nach kompletter Berechnung
						this.Data = [];
						this.DataChanged();
					}

					// Metadaten aktualisieren, die aus Overlap hergeleitet werden können
					this.OverlapSize = overlapSize;

					return chunks;
				}
				catch (Exception)
				{
					// Best Practice: Fehler isolieren, kein Blockieren. Caller kann mit leerem Resultat umgehen.
					return [];
				}
			}).ConfigureAwait(false);
		}

		internal async Task AggregateStretchedChunksAsync(IEnumerable<float[]> ifftChunks, double stretchFactor = 1.0f, int maxWorkers = 0)
		{
			// Validierung ohne Blockieren
			stretchFactor = Math.Max(0.01, stretchFactor);

			await Task.Run(() =>
			{
				try
				{
					if (ifftChunks == null)
					{
						return;
					}

					var chunks = ifftChunks as IList<float[]> ?? ifftChunks.ToList();
					if (chunks.Count == 0)
					{
						return;
					}

					int chunkSize = chunks[0]?.Length ?? 0;
					if (chunkSize <= 0 || chunks.Any(c => c == null || c.Length != chunkSize))
					{
						// Ungültige Chunk-Längen – defensiv abbrechen
						return;
					}

					// Überlappung bestimmen: Falls nicht gesetzt, nutze 50%
					int overlapSize = this.OverlapSize > 0
						? this.OverlapSize
						: Math.Max(0, (int) Math.Round(chunkSize * 0.5));

					overlapSize = Math.Clamp(overlapSize, 0, chunkSize - 1);

					// Ursprünglicher Hop
					int hopIn = Math.Max(1, chunkSize - overlapSize);

					// Gestretchter Hop
					int hopOut = Math.Max(1, (int) Math.Round(hopIn * stretchFactor));

					// Gesamtlänge des Ausgabesignals berechnen
					long outLen = hopOut * (long) (chunks.Count - 1) + chunkSize;
					var output = new float[outLen];

					// Einfache lineare Crossfade-Fenster über die Überlappung
					// Fenster für Überlappungsbereich vorbereiten
					float[] fadeIn = new float[overlapSize];
					float[] fadeOut = new float[overlapSize];
					if (overlapSize > 0)
					{
						for (int i = 0; i < overlapSize; i++)
						{
							float t = i / (float) overlapSize;
							fadeIn[i] = t;          // steigt 0→1
							fadeOut[i] = 1f - t;    // fällt 1→0
						}
					}

					// Optionaler Parallelismus: Überschreiben vermeiden. Overlap-Add ist additiv, daher Sequenziell sicherer.
					long writePos = 0;
					for (int idx = 0; idx < chunks.Count; idx++)
					{
						var chunk = chunks[idx];
						long basePos = writePos;

						// Nicht-Überlappender Teil
						int dryStart = overlapSize;
						int dryLen = chunkSize - dryStart;
						if (dryLen > 0)
						{
							// Direkt addieren
							for (int i = 0; i < dryLen; i++)
							{
								long p = basePos + dryStart + i;
								if (p >= 0 && p < output.LongLength)
								{
									output[p] += chunk[dryStart + i];
								}
							}
						}

						// Überlappender Teil im Vergleich zum vorherigen Chunk
						if (idx > 0 && overlapSize > 0)
						{
							// Überlappung beginnt am basePos
							for (int i = 0; i < overlapSize; i++)
							{
								long p = basePos + i;
								if (p >= 0 && p < output.LongLength)
								{
									float aPrev = output[p] * fadeOut[i]; // bisherige Summe, ausgeblendet
									float aCur = chunk[i] * fadeIn[i];     // neuer Chunk, eingeblendet
																		   // Da output[p] bereits den vorherigen Chunk enthält, mischen wir neu:
																		   // Rekonstruktion: altes Sample wird mit fadeOut abgesenkt, neues addiert
																		   // Um Doppel-Fading zu vermeiden, subtrahieren wir den ursprünglichen Wert nicht, sondern mischen additiv:
									output[p] = (output[p] * fadeOut[i]) + (chunk[i] * fadeIn[i]);
								}
							}
						}
						else
						{
							// Erster Chunk: schreibe den Anfang bis overlapSize direkt
							for (int i = 0; i < overlapSize; i++)
							{
								long p = basePos + i;
								if (p >= 0 && p < output.LongLength)
								{
									output[p] += chunk[i];
								}
							}
						}

						writePos += hopOut;
					}

					// Threadsichere Ersetzung der Daten
					this.Data = output;
					this.DataChanged();
				}
				catch (Exception)
				{
					// Fehler still behandeln, nicht blockieren
				}
			}).ConfigureAwait(false);
		}



	}
}
