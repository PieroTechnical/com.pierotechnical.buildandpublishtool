
using System.Diagnostics;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Pierotechnical.BuildAndUploadTool.Editor
{
    public class ButlerUploader
    {
        private string butlerPath;

        public ButlerUploader()
        {
            butlerPath = PlayerPrefs.GetString(BuildManager.ButlerPathKey, null);
            if (string.IsNullOrEmpty(butlerPath))
            {
                butlerPath = EditorUtility.OpenFilePanel("Locate butler executable", "", "exe");
                if (!string.IsNullOrEmpty(butlerPath))
                {
                    PlayerPrefs.SetString(BuildManager.ButlerPathKey, butlerPath);
                    PlayerPrefs.Save();
                }
                else
                {
                    Debug.LogError("Butler executable not located. Upload cancelled.");
                }
            }
        }

        public void Upload(string filePath, string channel, string version)
        {
            if (string.IsNullOrEmpty(butlerPath))
            {
                Debug.LogError("Butler executable path is not set.");
                return;
            }

            string gameName = PlayerSettings.productName;
            string organizationName = PlayerSettings.companyName;
            string formattedGameName = FormatURL(gameName); // Ensure game name is formatted correctly for URL
            string formattedOrganizationName = FormatURL(organizationName);

            string cmdArgs = $"push \"{filePath}\" {formattedOrganizationName}/{formattedGameName}:{channel} --userversion {version}";

            try
            {
                Process process = new Process();
                process.StartInfo.FileName = butlerPath;
                process.StartInfo.Arguments = cmdArgs;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.CreateNoWindow = true;
                process.Start();

                EditorUtility.DisplayProgressBar("Uploading to itch.io", "Uploading...", 0.5f);

                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();

                EditorUtility.ClearProgressBar();

                if (process.ExitCode == 0)
                {
                    Debug.Log($"Butler upload finished:\n{output}");
                }
                else
                {
                    Debug.LogError($"Butler upload failed:\n{error}");
                }
            }
            catch (System.Exception e)
            {
                EditorUtility.ClearProgressBar();
                Debug.LogError($"Exception during Butler upload: {e.Message}");
            }
        }

        private string FormatURL(string input)
        {
            return input.ToLower().Replace(" ", "-");
        }
    }
}