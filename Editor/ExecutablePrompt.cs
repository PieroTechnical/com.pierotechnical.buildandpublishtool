using System.IO;
using UnityEditor;
using UnityEngine;

namespace Pierotechnical.BuildAndUploadTool.Editor
{
    internal static class ExecutablePrompt
    {
        public static string Choose(string panelTitle, string currentPath)
        {
            string extension = Application.platform == RuntimePlatform.WindowsEditor ? "exe" : string.Empty;
            string directory = string.Empty;
            if (!string.IsNullOrEmpty(currentPath))
                directory = Path.GetDirectoryName(currentPath);

            return EditorUtility.OpenFilePanel(panelTitle, directory ?? string.Empty, extension);
        }
    }
}
