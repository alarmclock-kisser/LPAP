using LPAP.Audio;
using LPAP.Cuda;
namespace LPAP.Forms
{
	internal static class Program
	{
		/// <summary>
		///  The main entry point for the application.
		/// </summary>
		[STAThread]
		static void Main()
		{
			ApplicationConfiguration.Initialize();

            Application.Idle += (_, __) =>
            {
                // run once
                Application.Idle -= (_, __) => { };

                Task.Run(() =>
                {
                    try { NvencVideoRenderer.WriteHardwareInfo_To_LocalStats(); }
                    catch { }
                });
            };

            AudioScheduling.ConfigureProcessForAudio();
            Application.Run(new WindowMain());
        }
	}
}