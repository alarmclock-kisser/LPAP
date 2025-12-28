using LPAP.Audio;
using LPAP.Cuda;

namespace LPAP.Forms
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            ApplicationConfiguration.Initialize();

            EventHandler? idleHandler = null;
            idleHandler = (_, __) =>
            {
                Application.Idle -= idleHandler!;
                _ = Task.Run(() =>
                {
                    try { NvencVideoRenderer.WriteHardwareInfo_To_LocalStats(); }
                    catch { }
                });
            };
            Application.Idle += idleHandler;

            AudioScheduling.ConfigureProcessForAudio();
            Application.Run(new WindowMain());
        }
    }
}