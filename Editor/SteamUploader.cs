using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEditor;

namespace Pierotechnical.BuildAndUploadTool.Editor
{
    internal sealed class SteamDepotQuery
    {
        public bool Ok;
        public bool Cancelled;
        public string Error;
        public SteamDepotLookup Depots;
    }

    internal static class SteamUploader
    {
        public const string EditorPrefKey = "Pierotechnical.BuildTool.SteamCmdPath";
        const int LoginTimeoutMs = 60 * 1000;
        const int UploadTimeoutMs = 2 * 60 * 60 * 1000;
        const int DeleteAttempts = 5;

        public static string GetSteamCmdPath()
        {
            return EditorPrefs.GetString(EditorPrefKey, string.Empty);
        }

        public static void SetSteamCmdPath(string path)
        {
            EditorPrefs.SetString(EditorPrefKey, path ?? string.Empty);
        }

        public static string PromptForPath()
        {
            string selected = ExecutablePrompt.Choose("Locate steamcmd executable", GetSteamCmdPath());
            if (!string.IsNullOrEmpty(selected))
                SetSteamCmdPath(selected);

            return selected;
        }

        public static void OpenLogin(string steamcmdPath, string username)
        {
            var start = new ProcessStartInfo();
            start.FileName = steamcmdPath;
            start.Arguments = SteamCommand.InteractiveLoginArguments(username);
            start.UseShellExecute = true;
            string directory = Path.GetDirectoryName(steamcmdPath);
            if (!string.IsNullOrEmpty(directory))
                start.WorkingDirectory = directory;

            Process.Start(start);
        }

        internal static IPublishOperation CheckLoginAsync(
            string steamcmdPath,
            string username,
            Action<UploadCheck> onComplete)
        {
            if (onComplete == null)
                return CompletedPublishOperation.Instance;

            UploadCheck prepared = PrepareLoginCheck(steamcmdPath, username);
            if (!string.IsNullOrEmpty(prepared.Error) || prepared.Ok)
            {
                onComplete(prepared);
                return CompletedPublishOperation.Instance;
            }

            return ProcessRunner.RunAsync(
                steamcmdPath,
                SteamCommand.LoginCheckArguments(username),
                LoginTimeoutMs,
                SteamCmdDirectory(steamcmdPath),
                status => onComplete(FinishLoginCheck(status)));
        }

        static UploadCheck PrepareLoginCheck(string steamcmdPath, string username)
        {
            var check = new UploadCheck();
            if (string.IsNullOrEmpty(steamcmdPath) || !File.Exists(steamcmdPath))
            {
                check.Error = "steamcmd executable was not found. Use Locate steamcmd and choose the steamcmd executable.";
                return check;
            }

            if (string.IsNullOrWhiteSpace(username))
            {
                check.Error = "Enter a Steam username before uploading.";
                return check;
            }

            if (!HasSavedLogin(steamcmdPath, username))
                check.Error = SteamCommand.MissingCacheMessage;

            return check;
        }

        static UploadCheck FinishLoginCheck(ProcessRunResult status)
        {
            var check = new UploadCheck();
            if (status == null || status.TimedOut)
            {
                check.Error = SteamCommand.LoginRequiredMessage + Environment.NewLine + "steamcmd did not finish logging in.";
                return check;
            }

            if (status.Cancelled)
            {
                check.Cancelled = true;
                check.Error = "Steam login check was cancelled.";
                return check;
            }

            if (status.ExitCode != 0)
            {
                check.Error = SteamCommand.DescribeLoginFailure(ProcessRunner.CombineOutput(status, "steamcmd"));
                return check;
            }

            check.Ok = true;
            return check;
        }

        public static void LookupDepotsAsync(string steamcmdPath, string username, string appId, Action<SteamDepotQuery> onComplete)
        {
            if (onComplete == null)
                return;

            string canonicalAppId;
            SteamDepotQuery prepared = PrepareDepotLookup(steamcmdPath, username, appId, out canonicalAppId);
            if (!string.IsNullOrEmpty(prepared.Error))
            {
                onComplete(prepared);
                return;
            }

            ProcessRunner.RunAsync(
                steamcmdPath,
                SteamCommand.AppInfoArguments(username, canonicalAppId),
                LoginTimeoutMs,
                SteamCmdDirectory(steamcmdPath),
                info => onComplete(FinishDepotLookup(info)));
        }

