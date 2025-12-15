// LPAP.Audio/AudioObj.Playback.cs
using System.Threading.Tasks;

namespace LPAP.Audio
{
	public partial class AudioObj
	{
		public float LoopFraction { get; internal set; } = 1.0f;


		public Task PlayAsync(bool loop = false, long? startSample = null)
		{
			// Sofort UI-State aktualisieren, Engine setzt ihn ebenfalls
			this.SetPlaybackState(PlaybackState.Playing);
			if (startSample.HasValue)
			{
				this.StartingSample = Math.Max(0, startSample.Value);
			}
			AudioPlaybackEngine.Instance.Play(this, loop, this.StartingSample);
			return Task.CompletedTask;
		}

		public void Play(bool loop = false, long? startSample = null)
		{
			// Sofort UI-State aktualisieren, Engine setzt ihn ebenfalls
			this.SetPlaybackState(PlaybackState.Playing);
			if (startSample.HasValue)
			{
				this.StartingSample = Math.Max(0, startSample.Value);
			}
			AudioPlaybackEngine.Instance.Play(this, loop, this.StartingSample);
		}

		public Task StopAsync()
		{
			this.SetPlaybackState(PlaybackState.Stopped);
			AudioPlaybackEngine.Instance.Stop(this);
			this.PlaybackTracking = null; // Position zurücksetzen auf 0 beim nächsten Zugriff
			this.StartingSample = 0;
			return Task.CompletedTask;
		}

		public void Stop()
		{
			this.SetPlaybackState(PlaybackState.Stopped);
			AudioPlaybackEngine.Instance.Stop(this);
			this.PlaybackTracking = null; // Position zurücksetzen auf 0 beim nächsten Zugriff
			this.StartingSample = 0;
		}

		public Task PauseAsync()
		{
			this.SetPlaybackState(PlaybackState.Paused);
			AudioPlaybackEngine.Instance.Pause(this);
			return Task.CompletedTask;
		}

		public void Pause()
		{
			this.SetPlaybackState(PlaybackState.Paused);
			AudioPlaybackEngine.Instance.Pause(this);
		}

		public Task ResumeAsync()
		{
			this.SetPlaybackState(PlaybackState.Playing);
			AudioPlaybackEngine.Instance.Resume(this);
			return Task.CompletedTask;
		}

		public void Resume()
		{
			this.SetPlaybackState(PlaybackState.Playing);
			AudioPlaybackEngine.Instance.Resume(this);
		}

		public Task SeekAsync(long samplePosition)
		{
			this.StartingSample = Math.Max(0, samplePosition);
			AudioPlaybackEngine.Instance.Seek(this, samplePosition);
			return Task.CompletedTask;
		}

		public void Seek(long samplePosition)
		{
			this.StartingSample = Math.Max(0, samplePosition);
			AudioPlaybackEngine.Instance.Seek(this, samplePosition);
		}

		public Task SetLoopAsync(long startSample, long endSample, float fraction = 1.0f)
		{
			AudioPlaybackEngine.Instance.SetLoop(this, startSample, endSample, fraction);
			this.LoopFraction = fraction;
			return Task.CompletedTask;
		}

		public void SetLoop(long startSample, long endSample, float fraction = 1.0f)
		{
			AudioPlaybackEngine.Instance.SetLoop(this, startSample, endSample, fraction);
			this.LoopFraction = fraction;
		}

		public Task UpdateLoopFractionAsync(float fraction)
		{
			AudioPlaybackEngine.Instance.UpdateLoopFraction(this, fraction);
			this.LoopFraction = fraction;
			return Task.CompletedTask;
		}

		public void UpdateLoopFraction(float fraction)
		{
			AudioPlaybackEngine.Instance.UpdateLoopFraction(this, fraction);
			this.LoopFraction = fraction;
		}


	}
}
