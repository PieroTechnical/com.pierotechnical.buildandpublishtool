using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using Debug = UnityEngine.Debug;

namespace Pierotechnical.BuildAndUploadTool.Editor
{
    public static class PlatformCatalog
    {
        public sealed class Entry
        {
            public string Id;
            public string Label;
            public string Channel;
            public BuildTarget Target;
            public bool SupportsSteam;
        }

        static readonly Entry[] Platforms =
        {
            new Entry { Id = "windows", Label = "Windows", Channel = "windows", Target = BuildTarget.StandaloneWindows64, SupportsSteam = true },
            new Entry { Id = "mac", Label = "Mac", Channel = "mac", Target = BuildTarget.StandaloneOSX, SupportsSteam = true },
            new Entry { Id = "linux", Label = "Linux", Channel = "linux", Target = BuildTarget.StandaloneLinux64, SupportsSteam = true },
            new Entry { Id = "webgl", Label = "WebGL", Channel = "webgl", Target = BuildTarget.WebGL, SupportsSteam = false }
        };

        public static Entry[] All()
        {
            return Platforms;
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
    }

    public static class ProjectBuilder
    {
        const int DeleteAttempts = 5;

        public static TargetResult Execute(BuildRequest request)
        {
            var result = new TargetResult { Label = request == null ? "Build" : request.Label };
            if (request == null)
            {
                result.Message = "Build request was missing.";
                return result;
            }

            PlatformCatalog.Entry platform = PlatformCatalog.Find(request.PlatformId);
            if (platform == null)
            {
                result.Message = "Unknown platform '" + request.PlatformId + "'.";
                return result;
            }

            string tempDir = BuildPaths.TempFolder(request.OutputRoot, request.ItchGame, request.Version, request.PlatformId);
            string finalDir = BuildPaths.VersionedFolder(request.OutputRoot, request.ItchGame, request.Version, request.PlatformId);
            string stageDir = BuildPaths.StagingFolder(request.OutputRoot, request.ItchGame, request.Version, request.PlatformId);

            try
            {
                string prepareError;
                if (!TryDeleteDirectory(tempDir, out prepareError))
                {
                    result.Message = "Could not clear the temporary build folder: " + prepareError;
                    return result;
                }

                Directory.CreateDirectory(tempDir);

                string[] scenes = GetEnabledScenes();
                if (scenes.Length == 0)
                {
                    result.Message = "No enabled scenes in Build Settings.";
                    return result;
                }

                var options = new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = BuildPaths.PlayerLocation(tempDir, request.PlatformId, request.ItchGame),
                    target = platform.Target,
                    options = BuildOptions.None,
                    targetGroup = PlatformCatalog.Group(platform.Target)
                };

                BuildReport report = BuildPipeline.BuildPlayer(options);
                if (report == null || report.summary.result != BuildResult.Succeeded)
                {
                    int errors = report == null ? 0 : report.summary.totalErrors;
                    result.Message = "Build for " + request.Label + " failed.";
                    if (errors > 0)
                        result.Message += " Errors: " + errors + ".";
                    return result;
                }

                string promoteError;
                if (!TryPromote(tempDir, finalDir, out promoteError))
                {
                    result.Message = promoteError;
                    return result;
                }

                result.BuildSucceeded = true;
                result.Message = "Built " + request.Label + " to " + finalDir;

                if (!ButlerCommand.ShouldUpload(request.PublishItch, result.BuildSucceeded))
                    return result;

                result.UploadAttempted = true;
                bool stagingCancelled;
                string stageError;
                if (!TryStage(finalDir, stageDir, out stageError, out stagingCancelled))
                {
                    result.Cancelled = stagingCancelled;
                    result.Message += stagingCancelled
                        ? " Staging cancelled."
                        : " Upload skipped: " + stageError;
                    return result;
                }

                string uploadError;
                bool uploaded = ButlerUploader.TryPush(request, stageDir, out uploadError);
                result.UploadSucceeded = uploaded;
                if (!uploaded)
                {
                    result.Cancelled = uploadError == ButlerUploader.UploadCancelledMessage;
                    result.Message += " Upload failed: " + uploadError;
                }
                else
                {
                    string slugUser = BuildPaths.ToItchSlug(request.ItchUser);
                    string slugGame = BuildPaths.ToItchSlug(request.ItchGame);
                    result.Message += " Uploaded to " + slugUser + "/" + slugGame + ":" + request.ChannelName + ".";
                }

                return result;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                if (!result.UploadAttempted)
                    result.BuildSucceeded = false;
                if (string.IsNullOrEmpty(result.Message))
                    result.Message = request.Label + " failed: " + exception.Message;
                else
                    result.Message += " " + exception.Message;
                return result;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                if (!result.BuildSucceeded)
                    TryDeleteDirectory(tempDir, out _);
                TryDeleteDirectory(stageDir, out _);
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

        static bool TryPromote(string tempDir, string finalDir, out string error)
        {
            error = null;
            string parent = Path.GetDirectoryName(finalDir);
            if (!string.IsNullOrEmpty(parent))
                Directory.CreateDirectory(parent);

            string backup = finalDir + ".previous";
            bool movedFinalAside = false;
            try
            {
                string deleteError;
                if (!TryDeleteDirectory(backup, out deleteError))
                {
                    error = "Could not clear a previous replacement folder: " + deleteError;
                    return false;
                }

                if (Directory.Exists(finalDir))
                {
                    Directory.Move(finalDir, backup);
                    movedFinalAside = true;
                }

                Directory.Move(tempDir, finalDir);

                if (Directory.Exists(backup))
                    TryDeleteDirectory(backup, out _);

                return true;
            }
            catch (Exception exception)
            {
                if (movedFinalAside && !Directory.Exists(finalDir) && Directory.Exists(backup))
                {
                    try
                    {
                        Directory.Move(backup, finalDir);
                    }
                    catch (Exception restoreException)
                    {
                        Debug.LogError("Could not restore the previous build folder: " + restoreException.Message);
                    }
                }

                error = "Could not replace the previous build folder: " + exception.Message;
                return false;
            }
        }

        static bool TryStage(string sourceDir, string destinationDir, out string error, out bool cancelled)
        {
            error = null;
            cancelled = false;
            if (!Directory.Exists(sourceDir))
            {
                error = "Build folder was not found at " + sourceDir + ".";
                return false;
            }

            if (!TryDeleteDirectory(destinationDir, out error))
                return false;

            Directory.CreateDirectory(destinationDir);
            string[] files = Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories);
            for (int i = 0; i < files.Length; i++)
            {
                if (i % 20 == 0)
                {
                    float progress = files.Length == 0 ? 1f : (float)i / files.Length;
                    if (EditorUtility.DisplayCancelableProgressBar("Staging build", files[i], progress))
                    {
                        cancelled = true;
                        error = "Staging cancelled.";
                        return false;
                    }
                }

                string relative = BuildPaths.GetRelativePath(sourceDir, files[i]);
                if (Path.IsPathRooted(relative)
                    || relative == ".."
                    || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                {
                    error = "Could not stage " + files[i] + " because its path escaped the build folder.";
                    return false;
                }

                if (PublishExclusions.IsExcluded(relative))
                    continue;

                string targetPath = Path.Combine(destinationDir, relative);
                string targetDirectory = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrEmpty(targetDirectory))
                    Directory.CreateDirectory(targetDirectory);

                File.Copy(files[i], targetPath, true);
            }

            EditorUtility.ClearProgressBar();
            return true;
        }

        static bool TryDeleteDirectory(string path, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
                return true;

            for (int attempt = 0; attempt < DeleteAttempts; attempt++)
            {
                try
                {
                    ClearAttributes(path);
                    Directory.Delete(path, true);
                    return true;
                }
                catch (Exception exception)
                {
                    error = exception.Message;
                    System.Threading.Thread.Sleep(200);
                }
            }

            return !Directory.Exists(path);
        }

        static void ClearAttributes(string directory)
        {
            string[] files = Directory.GetFiles(directory, "*", SearchOption.AllDirectories);
            for (int i = 0; i < files.Length; i++)
                File.SetAttributes(files[i], FileAttributes.Normal);

            string[] directories = Directory.GetDirectories(directory, "*", SearchOption.AllDirectories);
            for (int i = 0; i < directories.Length; i++)
                File.SetAttributes(directories[i], FileAttributes.Normal);
        }
    }
}
