using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Pierotechnical.BuildAndUploadTool.Editor
{
    public sealed class SteamDepotQuery
    {
        public bool Ok;
        public bool Cancelled;
        public string Error;
        public SteamDepotLookup Depots;
    }

    public static class SteamUploader
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
            string extension = Application.platform == RuntimePlatform.WindowsEditor ? "exe" : string.Empty;
            string current = GetSteamCmdPath();
            string directory = string.Empty;
            if (!string.IsNullOrEmpty(current))
                directory = Path.GetDirectoryName(current);

            string selected = EditorUtility.OpenFilePanel("Locate steamcmd executable", directory ?? string.Empty, extension);
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

        public static UploadCheck CheckLogin(string steamcmdPath, string username)
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
            {
                check.Error = SteamCommand.MissingCacheMessage;
                return check;
            }

            ProcessRunResult status = ProcessRunner.Run(
                steamcmdPath,
                SteamCommand.LoginCheckArguments(username),
                LoginTimeoutMs,
                "Checking Steam login",
                SteamCmdDirectory(steamcmdPath));
            if (status.Cancelled)
            {
                check.Cancelled = true;
                check.Error = "Steam login check was cancelled.";
                return check;
            }

            if (status.TimedOut)
            {
                check.Error = SteamCommand.LoginRequiredMessage + Environment.NewLine + "steamcmd did not finish logging in.";
                return check;
            }

            if (status.ExitCode != 0)
            {
                check.Error = SteamCommand.DescribeLoginFailure(CombineOutput(status));
                return check;
            }

            check.Ok = true;
            return check;
        }

        public static SteamDepotQuery LookupDepots(string steamcmdPath, string username, string appId)
        {
            var query = new SteamDepotQuery();
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

            string canonicalAppId;
            if (!SteamCommand.TryParseSteamId(appId, out canonicalAppId))
            {
                query.Error = "Steam App ID must be a positive integer.";
                return query;
            }

            if (!HasSavedLogin(steamcmdPath, username))
            {
                query.Error = SteamCommand.MissingCacheMessage;
                return query;
            }

            ProcessRunResult info = ProcessRunner.Run(
                steamcmdPath,
                SteamCommand.AppInfoArguments(username, canonicalAppId),
                LoginTimeoutMs,
                "Looking up Steam depots",
                SteamCmdDirectory(steamcmdPath));
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
                query.Error = SteamCommand.DescribeLoginFailure(CombineOutput(info));
                return query;
            }

            query.Depots = SteamCommand.ParseAppDepots(CombineOutput(info));
            query.Ok = true;
            return query;
        }

        public static bool TryPublish(BuildQueueState state, out string message)
        {
            message = null;
            if (state == null)
            {
                message = "Steam upload was missing its queue.";
                return false;
            }

            BuildRequest sample = FirstSteamRequest(state.Pending);
            if (sample == null)
            {
                message = "No Steam upload was requested.";
                return false;
            }

            List<SteamDepotUpload> depots = SteamCommand.SelectDepots(state.Pending, state.Completed);
            if (depots.Count == 0)
            {
                message = "No successful desktop build was available for Steam.";
                return false;
            }

            for (int i = 0; i < depots.Count; i++)
            {
                if (string.IsNullOrEmpty(depots[i].DepotId))
                {
                    message = depots[i].Label + " depot ID must be a positive integer.";
                    return false;
                }
            }

            string duplicate;
            if (SteamCommand.TryFindDuplicateDepotId(depots, out duplicate))
            {
                message = "Depot " + duplicate + " is used by more than one platform.";
                return false;
            }

            string appId;
            if (!SteamCommand.TryParseSteamId(sample.SteamAppId, out appId))
            {
                message = "Steam App ID must be a positive integer.";
                return false;
            }

            bool everySucceeded = SteamCommand.EverySteamTargetSucceeded(state.Pending, state.Completed);
            string setlive = SteamCommand.ResolveSetLive(sample.SteamBranch, everySucceeded);
            string withheld = SteamCommand.DescribeUnsetLive(sample.SteamBranch, everySucceeded, appId);

            string work = BuildPaths.SteamWorkFolder(sample.OutputRoot, state.Id);
            string cacheDir = BuildPaths.SteamCacheFolder(sample.OutputRoot, appId);
            string appPath = Path.Combine(work, "app_build.vdf");
            try
            {
                Directory.CreateDirectory(work);
                Directory.CreateDirectory(cacheDir);
                for (int i = 0; i < depots.Count; i++)
                {
                    string depotPath = Path.Combine(work, depots[i].ScriptName);
                    File.WriteAllText(depotPath, SteamCommand.BuildDepotScript(depots[i]), new UTF8Encoding(false));
                }

                string description = SteamCommand.DescribeBuild(sample.Version, depots);
                File.WriteAllText(
                    appPath,
                    SteamCommand.BuildAppScript(appId, description, cacheDir, setlive, depots),
                    new UTF8Encoding(false));
            }
            catch (Exception exception)
            {
                message = "Could not write Steam scripts: " + exception.Message;
                return false;
            }

            string steamcmd = sample.SteamCmdPath;
            if (string.IsNullOrEmpty(steamcmd) || !File.Exists(steamcmd))
            {
                message = "steamcmd executable was not found. Scripts left at " + work + ".";
                return false;
            }

            string args = SteamCommand.UploadArguments(sample.SteamUser, appPath);
            BuildLog.Append(sample.OutputRoot, "steamcmd " + args);
            ProcessRunResult upload = ProcessRunner.Run(
                steamcmd,
                args,
                UploadTimeoutMs,
                "Uploading to Steam",
                SteamCmdDirectory(steamcmd));
            string uploadOutput = CombineOutput(upload);
            BuildLog.Append(sample.OutputRoot, uploadOutput);

            if (upload.Cancelled)
            {
                message = AppendWithheld("Steam upload cancelled. Check the Steamworks backend before uploading again.", withheld);
                return false;
            }

            if (upload.TimedOut)
            {
                message = AppendWithheld("Steam upload timed out. Scripts left at " + work + ".", withheld);
                return false;
            }

            if (upload.ExitCode != 0)
            {
                message = AppendWithheld("Steam upload failed. Scripts left at " + work + ".", withheld);
                return false;
            }

            string buildId;
            SteamCommand.TryParseBuildId(uploadOutput, out buildId);
            string success = SuccessMessage(appId, setlive, buildId);
            string deleteError;
            if (!TryDeleteDirectory(work, out deleteError))
            {
                message = AppendWithheld(success + " Could not remove " + work + ": " + deleteError, withheld);
                return true;
            }

            message = AppendWithheld(success, withheld);
            return true;
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

        static BuildRequest FirstSteamRequest(IList<BuildRequest> pending)
        {
            if (pending == null)
                return null;

            for (int i = 0; i < pending.Count; i++)
            {
                if (pending[i] != null && pending[i].PublishSteam)
                    return pending[i];
            }

            return null;
        }

        static string CombineOutput(ProcessRunResult result)
        {
            var builder = new StringBuilder();
            if (result != null && !string.IsNullOrWhiteSpace(result.StandardError))
                builder.AppendLine(result.StandardError.Trim());
            if (result != null && !string.IsNullOrWhiteSpace(result.StandardOutput))
                builder.AppendLine(result.StandardOutput.Trim());
            if (builder.Length == 0)
                builder.Append("steamcmd exited with code ").Append(result == null ? -1 : result.ExitCode).Append('.');

            return builder.ToString().Trim();
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
