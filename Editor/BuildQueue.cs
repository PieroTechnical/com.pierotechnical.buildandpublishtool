using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Pierotechnical.BuildAndUploadTool.Editor
{
    [InitializeOnLoad]
    public static class BuildQueue
    {
        const string SessionKey = "Pierotechnical.BuildAndUploadTool.Queue";
        const int SwitchTimeoutSeconds = 300;

        static readonly string DomainToken = Guid.NewGuid().ToString("N");
        static bool steamUploadInProgress;

        static BuildQueue()
        {
            EditorApplication.delayCall += ResumeIfNeeded;
        }

        public static bool HasPending()
        {
            return IsActive(Load());
        }

        static bool IsActive(BuildQueueState state)
        {
            if (state == null || state.Pending == null || state.Pending.Count == 0)
                return false;
            if (state.Completed == null || state.Completed.Count < state.Pending.Count)
                return true;
            if (state.SteamUploadFinished)
                return false;

            return SteamCommand.HasPublishableBuild(state.Pending, state.Completed);
        }

        public static void Start(List<BuildRequest> requests, string notice)
        {
            var state = new BuildQueueState();
            state.Id = Guid.NewGuid().ToString("N");
            state.Notice = notice ?? string.Empty;
            state.Pending = requests ?? new List<BuildRequest>();
            state.Completed = new List<TargetResult>();
            state.SwitchAttemptedIndex = -1;
            state.BuildAttemptedIndex = -1;
            Save(state);

            if (state.Pending.Count > 0)
                BuildLog.Reset(state.Pending[0].OutputRoot, state.Notice);

            Debug.Log("Starting build queue for " + state.Pending.Count + " platform(s).");
            Schedule(state.Id);
        }

        public static void Clear()
        {
            SessionState.EraseString(SessionKey);
        }

        static void ResumeIfNeeded()
        {
            BuildQueueState state = Load();
            if (state == null || string.IsNullOrEmpty(state.Id))
                return;
            if (!IsActive(state))
                return;

            Step(state.Id);
        }

        static void Schedule(string id)
        {
            EditorApplication.delayCall += () => Step(id);
        }

        static void Step(string id)
        {
            try
            {
                StepCore(id);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                BuildQueueState state = Load();
                if (state != null && state.Id == id)
                {
                    state.Notice = AppendNotice(state.Notice, exception.Message);
                    Finish(state);
                }
            }
        }

        static void StepCore(string id)
        {
            BuildQueueState state = Load();
            if (state == null || state.Id != id)
                return;

            if (state.Pending == null || state.Pending.Count == 0)
            {
                Clear();
                return;
            }

            if (state.Completed == null)
                state.Completed = new List<TargetResult>();

            if (state.Completed.Count >= state.Pending.Count)
            {
                ResolveSteamUpload(state);
                return;
            }

            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                Schedule(id);
                return;
            }

            if (!ResolveUploadGate(state))
                return;
            if (!CommitVersion(state))
                return;
            if (!SaveScenes(state))
                return;

            int index = state.Completed.Count;
            BuildRequest request = state.Pending[index];
            if (request == null)
            {
                RecordAndContinue(state, new TargetResult { Label = "Build", Message = "Build request was missing." });
                return;
            }

            PlatformCatalog.Entry platform = PlatformCatalog.Find(request.PlatformId);
            if (platform == null)
            {
                RecordAndContinue(state, Failure(request, "Unknown platform '" + request.PlatformId + "'."));
                return;
            }

            BuildTargetGroup group = PlatformCatalog.Group(platform.Target);
            if (!BuildPipeline.IsBuildTargetSupported(group, platform.Target))
            {
                RecordAndContinue(state, Failure(request, request.Label + " build support is not installed in this Editor."));
                return;
            }

            if (EditorUserBuildSettings.activeBuildTarget != platform.Target)
            {
                RequestTargetSwitch(state, request, index, group, platform.Target);
                return;
            }

            // A target switch can apply before the domain reloads. Do not start
            // BuildPlayer in that same domain; wait one step so a pending reload wins.
            bool switchRequestedHere = state.SwitchAttemptedIndex == index && state.SwitchDomainToken == DomainToken;
            if (switchRequestedHere && !state.SwitchObserved)
            {
                state.SwitchObserved = true;
                Save(state);
                Schedule(state.Id);
                return;
            }

            if (state.BuildAttemptedIndex == index)
            {
                RecordAndContinue(state, Failure(
                    request,
                    "The Editor reloaded during the " + request.Label + " build before it finished. The previous versioned folder was left in place. Run this target again."));
                return;
            }

            state.BuildAttemptedIndex = index;
            Save(state);
            TargetResult result = ProjectBuilder.Execute(request);
            RecordAndContinue(state, result);
        }

        static bool ResolveUploadGate(BuildQueueState state)
        {
            if (state.UploadGateResolved)
                return true;

            if (!ResolveItchGate(state))
                return false;
            if (!ResolveSteamGate(state))
                return false;

            state.UploadGateResolved = true;
            Save(state);
            return true;
        }

        static bool ResolveItchGate(BuildQueueState state)
        {
            BuildRequest uploadRequest = null;
            for (int i = 0; i < state.Pending.Count; i++)
            {
                if (state.Pending[i] != null && state.Pending[i].PublishItch)
                {
                    uploadRequest = state.Pending[i];
                    break;
                }
            }

            if (uploadRequest == null)
                return true;

            UploadCheck check = ButlerUploader.CheckLogin(uploadRequest.ItchUser, uploadRequest.ItchGame);
            if (check.Cancelled)
            {
                state.Notice = AppendNotice(state.Notice, check.Error);
                Finish(state);
                return false;
            }

            if (check.Ok)
                return true;

            bool proceed = EditorUtility.DisplayDialog(
                "Butler",
                check.Error + "\n\nBuilds will continue without uploading to itch.io.",
                "Build anyway",
                "Cancel");
            if (!proceed)
            {
                state.Notice = AppendNotice(state.Notice, check.Error);
                Finish(state);
                return false;
            }

            for (int i = 0; i < state.Pending.Count; i++)
            {
                if (state.Pending[i] != null)
                    state.Pending[i].PublishItch = false;
            }

            state.Notice = AppendNotice(state.Notice, check.Error + " Builds will not be uploaded to itch.io.");
            Save(state);
            return true;
        }

        static bool ResolveSteamGate(BuildQueueState state)
        {
            BuildRequest uploadRequest = null;
            for (int i = 0; i < state.Pending.Count; i++)
            {
                if (state.Pending[i] != null && state.Pending[i].PublishSteam)
                {
                    uploadRequest = state.Pending[i];
                    break;
                }
            }

            if (uploadRequest == null)
                return true;

            UploadCheck check = SteamUploader.CheckLogin(uploadRequest.SteamCmdPath, uploadRequest.SteamUser);
            if (check.Cancelled)
            {
                state.Notice = AppendNotice(state.Notice, check.Error);
                Finish(state);
                return false;
            }

            if (check.Ok)
                return true;

            bool proceed = EditorUtility.DisplayDialog(
                "Steam",
                check.Error + "\n\nBuilds will continue without uploading to Steam.",
                "Build anyway",
                "Cancel");
            if (!proceed)
            {
                state.Notice = AppendNotice(state.Notice, check.Error);
                Finish(state);
                return false;
            }

            for (int i = 0; i < state.Pending.Count; i++)
            {
                if (state.Pending[i] != null)
                    state.Pending[i].PublishSteam = false;
            }

            state.Notice = AppendNotice(state.Notice, check.Error + " Builds will not be uploaded to Steam.");
            Save(state);
            return true;
        }

        static void ResolveSteamUpload(BuildQueueState state)
        {
            if (!SteamCommand.HasPublishableBuild(state.Pending, state.Completed))
            {
                state.SteamUploadFinished = true;
                Save(state);
                Finish(state);
                return;
            }

            if (state.SteamUploadAttempted)
            {
                if (steamUploadInProgress)
                    return;

                state.Notice = AppendNotice(state.Notice, SteamCommand.InterruptedMessage);
                state.SteamUploadFinished = true;
                Save(state);
                Finish(state);
                return;
            }

            state.SteamUploadAttempted = true;
            Save(state);
            steamUploadInProgress = true;
            try
            {
                string message;
                SteamUploader.TryPublish(state, out message);
                state.Notice = AppendNotice(state.Notice, message);
                state.SteamUploadFinished = true;
                Save(state);
                Finish(state);
            }
            finally
            {
                steamUploadInProgress = false;
            }
        }

        static bool CommitVersion(BuildQueueState state)
        {
            if (state.VersionCommitted)
                return true;

            string version = state.Pending[0] == null ? string.Empty : state.Pending[0].Version;
            string error;
            if (!ToolSettings.TryCommitVersion(version, out error))
            {
                state.Notice = AppendNotice(state.Notice, error);
                Finish(state);
                return false;
            }

            state.VersionCommitted = true;
            Save(state);
            Schedule(state.Id);
            return false;
        }

        static bool SaveScenes(BuildQueueState state)
        {
            if (state.ScenesSaved)
                return true;

            if (!EditorSceneManager.SaveOpenScenes())
            {
                state.Notice = AppendNotice(state.Notice, "Could not save open scenes.");
                Finish(state);
                return false;
            }

            state.ScenesSaved = true;
            Save(state);
            Schedule(state.Id);
            return false;
        }

        static void RequestTargetSwitch(BuildQueueState state, BuildRequest request, int index, BuildTargetGroup group, BuildTarget target)
        {
            bool requestedThisDomain = state.SwitchAttemptedIndex == index && state.SwitchDomainToken == DomainToken;
            bool requestedEarlierDomain = state.SwitchAttemptedIndex == index && state.SwitchDomainToken != DomainToken;

            if (requestedEarlierDomain)
            {
                RecordAndContinue(state, Failure(request, "Could not switch the active build target to " + request.Label + "."));
                return;
            }

            if (requestedThisDomain)
            {
                if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                {
                    Schedule(state.Id);
                    return;
                }

                DateTime started;
                if (!DateTime.TryParse(state.SwitchStartedUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out started))
                    started = DateTime.UtcNow;

                if ((DateTime.UtcNow - started).TotalSeconds > SwitchTimeoutSeconds)
                {
                    RecordAndContinue(state, Failure(request, "Timed out waiting for the Editor to switch to " + request.Label + "."));
                    return;
                }

                Schedule(state.Id);
                return;
            }

            state.SwitchAttemptedIndex = index;
            state.SwitchDomainToken = DomainToken;
            state.SwitchStartedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            state.SwitchObserved = false;
            Save(state);
            Debug.Log("Switching build target to " + request.Label + ".");

            bool switched;
            try
            {
                switched = EditorUserBuildSettings.SwitchActiveBuildTarget(group, target);
            }
            catch (Exception exception)
            {
                RecordAndContinue(state, Failure(request, exception.Message));
                return;
            }

            if (!switched)
            {
                RecordAndContinue(state, Failure(request, "Could not switch the active build target to " + request.Label + "."));
                return;
            }

            // A target switch can reload the domain immediately or at the end of the frame.
            // Build only on a later step, after the active target matches.
            Schedule(state.Id);
        }

        static void RecordAndContinue(BuildQueueState state, TargetResult result)
        {
            if (result == null)
                result = new TargetResult { Label = "Build", Message = "Missing build result." };

            state.Completed.Add(result);
            state.SwitchAttemptedIndex = -1;
            state.BuildAttemptedIndex = -1;
            state.SwitchDomainToken = string.Empty;
            state.SwitchObserved = false;
            Save(state);

            string outputRoot = state.Pending.Count > 0 && state.Pending[0] != null
                ? state.Pending[0].OutputRoot
                : string.Empty;
            BuildLog.Append(outputRoot, result.Label + ": " + result.Message);

            if (result.BuildSucceeded)
                Debug.Log(result.Message);
            else
                Debug.LogError(result.Message);

            if (result.Cancelled)
            {
                Finish(state);
                return;
            }

            Schedule(state.Id);
        }

        static void Finish(BuildQueueState state)
        {
            string outputRoot = string.Empty;
            if (state.Pending != null && state.Pending.Count > 0 && state.Pending[0] != null)
                outputRoot = state.Pending[0].OutputRoot;

            string summary = Summarize(state, outputRoot);
            BuildLog.Append(outputRoot, summary);
            Clear();
            EditorUtility.DisplayDialog("Build", TrimForDialog(summary), "OK");
        }

        static string Summarize(BuildQueueState state, string outputRoot)
        {
            var builder = new StringBuilder();
            if (!string.IsNullOrEmpty(state.Notice))
            {
                builder.AppendLine(state.Notice);
                builder.AppendLine();
            }

            if (state.Completed == null || state.Completed.Count == 0)
                builder.AppendLine("No builds ran.");

            if (state.Completed != null)
            {
                for (int i = 0; i < state.Completed.Count; i++)
                {
                    TargetResult result = state.Completed[i];
                    if (result == null)
                        continue;

                    builder.AppendLine(StatusLabel(result) + ": " + result.Label);
                    if (!string.IsNullOrEmpty(result.Message))
                        builder.AppendLine(result.Message);
                }
            }

            if (!string.IsNullOrEmpty(outputRoot))
            {
                builder.AppendLine();
                builder.Append("Details: ").Append(BuildLog.LogPath(outputRoot));
            }

            return builder.ToString().Trim();
        }

        static string StatusLabel(TargetResult result)
        {
            if (result.Cancelled)
                return "Cancelled";
            if (!result.BuildSucceeded)
                return "Failed";
            if (result.UploadAttempted && result.UploadSucceeded)
                return "Built and uploaded";
            if (result.UploadAttempted)
                return "Built, upload failed";
            return "Built";
        }

        static string TrimForDialog(string text)
        {
            const int limit = 1200;
            if (string.IsNullOrEmpty(text) || text.Length <= limit)
                return text ?? string.Empty;

            return text.Substring(0, limit) + "\n\n... Full details are in Builds/last-build.log";
        }

        static TargetResult Failure(BuildRequest request, string message)
        {
            return new TargetResult
            {
                Label = request == null ? "Build" : request.Label,
                Message = message
            };
        }

        static string AppendNotice(string notice, string message)
        {
            if (string.IsNullOrEmpty(message))
                return notice ?? string.Empty;
            if (string.IsNullOrEmpty(notice))
                return message;
            return notice + "\n" + message;
        }

        static BuildQueueState Load()
        {
            string json = SessionState.GetString(SessionKey, string.Empty);
            if (string.IsNullOrEmpty(json))
                return null;

            BuildQueueState state = JsonUtility.FromJson<BuildQueueState>(json);
            if (state == null)
                return null;
            if (state.Pending == null)
                state.Pending = new List<BuildRequest>();
            if (state.Completed == null)
                state.Completed = new List<TargetResult>();
            return state;
        }

        static void Save(BuildQueueState state)
        {
            SessionState.SetString(SessionKey, JsonUtility.ToJson(state));
        }
    }
}
