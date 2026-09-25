using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Pierotechnical.BuildAndUploadTool.Editor
{
    public struct UploadCheck
    {
        public bool Ok;
        public bool Cancelled;
        public string Error;
    }

    public static class ButlerUploader
    {
        public const string EditorPrefKey = "Pierotechnical.BuildTool.ButlerPath";
        public const string UploadCancelledMessage = "Upload cancelled.";
        const string LegacyPlayerPrefKey = "ButlerPath";
        const int StatusTimeoutMs = 60 * 1000;
        const int PushTimeoutMs = 2 * 60 * 60 * 1000;

        public static string GetButlerPath()
        {
            string path = EditorPrefs.GetString(EditorPrefKey, string.Empty);
            if (!string.IsNullOrEmpty(path))
                return path;

            string legacy = PlayerPrefs.GetString(LegacyPlayerPrefKey, string.Empty);
            if (string.IsNullOrEmpty(legacy))
                return string.Empty;

            EditorPrefs.SetString(EditorPrefKey, legacy);
            return legacy;
        }

        public static void SetButlerPath(string path)
        {
            EditorPrefs.SetString(EditorPrefKey, path ?? string.Empty);
        }

        public static string PromptForPath()
        {
            string extension = Application.platform == RuntimePlatform.WindowsEditor ? "exe" : string.Empty;
            string current = GetButlerPath();
            string directory = string.Empty;
            if (!string.IsNullOrEmpty(current))
                directory = Path.GetDirectoryName(current);

            string selected = EditorUtility.OpenFilePanel("Locate butler executable", directory ?? string.Empty, extension);
            if (!string.IsNullOrEmpty(selected))
                SetButlerPath(selected);

            return selected;
        }

        public static UploadCheck CheckLogin(string itchUser, string itchGame)
        {
            var check = new UploadCheck();
            string path = GetButlerPath();
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                check.Error = "Butler executable was not found. Use Locate Butler and choose the butler executable.";
                return check;
            }

            string target = ButlerCommand.ItchTarget(itchUser, itchGame);
            if (string.IsNullOrEmpty(BuildPaths.ToItchSlug(itchUser)) || string.IsNullOrEmpty(BuildPaths.ToItchSlug(itchGame)))
            {
                check.Error = "Enter an itch username and game title before uploading.";
                return check;
            }

            ProcessRunResult status = ProcessRunner.Run(path, ButlerCommand.StatusArguments(itchUser, itchGame), StatusTimeoutMs, "Checking " + target);
            if (status.Cancelled)
            {
                check.Cancelled = true;
                check.Error = "Butler check was cancelled.";
                return check;
            }

            if (status.TimedOut)
            {
                check.Error = "butler status " + target + " timed out.";
                return check;
            }

            if (status.ExitCode != 0)
            {
                check.Error = ButlerCommand.DescribeStatusFailure(CombineOutput(status));
                return check;
            }

            check.Ok = true;
            return check;
        }

        public static bool TryPush(BuildRequest request, string folder, out string error)
        {
            error = null;
            string path = GetButlerPath();
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                error = "Butler executable was not found.";
                return false;
            }

            string args = ButlerCommand.PushArguments(
                folder,
                request.ItchUser,
                request.ItchGame,
                request.ChannelName,
                request.Version);

            ProcessRunResult push = ProcessRunner.Run(path, args, PushTimeoutMs, "Uploading " + request.Label + " to itch.io");
            if (push.Cancelled)
            {
                error = UploadCancelledMessage;
                return false;
            }

            if (push.TimedOut)
            {
                error = "Butler upload timed out." + Environment.NewLine + CombineOutput(push);
                return false;
            }

            if (push.ExitCode != 0)
            {
                error = CombineOutput(push);
                return false;
            }

            return true;
        }

        public static string CombineOutput(ProcessRunResult result)
        {
            var builder = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(result.StandardError))
                builder.AppendLine(result.StandardError.Trim());
            if (!string.IsNullOrWhiteSpace(result.StandardOutput))
                builder.AppendLine(result.StandardOutput.Trim());
            if (builder.Length == 0)
                builder.Append("Butler exited with code ").Append(result.ExitCode).Append('.');

            return builder.ToString().Trim();
        }
    }
}
