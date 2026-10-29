using System;
using UnityEditor;
using UnityEngine;
#if UNITY_EDITOR_WIN
using System.Runtime.InteropServices;
#endif

namespace Pierotechnical.BuildAndUploadTool.Editor
{
    internal static class EditorAwake
    {
        const uint BackgroundTickMilliseconds = 100;

        static int holders;
        static bool reloadHooked;
#if UNITY_EDITOR_WIN
        static IntPtr timerId;
        static TimerProc timerProc;
#else
        static DateTime nextPumpUtc;
#endif

        internal static void Hold()
        {
            holders++;
            EnsureReloadHook();
            StartTicking();
        }

        internal static void Release()
        {
            if (holders == 0)
                throw new InvalidOperationException("Released an Editor awake that was not held.");

            holders--;
            if (holders == 0)
                StopTicking();
        }

        static void EnsureReloadHook()
        {
            if (reloadHooked)
                return;

            reloadHooked = true;
            AssemblyReloadEvents.beforeAssemblyReload += StopForReload;
            EditorApplication.quitting += StopForReload;
        }

        static void StopForReload()
        {
            StopTicking();
        }

        static void StartTicking()
        {
#if UNITY_EDITOR_WIN
            if (timerId != IntPtr.Zero)
                return;

            if (timerProc == null)
                timerProc = OnTimer;
            timerId = SetTimer(IntPtr.Zero, IntPtr.Zero, BackgroundTickMilliseconds, timerProc);
            if (timerId == IntPtr.Zero)
            {
                Debug.LogError(
                    "Could not keep the Editor ticking in the background. Windows error "
                    + Marshal.GetLastWin32Error() + ".");
            }
#else
            EditorApplication.update -= Pump;
            EditorApplication.update += Pump;
#endif
        }

        static void StopTicking()
        {
#if UNITY_EDITOR_WIN
            if (timerId == IntPtr.Zero)
                return;

            IntPtr id = timerId;
            timerId = IntPtr.Zero;
            if (!KillTimer(IntPtr.Zero, id))
            {
                Debug.LogError(
                    "Could not stop the Editor background tick. Windows error "
                    + Marshal.GetLastWin32Error() + ".");
            }
#else
            EditorApplication.update -= Pump;
#endif
        }

#if UNITY_EDITOR_WIN
        // Unity still dispatches window timers on the main thread while it is
        // unfocused, but it does not schedule Editor ticks. QueuePlayerLoopUpdate
        // is what makes update, and the queue steps and process polling on it, run.
        static void OnTimer(IntPtr window, uint message, IntPtr id, uint time)
        {
            try
            {
                if (holders > 0)
                    EditorApplication.QueuePlayerLoopUpdate();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        delegate void TimerProc(IntPtr window, uint message, IntPtr id, uint time);

        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr SetTimer(IntPtr window, IntPtr id, uint milliseconds, TimerProc procedure);

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool KillTimer(IntPtr window, IntPtr id);
#else
        static void Pump()
        {
            DateTime now = DateTime.UtcNow;
            if (now < nextPumpUtc)
                return;

            nextPumpUtc = now.AddMilliseconds(BackgroundTickMilliseconds);
            EditorApplication.QueuePlayerLoopUpdate();
        }
#endif
    }
}
