using System.IO;
using System.IO.Compression;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Pierotechnical.BuildAndUploadTool.Editor
{
    public interface IBuildPlatform
    {
        bool IsEnabled { get; }
        void DrawBuildOptions();
        void Build();
        void Upload();
    }

    public abstract class BuildPlatform : IBuildPlatform
    {
        protected string gameName;
        protected string organizationName;
        protected string version;
        protected string buildsFolderPath;
        protected string buildDirectory;
        protected BuildTarget buildTarget;
        protected string buildTargetName;
        protected ScriptingImplementation scriptingBackend;

        public bool IsEnabled { get; protected set; }

        protected BuildPlatform(string gameName, string organizationName, string version, string buildsFolderPath, BuildTarget buildTarget, ScriptingImplementation scriptingBackend, string buildTargetName = "")
        {
            this.gameName = gameName;
            this.organizationName = organizationName;
            this.version = version;
            this.buildsFolderPath = buildsFolderPath;
            this.buildTarget = buildTarget;
            this.buildTargetName = buildTargetName;
            this.scriptingBackend = scriptingBackend;
            this.buildDirectory = Path.Combine(buildsFolderPath, $"{gameName}_{buildTarget}");
        }

        public abstract void DrawBuildOptions();

        public virtual void Build()
        {
            if(buildTargetName == "") {
                buildTargetName = buildTarget.ToString();
            }

            if (Directory.Exists(buildDirectory))
            {
                Directory.Delete(buildDirectory, true);
            }
            Directory.CreateDirectory(buildDirectory);

            var buildPlayerOptions = new BuildPlayerOptions
            {
                scenes = GetBuildScenes(),
                locationPathName = GetBuildPath(),
                target = buildTarget,
                options = BuildOptions.None,
                targetGroup = BuildPipeline.GetBuildTargetGroup(buildTarget)
            };

            PlayerSettings.SetScriptingBackend(BuildPipeline.GetBuildTargetGroup(buildTarget), scriptingBackend);
            var report = BuildPipeline.BuildPlayer(buildPlayerOptions);

            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            {
                Debug.LogError($"Build for {buildTarget} with {scriptingBackend} failed.");
            }
            else
            {
                Debug.Log($"Build for {buildTarget} succeeded.");
            }
        }

        protected virtual string GetBuildPath()
        {
            return Path.Combine(buildDirectory, $"{gameName}.exe");
        }

        public virtual void Upload()
        {
            string zipFilePath = $"{buildDirectory}.zip";
            CreateZipFile(buildDirectory, zipFilePath);

            var uploader = new ButlerUploader();
            uploader.Upload(zipFilePath, buildTargetName, version);
        }

        protected void CreateZipFile(string sourcePath, string destinationPath)
        {
            if (File.Exists(destinationPath))
            {
                File.Delete(destinationPath);
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
            string[] excludedFolders = { $"{gameName}_BackUpThisFolder_ButDontShipItWithYourGame", $"{gameName}_BurstDebugInformation_DoNotShip" };
            return excludedFolders.Any(folder => entryName.Contains(folder));
        }

        protected string[] GetBuildScenes()
        {
            return EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();
        }
    }

    public class WindowsBuild : BuildPlatform
    {
        public WindowsBuild(string gameName, string organizationName, string version, string buildsFolderPath)
            : base(gameName, organizationName, version, buildsFolderPath, BuildTarget.StandaloneWindows64, ScriptingImplementation.IL2CPP, "windows") { }

        public override void DrawBuildOptions()
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Build and Upload for Windows", GUILayout.ExpandWidth(true)))
            {
                Build();
                Upload();
            }
            IsEnabled = EditorGUILayout.Toggle(IsEnabled, GUILayout.Width(20));
            GUILayout.EndHorizontal();
        }

        protected override string GetBuildPath()
        {
            return Path.Combine(buildDirectory, $"{gameName}.exe");
        }
    }

    public class MacBuild : BuildPlatform
    {
        public MacBuild(string gameName, string organizationName, string version, string buildsFolderPath)
            : base(gameName, organizationName, version, buildsFolderPath, BuildTarget.StandaloneOSX, ScriptingImplementation.IL2CPP, "mac") { }

        public override void DrawBuildOptions()
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Build and Upload for Mac", GUILayout.ExpandWidth(true)))
            {
                Build();
                Upload();
            }
            IsEnabled = EditorGUILayout.Toggle(IsEnabled, GUILayout.Width(20));
            GUILayout.EndHorizontal();
        }

        protected override string GetBuildPath()
        {
            return Path.Combine(buildDirectory, "MacBuild.app");
        }
    }

    public class LinuxBuild : BuildPlatform
    {
        public LinuxBuild(string gameName, string organizationName, string version, string buildsFolderPath)
            : base(gameName, organizationName, version, buildsFolderPath, BuildTarget.StandaloneLinux64, ScriptingImplementation.IL2CPP, "linux") { }

        public override void DrawBuildOptions()
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Build and Upload for Linux", GUILayout.ExpandWidth(true)))
            {
                Build();
                Upload();
            }
            IsEnabled = EditorGUILayout.Toggle(IsEnabled, GUILayout.Width(20));
            GUILayout.EndHorizontal();
        }

        protected override string GetBuildPath()
        {
            return Path.Combine(buildDirectory, "LinuxBuild.x86_64");
        }
    }

    public class WebGLBuild : BuildPlatform
    {
        public WebGLBuild(string gameName, string organizationName, string version, string buildsFolderPath)
            : base(gameName, organizationName, version, buildsFolderPath, BuildTarget.WebGL, ScriptingImplementation.Mono2x) { }

        public override void DrawBuildOptions()
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Build and Upload for WebGL", GUILayout.ExpandWidth(true)))
            {
                Build();
                Upload();
            }
            IsEnabled = EditorGUILayout.Toggle(IsEnabled, GUILayout.Width(20));
            GUILayout.EndHorizontal();
        }

        protected override string GetBuildPath()
        {
            return Path.Combine(buildDirectory, "WebGLBuild");
        }
    }
}