        static SteamDepotQuery PrepareDepotLookup(string steamcmdPath, string username, string appId, out string canonicalAppId)
        {
            var query = new SteamDepotQuery();
            canonicalAppId = null;
            if (string.IsNullOrEmpty(steamcmdPath) || !File.Exists(steamcmdPath))
            {
                query.Error = "steamcmd executable was not found. Use Locate steamcmd and choose the steamcmd executable.";
                return query;
            }

            if (string.IsNullOrWhiteSpace(username))
            {
                query.Error = "Enter a Steam username before looking up depots.";
                return query;
            }

            if (!SteamCommand.TryParseSteamId(appId, out canonicalAppId))
            {
                query.Error = "Steam App ID must be a positive integer.";
                return query;
            }

            if (!HasSavedLogin(steamcmdPath, username))
                query.Error = SteamCommand.MissingCacheMessage;

            return query;
        }

        static SteamDepotQuery FinishDepotLookup(ProcessRunResult info)
        {
            var query = new SteamDepotQuery();
            if (info == null)
            {
                query.Error = "steamcmd did not finish looking up depots.";
                return query;
            }

            if (info.Cancelled)
            {
                query.Cancelled = true;
                query.Error = "Steam depot lookup was cancelled.";
                return query;
            }

            if (info.TimedOut)
            {
                query.Error = "steamcmd did not finish looking up depots.";
                return query;
            }

            if (info.ExitCode != 0)
            {
                query.Error = SteamCommand.DescribeLoginFailure(ProcessRunner.CombineOutput(info, "steamcmd"));
                return query;
            }

            query.Depots = SteamCommand.ParseAppDepots(ProcessRunner.CombineOutput(info, "steamcmd"));
            query.Ok = true;
            return query;
        }

        internal static IPublishOperation BeginPublish(
            PublishJob job,
            PublishExecutionContext context,
            Action<PublishOperationResult> onComplete)
        {
            if (job == null || job.Steam == null || context == null || context.Plan == null)
                return Complete(
                    onComplete,
                    PublishJobStatus.Failed,
                    "Steam upload was missing its publish job.");

            SteamPublishPayload payload = job.Steam;
            List<SteamDepotUpload> depots = SteamCommand.SelectDepots(
                payload,
                context.Plan,
                context.TargetResults);
            if (depots.Count == 0)
                return Complete(
                    onComplete,
                    PublishJobStatus.Skipped,
                    "No successful desktop build was available for Steam.");

            string duplicate;
            if (SteamCommand.TryFindDuplicateDepotId(depots, out duplicate))
                return Complete(
                    onComplete,
                    PublishJobStatus.Failed,
                    "Depot " + duplicate + " is used by more than one platform.");

            for (int i = 0; i < depots.Count; i++)
            {
                string pathError;
                if (!SafeFileSystem.TryEnsureLinkFree(
                    context.Plan.OutputRoot,
                    depots[i].ContentRoot,
                    true,
                    out pathError))
                {
                    return Complete(
                        onComplete,
                        PublishJobStatus.Failed,
                        pathError);
                }
            }

            string appId;
            if (!SteamCommand.TryParseSteamId(payload.AppId, out appId))
                return Complete(
                    onComplete,
                    PublishJobStatus.Failed,
                    "Steam App ID must be a positive integer.");

            bool everySucceeded = SteamCommand.EverySteamTargetSucceeded(
                payload,
                context.TargetResults);
            string setlive = SteamCommand.ResolveSetLive(payload.Branch, everySucceeded);
            string withheld = SteamCommand.DescribeUnsetLive(payload.Branch, everySucceeded, appId);
            string work = BuildPaths.SteamWorkFolder(context.Plan.OutputRoot, context.QueueId);
            string cacheDir = BuildPaths.SteamCacheFolder(context.Plan.OutputRoot, appId);
            string appPath = Path.Combine(work, "app_build.vdf");

            try
            {
                string pathError;
                if (!SafeFileSystem.TryEnsureLinkFree(
                        context.Plan.OutputRoot,
                        work,
                        false,
                        out pathError)
                    || !SafeFileSystem.TryEnsureLinkFree(
                        context.Plan.OutputRoot,
                        cacheDir,
                        false,
                        out pathError))
                {
                    return Complete(
                        onComplete,
                        PublishJobStatus.Failed,
                        "Could not prepare Steam paths: " + pathError);
                }

                Directory.CreateDirectory(work);
                Directory.CreateDirectory(cacheDir);
                for (int i = 0; i < depots.Count; i++)
                {
                    string depotPath = Path.Combine(work, depots[i].ScriptName);
                    File.WriteAllText(
                        depotPath,
                        SteamCommand.BuildDepotScript(depots[i]),
                        new UTF8Encoding(false));
                }

                File.WriteAllText(
                    appPath,
                    SteamCommand.BuildAppScript(
                        appId,
                        SteamCommand.DescribeBuild(context.Plan.Version, depots),
                        cacheDir,
                        setlive,
                        depots),
                    new UTF8Encoding(false));
            }
            catch (Exception exception)
            {
                return Complete(
                    onComplete,
                    PublishJobStatus.Failed,
                    "Could not write Steam scripts: " + exception.Message);
            }

            if (string.IsNullOrEmpty(payload.SteamCmdPath) || !File.Exists(payload.SteamCmdPath))
                return Complete(
                    onComplete,
                    PublishJobStatus.Failed,
                    "steamcmd executable was not found. Scripts left at " + work + ".");

            string args = SteamCommand.UploadArguments(payload.Username, appPath);
            BuildLog.Append(
                context.Plan.OutputRoot,
                context.QueueId,
                "steamcmd " + args);
            return ProcessRunner.RunAsync(
                payload.SteamCmdPath,
                args,
                UploadTimeoutMs,
                SteamCmdDirectory(payload.SteamCmdPath),
                line => BuildLog.Append(
                    context.Plan.OutputRoot,
                    context.QueueId,
                    "steamcmd: " + line),
                upload =>
                {
                    string uploadOutput = ProcessRunner.CombineOutput(upload, "steamcmd");
                    if (upload.Cancelled)
                    {
                        onComplete(new PublishOperationResult
                        {
                            Status = PublishJobStatus.Cancelled,
                            Message = AppendWithheld(
                                "Steam upload cancelled. Check the Steamworks backend before uploading again."
                                    + TerminationNote(upload),
                                withheld),
                            Process = upload
                        });
                        return;
                    }

                    if (upload.TimedOut)
                    {
                        onComplete(new PublishOperationResult
                        {
                            Status = PublishJobStatus.Failed,
                            Message = AppendWithheld(
                                "Steam upload timed out. Scripts left at " + work + ". "
                                    + uploadOutput,
                                withheld),
                            Process = upload
                        });
                        return;
                    }

                    string buildId;
                    if (upload.ExitCode != 0
                        || !SteamCommand.HasUploadSuccess(uploadOutput, out buildId))
                    {
                        onComplete(new PublishOperationResult
                        {
                            Status = PublishJobStatus.Failed,
                            Message = AppendWithheld(
                                "Steam upload did not report a successful SteamPipe build. Scripts left at "
                                    + work + ". " + uploadOutput,
                                withheld),
                            Process = upload
                        });
                        return;
                    }

                    string success = SuccessMessage(appId, setlive, buildId);
                    string deleteError;
                    if (!TryDeleteDirectory(work, out deleteError))
                    {
                        success += " Could not remove " + work + ": " + deleteError;
                    }

                    onComplete(new PublishOperationResult
                    {
                        Status = PublishJobStatus.Succeeded,
                        Message = AppendWithheld(success, withheld),
                        Process = upload
                    });
                });
        }

