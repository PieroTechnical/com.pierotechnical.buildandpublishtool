using System;
using UnityEditor;

namespace Pierotechnical.BuildAndUploadTool.Editor
{
    internal static class EditorTick
    {
        static Action pending;
        static bool hooked;

        // delayCall is skipped while the Editor application is inactive. These actions
        // run from update instead. Update itself also stops on an unfocused Windows
        // Editor unless EditorAwake is holding a background tick.
        internal static void Schedule(Action action)
        {
            if (action == null)
                throw new ArgumentNullException("action");

            pending += action;
            if (hooked)
                return;

            hooked = true;
            EditorApplication.update += RunScheduled;
        }

        internal static void RunScheduled()
        {
            EditorApplication.update -= RunScheduled;
            hooked = false;
            Action actions = pending;
            pending = null;
            if (actions != null)
                actions.Invoke();
        }
    }
}
