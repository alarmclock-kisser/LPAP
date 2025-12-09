using LPAP.Audio;
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
            AudioScheduling.ConfigureProcessForAudio();
            Application.Run(new WindowMain());
        }
    }
}