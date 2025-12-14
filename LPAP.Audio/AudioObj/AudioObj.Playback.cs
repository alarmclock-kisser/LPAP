// LPAP.Audio/AudioObj.Playback.cs
using System.Threading.Tasks;

namespace LPAP.Audio
{
	public partial class AudioObj
	{
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

		public Task StopAsync()
		{
			this.SetPlaybackState(PlaybackState.Stopped);
			AudioPlaybackEngine.Instance.Stop(this);
			this.PlaybackTracking = null; // Position zurücksetzen auf 0 beim nächsten Zugriff
			this.StartingSample = 0;
			return Task.CompletedTask;
		}

		public Task PauseAsync()
		{
			this.SetPlaybackState(PlaybackState.Paused);
			AudioPlaybackEngine.Instance.Pause(this);
			return Task.CompletedTask;
		}

		public Task ResumeAsync()
		{
			this.SetPlaybackState(PlaybackState.Playing);
			AudioPlaybackEngine.Instance.Resume(this);
			return Task.CompletedTask;
		}

		public Task SeekAsync(long samplePosition)
		{
			this.StartingSample = Math.Max(0, samplePosition);
			AudioPlaybackEngine.Instance.Seek(this, samplePosition);
			return Task.CompletedTask;
		}

		public Task SetLoopAsync(long startSample, long endSample, float fraction = 1.0f)
		{
			AudioPlaybackEngine.Instance.SetLoop(this, startSample, endSample, fraction);
			return Task.CompletedTask;
		}

		public Task UpdateLoopFractionAsync(float fraction)
		{
			AudioPlaybackEngine.Instance.UpdateLoopFraction(this, fraction);
			return Task.CompletedTask;
		}


	}
}
