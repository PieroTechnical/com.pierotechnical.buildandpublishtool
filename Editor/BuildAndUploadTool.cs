using UnityEngine;
using UnityEditor;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Debug = UnityEngine.Debug;
using System;

namespace Pierotechnical.BuildAndUploadTool.Editor
{
    public class BuildAndUploadTool : EditorWindow
    {
        private string buildsFolderPath;
        private const string ButlerPathKey = "ButlerPath";
        private const string VersionFilePath = "Assets/version.txt";
        private string version = "0.1.0";
        private string previousVersion; // Store the previous version to detect changes

        private bool buildWindows = true;
        private bool buildMac = false;
        private bool buildLinux = false;

        [MenuItem("Tools/Build and Upload Automation Tool %#u")] // Ctrl + Shift + U
        public static void ShowWindow()
        {
            var window = GetWindow<BuildAndUploadTool>("Build and Upload Automation Tool");
            window.LoadVersion(); // Ensure the latest version is loaded when the window is opened
        }

        void OnEnable()
        {
            string projectPath = Application.dataPath;
            buildsFolderPath = Path.Combine(Directory.GetParent(projectPath).FullName, "Builds");
            LoadVersion();
            previousVersion = version; // Initialize previousVersion
        }

        void OnGUI()
        {
            GUILayout.Label("Build and Upload Automation Tool", EditorStyles.boldLabel);
            DrawVersionControls();
            DrawBuildOptions();
            DrawBuildButton();
            CheckVersionAndUpdate();
        }

        private void CheckVersionAndUpdate()
        {
            if (version != previousVersion)
            {
                SaveVersion();
                previousVersion = version; // Update the previousVersion after saving
            }
        }

        private void DrawVersionControls()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Version:", GUILayout.Width(50));
            version = GUILayout.TextField(version, GUILayout.ExpandWidth(true));
            if (GUILayout.Button("Increment Minor", GUILayout.ExpandWidth(true)))
            {
                IncrementMinorVersion();
            }
            if (GUILayout.Button("Increment Patch", GUILayout.ExpandWidth(true)))
            {
                IncrementPatchVersion();
            }
            GUILayout.EndHorizontal();
        }

        private void DrawBuildOptions()
        {
            DrawPlatformBuildOption("Build and Upload for Windows", BuildAndUploadWindows, ref buildWindows);
            DrawPlatformBuildOption("Build and Upload for Mac", BuildAndUploadMac, ref buildMac, Application.platform == RuntimePlatform.OSXEditor);
            DrawPlatformBuildOption("Build and Upload for Linux", BuildAndUploadLinux, ref buildLinux, false);
        }

        private void DrawPlatformBuildOption(string buttonText, Action buildAction, ref bool buildToggle, bool isEnabled = true)
        {
            GUILayout.BeginHorizontal();
            EditorGUI.BeginDisabledGroup(!isEnabled);

            if (GUILayout.Button(buttonText, GUILayout.ExpandWidth(true)))
            {
                Debug.Log($"Building for {buttonText.Split(' ')[4]}..."); // Assumes the platform name is the fifth word in the button text
                buildAction();
            }

            buildToggle = EditorGUILayout.Toggle(buildToggle, GUILayout.Width(20));

            EditorGUI.EndDisabledGroup();
            GUILayout.EndHorizontal();
        }


        private void DrawBuildButton()
        {
            if (GUILayout.Button("Build and Upload Selected Platforms", GUILayout.ExpandWidth(true)))
            {
                Debug.Log("Build and Upload Selected Platforms button clicked.");
                if (buildWindows) BuildAndUploadWindows();
                if (buildMac) BuildAndUploadMac();
                if (buildLinux) BuildAndUploadLinux();
            }
        }

