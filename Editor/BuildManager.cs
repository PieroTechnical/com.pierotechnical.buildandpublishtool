using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Pierotechnical.BuildAndUploadTool.Editor
{
    public class BuildManager : EditorWindow
    {
        public const string ButlerPathKey = "ButlerPath"; 
        private List<IBuildPlatform> platforms = new();
        private string buildsFolderPath;
        private const string VersionFilePath = "Assets/version.txt";
        private string gameName;
        private string organizationName;
        private string version = "0.1.0";
        private string previousVersion;

        [MenuItem("Tools/Build Automation and Publish Tool %#u")]
        public static void ShowWindow()
        {
            var window = GetWindow<BuildManager>("Build Automation and Publish Tool");
            window.LoadOptions();
        }

        void OnEnable()
        {
            string projectPath = UnityEngine.Application.dataPath;
            buildsFolderPath = Path.Combine(Directory.GetParent(projectPath).FullName, "Builds");
            LoadOptions();
            previousVersion = version;

            // Initialize platforms
            platforms.Add(new WindowsBuild(gameName, organizationName, version, buildsFolderPath));
            platforms.Add(new MacBuild(gameName, organizationName, version, buildsFolderPath));
            platforms.Add(new LinuxBuild(gameName, organizationName, version, buildsFolderPath));
            platforms.Add(new WebGLBuild(gameName, organizationName, version, buildsFolderPath));
        }

        void OnGUI()
        {
            DrawTitle();
            DrawProjectOptions();
            DrawBuildOptions();
            DrawBuildButton();
            CheckVersionAndUpdate();
        }

        private void DrawTitle()
        {
            GUILayout.Label("Build Automation and Publish Tool (PieroTechnical)", EditorStyles.largeLabel, GUILayout.ExpandWidth(true));
        }

        private void DrawProjectOptions()
        {
            GUILayout.Space(5);
            GUILayout.Label("Project Options:", EditorStyles.boldLabel);

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
            if (GUILayout.Button(GenerateURL(), EditorStyles.linkLabel))
            {
                UnityEngine.Application.OpenURL(GenerateURL());
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(2);
        }

        private string GenerateURL()
        {
            return FormatURL($"https://{organizationName}.itch.io/{gameName}");
        }

        private string FormatURL(string input)
        {
            return input.ToLower().Replace(" ", "-");
        }

        private void DrawBuildOptions()
        {
            GUILayout.Space(5);
            GUILayout.Label("Build Options:", EditorStyles.boldLabel);
            foreach (var platform in platforms)
            {
                platform.DrawBuildOptions();
            }
        }

        private void DrawBuildButton()
        {
            if (GUILayout.Button("Build and Upload Selected Platforms", GUILayout.ExpandWidth(true)))
            {
                Debug.Log("Build and Upload Selected Platforms button clicked.");
                foreach (var platform in platforms)
                {
                    if (platform.IsEnabled)
                    {
                        platform.Build();
                        platform.Upload();
                    }
                }
            }
        }

        private void CheckVersionAndUpdate()
        {
            if (version != previousVersion)
            {
                SaveOptions();
                previousVersion = version;
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