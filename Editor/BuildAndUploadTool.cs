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
        private string gameName;
        private string organizationName;
        private string version = "0.1.0";
        private string previousVersion;

        private bool buildWindows = true;
        private bool buildMac = false;
        private bool buildLinux = false;

        [MenuItem("Tools/Build Automation and Publish Tool %#u")] // Ctrl + Shift + U
        public static void ShowWindow()
        {
            var window = GetWindow<BuildAndUploadTool>("Build Automation and Publish Tool");
            window.LoadOptions();
        }

        void OnEnable()
        {
            string projectPath = Application.dataPath;
            buildsFolderPath = Path.Combine(Directory.GetParent(projectPath).FullName, "Builds");
            LoadOptions();
            previousVersion = version;
        }

        void OnGUI()
        {
            DrawTitle();

            DrawProjectOptions();
            DrawBuildOptions();
            DrawBuildButton();
            CheckVersionAndUpdate();
        }

        private static void DrawTitle()
        {
            GUILayout.Label("Build Automation and Publish Tool (PieroTechnical)",
                            EditorStyles.largeLabel,
                            GUILayout.ExpandWidth(true));
        }

        private void CheckVersionAndUpdate()
        {
            if (version != previousVersion)
            {
                SaveOptions();
                previousVersion = version;
            }
        }

        private void DrawProjectOptions()
        {
            GUILayout.Space(5);
            GUILayout.Label("Project Options:", EditorStyles.boldLabel);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Username:", GUILayout.Width(100));
            organizationName = GUILayout.TextField(organizationName, GUILayout.ExpandWidth(true));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Game Title:", GUILayout.Width(100));
            gameName = GUILayout.TextField(gameName, GUILayout.ExpandWidth(true));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Game URL:", GUILayout.Width(100));
            GUILayout.Label($"https://{organizationName}.itch.io/{gameName}".ToLower());
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Version:", GUILayout.Width(100));
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
            GUILayout.Space(5);
            GUILayout.Label("Build Options:", EditorStyles.boldLabel);
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
                Debug.Log($"Building for {buttonText.Split(' ')[4]}...");
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
            string butlerPath = PlayerPrefs.GetString(ButlerPathKey, null);
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

            Debug.Log($"Using ButlerPath: {butlerPath}");
            string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(filePath);
            string channel = fileNameWithoutExtension.ToLower();
            string cmdArgs = $"push \"{filePath}\" {organizationName}/{gameName}:{channel} --userversion {version}";

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
                    Debug.Log($"Butler upload successful:\n{output}");
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

        private void BuildAndUploadWindows()
        {
            string[] scenes = GetBuildScenes();
            string buildPath = buildsFolderPath;

            bool success = true;
            string errorMessage = "";

            success &= BuildAndUpload(buildPath, scenes, BuildTarget.StandaloneWindows64, "windows", $"{gameName}.exe", ScriptingImplementation.IL2CPP, ref errorMessage);

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

            string versionsDirectoryPath = Path.Combine(buildsFolderPath, "_versions");
            if (!Directory.Exists(versionsDirectoryPath))
            {
                Directory.CreateDirectory(versionsDirectoryPath);
            }

            string platformName = GetPlatformName(channel);
            string versionedZipFileName = $"Grib_{platformName}_{version}.zip";
            string copyFilePath = Path.Combine(versionsDirectoryPath, versionedZipFileName);

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

            EditorUtility.DisplayProgressBar("Zipping Files", "Creating zip archive...", 0.5f);

            using (var archive = ZipFile.Open(destinationPath, ZipArchiveMode.Create))
            {
                foreach (string file in Directory.GetFiles(sourcePath, "*", SearchOption.AllDirectories))
                {
                    string entryName = Path.GetRelativePath(sourcePath, file);

                    if (IsExcludedFileOrDirectory(entryName)) continue;

                    archive.CreateEntryFromFile(file, entryName);
                }
            }

            EditorUtility.ClearProgressBar();
            Debug.Log($"Created zip file at {destinationPath}");
        }

        private bool IsExcludedFileOrDirectory(string entryName)
        {
            string[] excludedFolders = { "Grib_BackUpThisFolder_ButDontShipItWithYourGame", "Game_BurstDebugInformation_DoNotShip" };
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

        private void LoadOptions()
        {

            gameName = PlayerSettings.productName;
            organizationName = PlayerSettings.companyName;

            if (File.Exists(VersionFilePath))
            {
                version = File.ReadAllText(VersionFilePath).Trim();
                previousVersion = version;
            }
            else
            {
                SaveOptions();
            }
        }

        private void SaveOptions()
        {
            File.WriteAllText(VersionFilePath, version);
        }

        private void IncrementMinorVersion()
        {
            var versionParts = version.Split('.');
            if (versionParts.Length == 3 && int.TryParse(versionParts[1], out int minorVersion))
            {
                minorVersion++;
                version = $"{versionParts[0]}.{minorVersion}.0";
                SaveOptions();
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
                SaveOptions();
            }
            else
            {
                Debug.LogError("Version format is incorrect. Expected format: X.Y.Z");
            }
        }
    }
}
