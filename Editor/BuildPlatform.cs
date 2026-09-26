using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using Debug = UnityEngine.Debug;

namespace Pierotechnical.BuildAndUploadTool.Editor
{
    internal static class PlatformCatalog
    {
        internal sealed class Entry
        {
            internal Entry(string id, string label, string channel, BuildTarget target, bool supportsSteam)
            {
                Id = id;
                Label = label;
                Channel = channel;
                Target = target;
                SupportsSteam = supportsSteam;
            }

            public string Id { get; private set; }
            public string Label { get; private set; }
            public string Channel { get; private set; }
            public BuildTarget Target { get; private set; }
            public bool SupportsSteam { get; private set; }
        }

        static readonly Entry[] Platforms =
        {
            CreateEntry(BuildTarget.StandaloneWindows64),
            CreateEntry(BuildTarget.StandaloneOSX),
            CreateEntry(BuildTarget.StandaloneLinux64),
            CreateEntry(BuildTarget.WebGL)
        };

        public static Entry[] All()
        {
            var copy = new Entry[Platforms.Length];
            for (int i = 0; i < Platforms.Length; i++)
            {
                Entry entry = Platforms[i];
                copy[i] = new Entry(entry.Id, entry.Label, entry.Channel, entry.Target, entry.SupportsSteam);
            }
            return copy;
        }

        public static Entry Find(string id)
        {
            if (string.IsNullOrEmpty(id))
                return null;

            for (int i = 0; i < Platforms.Length; i++)
            {
                if (Platforms[i].Id == id)
                    return Platforms[i];
            }

            return null;
        }

        public static bool SupportsSteamPlatform(string platformId)
        {
            Entry entry = Find(platformId);
            return entry != null && entry.SupportsSteam;
        }

        public static BuildTargetGroup Group(BuildTarget target)
        {
#pragma warning disable 618
            return BuildPipeline.GetBuildTargetGroup(target);
#pragma warning restore 618
        }

        static Entry CreateEntry(BuildTarget target)
        {
            BuildTargetDescriptor descriptor = BuildTargetCatalog.Find(target);
            return new Entry(
                descriptor.Id,
                descriptor.Label,
                descriptor.DefaultChannel,
                descriptor.Target,
                descriptor.SupportsSteam);
        }
    }

    internal static class ProjectBuilder
    {
        internal static TargetResult Execute(BuildPlan plan, int targetIndex)
        {
            if (plan == null
                || plan.Targets == null
                || targetIndex < 0
                || targetIndex >= plan.Targets.Count
                || plan.Targets[targetIndex] == null)
            {
                return new TargetResult { Label = "Build", Message = "Build plan target was missing." };
            }

            BuildTargetPlan target = plan.Targets[targetIndex];
            var result = new TargetResult
            {
                TargetId = target.Id,
                Label = target.Label
            };
            BuildTarget buildTarget = (BuildTarget)target.TargetValue;
            string folderKey = string.IsNullOrEmpty(target.OutputKey) ? "game" : target.OutputKey;
            string tempDir = BuildPaths.TempFolder(
                plan.OutputRoot,
                plan.GameName,
                plan.Version,
                folderKey,
                plan.Id);
            string finalDir = BuildPaths.VersionedFolder(
                plan.OutputRoot,
                plan.GameName,
                plan.Version,
                folderKey);

            try
            {
                string prepareError;
                if (!SafeFileSystem.TryDeleteDirectory(plan.OutputRoot, tempDir, out prepareError))
                {
                    result.Message = "Could not clear the temporary build folder: " + prepareError;
                    return result;
                }

                Directory.CreateDirectory(tempDir);

                string[] scenes = plan.Scenes ?? new string[0];
                if (scenes.Length == 0)
                {
                    result.Message = "No enabled scenes in Build Settings.";
                    return result;
                }

                var options = new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = BuildPaths.PlayerLocation(
                        tempDir,
                        buildTarget,
                        plan.GameName,
                        plan.AndroidAppBundle),
                    target = buildTarget,
                    options = BuildOptions.None,
                    targetGroup = PlatformCatalog.Group(buildTarget)
                };

                if (buildTarget == BuildTarget.Android)
                    EditorUserBuildSettings.buildAppBundle = plan.AndroidAppBundle;

                BuildReport report = BuildPipeline.BuildPlayer(options);
                if (report == null || report.summary.result != BuildResult.Succeeded)
                {
                    int errors = report == null ? 0 : report.summary.totalErrors;
                    result.Message = "Build for " + target.Label + " failed.";
                    if (errors > 0)
                        result.Message += " Errors: " + errors + ".";
                    return result;
                }

                string promoteError;
                if (!SafeFileSystem.TryPromote(
                    plan.OutputRoot,
                    tempDir,
                    finalDir,
                    plan.Id,
                    out promoteError))
                {
                    result.Message = promoteError;
                    return result;
                }

                result.BuildSucceeded = true;
                result.ArtifactPath = finalDir;
                result.ArtifactBytes = report.summary.totalSize > (ulong)long.MaxValue
                    ? long.MaxValue
                    : (long)report.summary.totalSize;
                result.Message = "Built " + target.Label + " to " + finalDir;
                return result;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                result.BuildSucceeded = false;
                if (string.IsNullOrEmpty(result.Message))
                    result.Message = target.Label + " failed: " + exception.Message;
                else
                    result.Message += " " + exception.Message;
                return result;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                if (!result.BuildSucceeded)
                    SafeFileSystem.TryDeleteDirectory(plan.OutputRoot, tempDir, out _);
            }
        }

        public static string[] GetEnabledScenes()
        {
            var enabled = new System.Collections.Generic.List<string>();
            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
            for (int i = 0; i < scenes.Length; i++)
            {
                if (scenes[i] != null && scenes[i].enabled && !string.IsNullOrEmpty(scenes[i].path))
                    enabled.Add(scenes[i].path);
            }

            return enabled.ToArray();
        }

    }
}
