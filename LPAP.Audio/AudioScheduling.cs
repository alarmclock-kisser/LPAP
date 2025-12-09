// LPAP.Audio/AudioScheduling.cs
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace LPAP.Audio
{
    public static class AudioScheduling
    {
        private static bool configured;

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentThread();

        [DllImport("kernel32.dll")]
        private static extern bool SetThreadPriority(IntPtr hThread, int nPriority);

        private const int THREAD_PRIORITY_HIGHEST = 2;

        public static void ConfigureProcessForAudio()
        {
            if (configured)
            {
                return;
            }

            configured = true;

            try
            {
                var p = Process.GetCurrentProcess();
                p.PriorityClass = ProcessPriorityClass.High;
            }
            catch { }

            try
            {
                IntPtr hThread = GetCurrentThread();
                SetThreadPriority(hThread, THREAD_PRIORITY_HIGHEST);
            }
            catch { }
        }

        public static void DemoteCurrentThreadForBackgroundWork()
        {
            try
            {
                Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;
            }
            catch { }
        }
    }
}