        static IPublishOperation Complete(
            Action<PublishOperationResult> onComplete,
            PublishJobStatus status,
            string message)
        {
            if (onComplete != null)
                onComplete(new PublishOperationResult { Status = status, Message = message });
            return CompletedPublishOperation.Instance;
        }

        static string TerminationNote(ProcessRunResult result)
        {
            return result != null && result.TerminationUnconfirmed
                ? " Process-tree termination could not be confirmed."
                : string.Empty;
        }

        static string SuccessMessage(string appId, string setlive, string buildId)
        {
            string message = string.IsNullOrEmpty(setlive)
                ? "Uploaded to Steam app " + appId + " without setting a branch live."
                : "Uploaded to Steam app " + appId + " and set live on branch " + setlive + ".";
            if (!string.IsNullOrEmpty(buildId))
                message += " BuildID " + buildId + ".";

            return message + " " + SteamCommand.BuildsPageUrl(appId);
        }

        static bool HasSavedLogin(string steamcmdPath, string username)
        {
            string directory = SteamCmdDirectory(steamcmdPath);
            if (string.IsNullOrEmpty(directory))
                return false;

            string configPath = Path.Combine(directory, "config", "config.vdf");
            if (!File.Exists(configPath))
                return false;

            try
            {
                return SteamCommand.HasCachedAccount(File.ReadAllText(configPath), username);
            }
            catch (Exception)
            {
                return false;
            }
        }

        static string SteamCmdDirectory(string steamcmdPath)
        {
            if (string.IsNullOrEmpty(steamcmdPath))
                return string.Empty;

            return Path.GetDirectoryName(steamcmdPath) ?? string.Empty;
        }

        static string AppendWithheld(string message, string withheld)
        {
            if (string.IsNullOrEmpty(withheld))
                return message;

            return message + " " + withheld;
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
    }
}
