using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Pierotechnical.BuildAndUploadTool.Editor
{
    internal struct UploadCheck
    {
        public bool Ok;
        public bool Cancelled;
        public string Error;
    }

    internal struct ItchChannelQuery
    {
        public bool Ok;
        public bool Cancelled;
        public string Error;
        public List<string> Channels;
    }

    internal static class ButlerUploader
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
            string selected = ExecutablePrompt.Choose("Locate butler executable", GetButlerPath());
            if (!string.IsNullOrEmpty(selected))
                SetButlerPath(selected);

            return selected;
        }

        public static void OpenLogin(string butlerPath)
        {
            var start = new ProcessStartInfo();
            start.FileName = butlerPath;
            start.Arguments = ButlerCommand.LoginArguments();
            start.UseShellExecute = true;
            string directory = Path.GetDirectoryName(butlerPath);
            if (!string.IsNullOrEmpty(directory))
                start.WorkingDirectory = directory;

            Process.Start(start);
        }

        internal static IPublishOperation CheckLoginAsync(
            string itchUser,
            string itchGame,
            Action<UploadCheck> onComplete)
        {
            return CheckLoginAsync(GetButlerPath(), itchUser, itchGame, onComplete);
        }

        internal static IPublishOperation CheckLoginAsync(
            string butlerPath,
            string itchUser,
            string itchGame,
            Action<UploadCheck> onComplete)
        {
            if (onComplete == null)
                return CompletedPublishOperation.Instance;

            string target;
            UploadCheck prepared = PrepareLoginCheck(
                butlerPath,
                itchUser,
                itchGame,
                out target);
            if (!string.IsNullOrEmpty(prepared.Error) || prepared.Ok)
            {
                onComplete(prepared);
                return CompletedPublishOperation.Instance;
            }

            return ProcessRunner.RunAsync(
                butlerPath,
                ButlerCommand.StatusArguments(itchUser, itchGame),
                StatusTimeoutMs,
                null,
                status => onComplete(FinishLoginCheck(status, target)));
        }

        static UploadCheck PrepareLoginCheck(
            string butlerPath,
            string itchUser,
            string itchGame,
            out string target)
        {
            var check = new UploadCheck();
            target = ButlerCommand.ItchTarget(itchUser, itchGame);
            if (string.IsNullOrEmpty(butlerPath) || !File.Exists(butlerPath))
            {
                check.Error = "Butler executable was not found. Use Locate Butler and choose the butler executable.";
                return check;
            }

            if (string.IsNullOrEmpty(BuildPaths.ToItchSlug(itchUser)) || string.IsNullOrEmpty(BuildPaths.ToItchSlug(itchGame)))
            {
                check.Error = "Enter an itch username and game title before uploading.";
                return check;
            }

            return check;
        }

        static UploadCheck FinishLoginCheck(ProcessRunResult status, string target)
        {
            var check = new UploadCheck();
            if (status == null)
            {
                check.Error = "butler status " + target + " timed out.";
                return check;
            }

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
                check.Error = ButlerCommand.DescribeStatusFailure(ProcessRunner.CombineOutput(status, "Butler"));
                return check;
            }

            check.Ok = true;
            return check;
        }

        public static void LookupChannelsAsync(string butlerPath, string itchUser, string itchGame, Action<ItchChannelQuery> onComplete)
        {
            if (onComplete == null)
                return;

            ItchChannelQuery prepared = PrepareChannelLookup(butlerPath, itchUser, itchGame);
            if (!string.IsNullOrEmpty(prepared.Error))
            {
                onComplete(prepared);
                return;
            }

            ProcessRunner.RunAsync(
                butlerPath,
                ButlerCommand.StatusJsonArguments(itchUser, itchGame),
                StatusTimeoutMs,
                null,
                status => onComplete(FinishChannelLookup(status)));
        }

        static ItchChannelQuery PrepareChannelLookup(string butlerPath, string itchUser, string itchGame)
        {
            var query = new ItchChannelQuery();
            if (string.IsNullOrEmpty(butlerPath) || !File.Exists(butlerPath))
            {
                query.Error = "Butler executable was not found. Use Locate Butler and choose the butler executable.";
                return query;
            }

            if (string.IsNullOrEmpty(BuildPaths.ToItchSlug(itchUser)) || string.IsNullOrEmpty(BuildPaths.ToItchSlug(itchGame)))
                query.Error = "Enter an itch username and game title before looking up channels.";

            return query;
        }

        static ItchChannelQuery FinishChannelLookup(ProcessRunResult status)
        {
            var query = new ItchChannelQuery();
            if (status == null)
            {
                query.Error = "butler status timed out.";
                return query;
            }

            if (status.Cancelled)
            {
                query.Cancelled = true;
                query.Error = "Channel lookup was cancelled.";
                return query;
            }

            if (status.TimedOut)
            {
                query.Error = "butler status timed out.";
                return query;
            }

            if (status.ExitCode != 0)
            {
                query.Error = ButlerCommand.DescribeStatusFailure(ProcessRunner.CombineOutput(status, "Butler"));
                return query;
            }

            query.Channels = ButlerCommand.ParseStatusChannels(status.StandardOutput);
            query.Ok = true;
            return query;
        }

        internal static IPublishOperation BeginPush(
            ItchPublishPayload payload,
            string label,
            string folder,
            string outputRoot,
            string queueId,
            Action<PublishOperationResult> onComplete)
        {
            if (payload == null
                || string.IsNullOrEmpty(payload.ButlerPath)
                || !File.Exists(payload.ButlerPath))
            {
                onComplete(new PublishOperationResult
                {
                    Status = PublishJobStatus.Failed,
                    Message = "Butler executable was not found."
                });
                return CompletedPublishOperation.Instance;
            }
            string pathError = null;
            if (!Directory.Exists(folder)
                || !SafeFileSystem.TryEnsureLinkFree(
                    outputRoot,
                    folder,
                    true,
                    out pathError))
            {
                onComplete(new PublishOperationResult
                {
                    Status = PublishJobStatus.Failed,
                    Message = !Directory.Exists(folder)
                        ? "Build folder was not found: " + folder
                        : pathError
                });
                return CompletedPublishOperation.Instance;
            }

            string args = ButlerCommand.PushArguments(
                folder,
                payload.Owner,
                payload.Project,
                payload.Channel,
                payload.Version);
            BuildLog.Append(outputRoot, queueId, "butler " + args);
            return ProcessRunner.RunAsync(
                payload.ButlerPath,
                args,
                PushTimeoutMs,
                null,
                line => BuildLog.Append(outputRoot, queueId, "butler: " + line),
                push =>
                {
                    string output = ProcessRunner.CombineOutput(push, "Butler");
                    PublishJobStatus status = push.Cancelled
                        ? PublishJobStatus.Cancelled
                        : push.TimedOut || push.ExitCode != 0
                            ? PublishJobStatus.Failed
                            : PublishJobStatus.Succeeded;
                    string message = status == PublishJobStatus.Succeeded
                        ? "Uploaded " + label + " to "
                            + payload.Owner + "/" + payload.Project + ":" + payload.Channel + "."
                        : push.Cancelled
                            ? UploadCancelledMessage + TerminationNote(push)
                            : push.TimedOut
                                ? "Butler upload timed out. " + output
                                : output;
                    onComplete(new PublishOperationResult
                    {
                        Status = status,
                        Message = message,
                        Process = push
                    });
                });
        }

        static string TerminationNote(ProcessRunResult result)
        {
            return result != null && result.TerminationUnconfirmed
                ? " Process-tree termination could not be confirmed."
                : string.Empty;
        }
    }
}
