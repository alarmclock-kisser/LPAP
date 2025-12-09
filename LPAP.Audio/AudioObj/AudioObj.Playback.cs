// LPAP.Audio/AudioObj.Playback.cs
using System.Threading.Tasks;

namespace LPAP.Audio
{
    public partial class AudioObj
    {
        public Task PlayAsync(bool loop = false)
        {
            AudioPlaybackEngine.Instance.Play(this, loop);
            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            AudioPlaybackEngine.Instance.Stop(this);
            return Task.CompletedTask;
        }

        public Task PauseAsync()
        {
            AudioPlaybackEngine.Instance.Pause(this);
            return Task.CompletedTask;
        }

        public Task ResumeAsync()
        {
            AudioPlaybackEngine.Instance.Resume(this);
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