        private void UploadToItch(string filePath)
        {
            string butlerPath = System.Environment.GetEnvironmentVariable("ButlerPath");
            if (string.IsNullOrEmpty(butlerPath))
            {
                butlerPath = PlayerPrefs.GetString(ButlerPathKey, null);
                if (string.IsNullOrEmpty(butlerPath))
                {
                    butlerPath = EditorUtility.OpenFilePanel("Locate butler executable", "", "exe");
                    if (!string.IsNullOrEmpty(butlerPath))
                    {
                        PlayerPrefs.SetString(ButlerPathKey, butlerPath);
                        PlayerPrefs.Save();
                    }
                    else
                    {
                        Debug.LogError("Butler executable not located. Upload cancelled.");
                        return;
                    }
                }
            }

            Debug.Log($"Using ButlerPath: {butlerPath}");
            string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(filePath);
            string user = "pierotechnical";
            string game = "grib";
            string channel = fileNameWithoutExtension.ToLower();
            string cmdArgs = $"push \"{filePath}\" {user}/{game}:{channel} --userversion {version}";

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

                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode == 0)
                {
                    Debug.Log($"Butler upload successful:\n{output}");
                }
                else
                {
                    Debug.LogError($"Butler upload failed:\n{error}");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Exception during Butler upload: {e.Message}");
            }
        }

        private void BuildAndUploadWindows()
        {
            string[] scenes = GetBuildScenes();
            string buildPath = buildsFolderPath;

            bool success = true;
            string errorMessage = "";

            success &= BuildAndUpload(buildPath, scenes, BuildTarget.StandaloneWindows64, "windows", "Game.exe", ScriptingImplementation.IL2CPP, ref errorMessage);

            if (success)
            {
                Debug.Log("Build uploaded to itch.io successfully!");
            }
            else
            {
                Debug.LogError($"Build failed to upload:\n{errorMessage}");
            }
        }

        private void BuildAndUploadMac()
        {
            if (Application.platform != RuntimePlatform.OSXEditor)
            {
                Debug.LogError("Mac builds can only be performed on a Mac system.");
                return;
            }

            string[] scenes = GetBuildScenes();
            string buildPath = buildsFolderPath;
            string errorMessage = "";

            if (IsIL2CPPInstalled(BuildTarget.StandaloneOSX))
            {
                if (!BuildAndUpload(buildPath, scenes, BuildTarget.StandaloneOSX, "mac", "Game.app", ScriptingImplementation.IL2CPP, ref errorMessage))
                {
                    Debug.LogError("IL2CPP build failed: " + errorMessage);
                }
            }
            else
            {
                Debug.LogError("IL2CPP is not installed for Mac.");
            }
        }

        private void TryMonoFallback(string buildPath, string[] scenes, string channel, string executableName, ref string errorMessage)
        {
            if (IsMonoInstalled(BuildTarget.StandaloneOSX))
            {
                if (!BuildAndUpload(buildPath, scenes, BuildTarget.StandaloneOSX, channel, executableName, ScriptingImplementation.Mono2x, ref errorMessage))
                {
                    Debug.LogError("Mono build failed: " + errorMessage);
                }
            }
            else
            {
                errorMessage = "Mono is not installed.";
                Debug.LogError(errorMessage);
            }
        }

        private bool BuildWithMono(string buildPath, string[] scenes, string channel, string executableName, ref string errorMessage)
        {
            if (IsMonoInstalled(BuildTarget.StandaloneOSX))
            {
                return BuildAndUpload(buildPath, scenes, BuildTarget.StandaloneOSX, channel, executableName, ScriptingImplementation.Mono2x, ref errorMessage);
            }
            else
            {
                errorMessage = "Mono is not installed.";
                Debug.LogError("Build failed: " + errorMessage);
                return false; // Indicate failure
            }
        }

        private bool BuildAndUpload(string buildPath, string[] scenes, BuildTarget target, string channel, string executableName, ScriptingImplementation backend, ref string errorMessage)
        {
            string targetPath = Path.Combine(buildPath, channel, executableName);
            BuildPlayerOptions buildPlayerOptions = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = targetPath,
                target = target,
                options = BuildOptions.None,
                targetGroup = BuildTargetGroup.Standalone
            };
            PlayerSettings.SetScriptingBackend(BuildPipeline.GetBuildTargetGroup(target), backend);

            var report = BuildPipeline.BuildPlayer(buildPlayerOptions);
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            {
                string error = $"Build for {channel} with {backend} failed.";
                Debug.LogError(error);
                errorMessage += error + "\n";
                return false;
            }
            else
            {
                ZipAndUpload(Path.Combine(buildPath, channel), channel, version);
                return true;
            }
        }

        private bool IsIL2CPPInstalled(BuildTarget target)
        {
            // Return true if IL2CPP is the scripting backend for the specified target
            return PlayerSettings.GetScriptingBackend(BuildPipeline.GetBuildTargetGroup(target)) == ScriptingImplementation.IL2CPP;
        }

        private bool IsMonoInstalled(BuildTarget target)
        {
            // Return true if Mono is the scripting backend for the specified target
            return PlayerSettings.GetScriptingBackend(BuildPipeline.GetBuildTargetGroup(target)) == ScriptingImplementation.Mono2x;
        }

        private void BuildAndUploadLinux()
        {
            string[] scenes = GetBuildScenes();
            string buildPath = buildsFolderPath;

            bool success = true;
            string errorMessage = "";

            success &= BuildAndUploadWithFallback(buildPath, scenes, BuildTarget.StandaloneLinux64, "linux", "Game.x86_64", ref errorMessage);

            if (success)
            {
                Debug.Log("Build uploaded to itch.io successfully!");
            }
            else
            {
                Debug.LogError($"Build failed to upload:\n{errorMessage}");
            }
        }

        private void BuildAndUploadAll()
        {
            string[] scenes = GetBuildScenes();
            string buildPath = buildsFolderPath;

            bool success = true;
            string errorMessage = "";

            success &= BuildAndUpload(buildPath, scenes, BuildTarget.StandaloneWindows64, "windows", "Game.exe", ScriptingImplementation.IL2CPP, ref errorMessage);
            success &= BuildAndUploadWithFallback(buildPath, scenes, BuildTarget.StandaloneOSX, "mac", "Game.app", ref errorMessage);
            success &= BuildAndUploadWithFallback(buildPath, scenes, BuildTarget.StandaloneLinux64, "linux", "Game.x86_64", ref errorMessage);

            if (success)
            {
                Debug.Log("All builds uploaded to itch.io successfully!");
            }
            else
            {
                Debug.LogError($"Some builds failed to upload:\n{errorMessage}");
            }
        }

        private bool BuildAndUploadWithFallback(string buildPath, string[] scenes, BuildTarget target, string channel, string fileName, ref string errorMessage)
        {
            bool success = false;
            if (IsIL2CPPInstalled(target))
            {
                success = BuildAndUpload(buildPath, scenes, target, channel, fileName, ScriptingImplementation.IL2CPP, ref errorMessage);
            }

            if (!success && IsMonoInstalled(target))
            {
                success = BuildAndUpload(buildPath, scenes, target, channel, fileName, ScriptingImplementation.Mono2x, ref errorMessage);
            }

            return success;
        }

        private bool IsBuildTargetSupported(BuildTarget target)
        {
            return BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, target);
        }

        private string[] GetBuildScenes()
        {
            return EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();
        }

        private void ZipAndUpload(string buildFolder, string channel, string version)
        {
            string zipFilePath = buildFolder + ".zip";
            CreateZipFile(buildFolder, zipFilePath);

            // Ensure the _versions directory exists
            string versionsDirectoryPath = Path.Combine(buildsFolderPath, "_versions");
            if (!Directory.Exists(versionsDirectoryPath))
            {
                Directory.CreateDirectory(versionsDirectoryPath);
            }

            // Adjust the path for the versioned zip file to include the _versions subfolder
            string platformName = GetPlatformName(channel);
            string versionedZipFileName = $"Grib_{platformName}_{version}.zip";
            string copyFilePath = Path.Combine(versionsDirectoryPath, versionedZipFileName);

            // Check if file already exists and delete it to avoid IOException on copy
            if (File.Exists(copyFilePath))
            {
                File.Delete(copyFilePath);
            }

            File.Copy(zipFilePath, copyFilePath, true);
            Debug.Log($"Versioned ZIP file saved to: {copyFilePath}");

            UploadToItch(zipFilePath);
        }

        private void CreateZipFile(string sourcePath, string destinationPath)
        {
            if (File.Exists(destinationPath))
            {
                File.Delete(destinationPath);
            }

            if (!Directory.Exists(sourcePath))
            {
                Directory.CreateDirectory(sourcePath);
            }

            using (var archive = ZipFile.Open(destinationPath, ZipArchiveMode.Create))
            {
                foreach (string file in Directory.GetFiles(sourcePath, "*", SearchOption.AllDirectories))
                {
                    string entryName = Path.GetRelativePath(sourcePath, file);

                    if (IsExcludedFileOrDirectory(entryName)) continue;

                    archive.CreateEntryFromFile(file, entryName);
                }
            }
            Debug.Log($"Created zip file at {destinationPath}");
        }

        private bool IsExcludedFileOrDirectory(string entryName)
        {
            string[] excludedFolders = { "Game_BackUpThisFolder_ButDontShipItWithYourGame", "Game_BurstDebugInformation_DoNotShip" };
            return excludedFolders.Any(folder => entryName.Contains(folder));
        }

        private string GetPlatformName(string channel)
        {
            switch (channel.ToLower())
            {
                case "windows":
                    return "Windows";
                case "mac":
                    return "Mac";
                case "linux":
                    return "Linux";
                default:
                    return "UnknownPlatform";
            }
        }

        private void LoadVersion()
        {
            if (File.Exists(VersionFilePath))
            {
                version = File.ReadAllText(VersionFilePath).Trim();
                previousVersion = version; // Ensure previousVersion is also updated when loading
            }
            else
            {
                SaveVersion();
            }
        }

        private void SaveVersion()
        {
            File.WriteAllText(VersionFilePath, version);
            Debug.Log("Version saved: " + version); // Optionally log that the version was saved
        }

        private void IncrementMinorVersion()
        {
            var versionParts = version.Split('.');
            if (versionParts.Length == 3 && int.TryParse(versionParts[1], out int minorVersion))
            {
                minorVersion++;
                version = $"{versionParts[0]}.{minorVersion}.0";
                SaveVersion();
            }
            else
            {
                Debug.LogError("Version format is incorrect. Expected format: X.Y.Z");
            }
        }

        private void IncrementPatchVersion()
        {
            var versionParts = version.Split('.');
            if (versionParts.Length == 3 && int.TryParse(versionParts[2], out int patchVersion))
            {
                patchVersion++;
                version = $"{versionParts[0]}.{versionParts[1]}.{patchVersion}";
                SaveVersion();
            }
            else
            {
                Debug.LogError("Version format is incorrect. Expected format: X.Y.Z");
            }
        }
    }
}
