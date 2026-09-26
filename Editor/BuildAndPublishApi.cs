using System;
using System.Collections.Generic;
using UnityEditor;

namespace Pierotechnical.BuildAndUploadTool.Editor
{
    public enum BuildValidationLevel
    {
        Warning,
        Error
    }

    public sealed class BuildValidationMessage
    {
        internal BuildValidationMessage(
            BuildValidationLevel level,
            string code,
            string field,
            string targetId,
            string message)
        {
            Level = level;
            Code = code ?? string.Empty;
            Field = field ?? string.Empty;
            TargetId = targetId ?? string.Empty;
            Message = message ?? string.Empty;
        }

        public BuildValidationLevel Level { get; private set; }
        public string Code { get; private set; }
        public string Field { get; private set; }
        public string TargetId { get; private set; }
        public string Message { get; private set; }
    }

    public sealed class BuildPlanOptions
    {
        public BuildPlanOptions()
        {
            TargetIds = new List<string>();
        }

        public IList<string> TargetIds { get; private set; }
        public bool Publish { get; set; }
        public string Version { get; set; }
    }

    public sealed class ValidatedBuildPlan
    {
        readonly string[] targetIds;

        internal ValidatedBuildPlan(BuildPlan plan)
        {
            Value = plan;
            Id = plan == null ? string.Empty : plan.Id;
            Version = plan == null ? string.Empty : plan.Version;
            int count = plan == null || plan.Targets == null ? 0 : plan.Targets.Count;
            targetIds = new string[count];
            for (int i = 0; i < count; i++)
                targetIds[i] = plan.Targets[i] == null ? string.Empty : plan.Targets[i].Id;
        }

        internal BuildPlan Value { get; private set; }
        public string Id { get; private set; }
        public string Version { get; private set; }
        public string[] TargetIds { get { return (string[])targetIds.Clone(); } }
    }

    public sealed class BuildTargetOutcome
    {
        internal BuildTargetOutcome(TargetResult result)
        {
            TargetId = result == null ? string.Empty : result.TargetId;
            Label = result == null ? string.Empty : result.Label;
            Succeeded = result != null && result.BuildSucceeded;
            Message = result == null ? string.Empty : result.Message;
            ArtifactPath = result == null ? string.Empty : result.ArtifactPath;
        }

        public string TargetId { get; private set; }
        public string Label { get; private set; }
        public bool Succeeded { get; private set; }
        public string Message { get; private set; }
        public string ArtifactPath { get; private set; }
    }

    public sealed class PublisherOutcome
    {
        internal PublisherOutcome(PublishJobResult result)
        {
            JobId = result == null ? string.Empty : result.JobId;
            ProviderId = result == null ? string.Empty : result.ProviderId;
            Status = result == null ? string.Empty : result.Status.ToString();
            Message = result == null ? string.Empty : result.Message;
        }

        public string JobId { get; private set; }
        public string ProviderId { get; private set; }
        public string Status { get; private set; }
        public string Message { get; private set; }
    }

    public sealed class BuildRunSnapshot
    {
        internal BuildRunSnapshot(BuildQueueState state, BuildQueueStatus status)
        {
            QueueId = status == null ? string.Empty : status.QueueId;
            Phase = status == null ? string.Empty : status.Phase.ToString();
            CurrentTarget = status == null ? string.Empty : status.CurrentTarget;
            CurrentPublisher = status == null ? string.Empty : status.CurrentPublisher;
            CompletedTargets = status == null ? 0 : status.CompletedTargets;
            TotalTargets = status == null ? 0 : status.TotalTargets;
            ElapsedSeconds = status == null ? 0L : status.ElapsedSeconds;
            LatestMessage = status == null ? string.Empty : status.LatestMessage;
            CanCancel = status != null && status.CanCancel;

            var targets = new List<BuildTargetOutcome>();
            if (state != null && state.TargetResults != null)
            {
                for (int i = 0; i < state.TargetResults.Count; i++)
                    targets.Add(new BuildTargetOutcome(state.TargetResults[i]));
            }
            TargetResults = targets.ToArray();

            var publishers = new List<PublisherOutcome>();
            if (state != null && state.PublishResults != null)
            {
                for (int i = 0; i < state.PublishResults.Count; i++)
                    publishers.Add(new PublisherOutcome(state.PublishResults[i]));
            }
            PublisherResults = publishers.ToArray();
        }

        public string QueueId { get; private set; }
        public string Phase { get; private set; }
        public string CurrentTarget { get; private set; }
        public string CurrentPublisher { get; private set; }
        public int CompletedTargets { get; private set; }
        public int TotalTargets { get; private set; }
        public long ElapsedSeconds { get; private set; }
        public string LatestMessage { get; private set; }
        public bool CanCancel { get; private set; }
        public BuildTargetOutcome[] TargetResults { get; private set; }
        public PublisherOutcome[] PublisherResults { get; private set; }
    }

    public static class BuildAndPublish
    {
        public static event Action RunChanged;

        static BuildAndPublish()
        {
            BuildQueueController.StatusChanged += NotifyRunChanged;
        }

        public static bool HasActiveRun
        {
            get { return BuildQueueController.HasPending; }
        }

        public static BuildRunSnapshot CurrentRun
        {
            get
            {
                BuildQueueStatus status = BuildQueueController.Status;
                return status == null
                    ? null
                    : new BuildRunSnapshot(BuildQueueController.CurrentState, status);
            }
        }

        public static bool TryCreatePlan(
            BuildPlanOptions options,
            out ValidatedBuildPlan plan,
            out BuildValidationMessage[] messages)
        {
            plan = null;
            options = options ?? new BuildPlanOptions();
            List<BuildTargetEntry> targets = ToolSettings.LoadTargets();
            List<string> unknownTargetIds;
            List<int> indices = ResolveTargets(
                targets,
                options.TargetIds,
                out unknownTargetIds);
            string version = options.Version;
            if (string.IsNullOrWhiteSpace(version)
                && !ToolSettings.TryReadVersionFile(out version))
            {
                version = PlayerSettings.bundleVersion;
            }

            var configuration = new BuildConfiguration
            {
                Targets = targets,
                SelectedIndices = indices,
                Upload = options.Publish,
                Version = version,
                GameName = ToolSettings.LoadGameName(),
                ItchOwner = ToolSettings.LoadItchUser(),
                ItchProject = ToolSettings.LoadItchGame(),
                ButlerPath = ButlerUploader.GetButlerPath(),
                SteamUser = ToolSettings.LoadSteamUser(),
                SteamAppId = ToolSettings.LoadSteamAppId(),
                SteamBranch = ToolSettings.LoadSteamBranch(),
                SteamCmdPath = SteamUploader.GetSteamCmdPath()
            };
            BuildPlanResult result = BuildPlanFactory.Create(
                configuration,
                UnityBuildEnvironment.Capture(targets, indices));
            BuildValidationMessage[] planMessages = ConvertIssues(result.Issues);
            messages = AddUnknownTargetMessages(planMessages, unknownTargetIds);
            if (result.HasErrors || result.Plan == null || unknownTargetIds.Count > 0)
                return false;

            plan = new ValidatedBuildPlan(result.Plan);
            return true;
        }

        public static bool TrySubmit(ValidatedBuildPlan plan, out string error)
        {
            if (plan == null || plan.Value == null)
            {
                error = "A validated build plan is required.";
                return false;
            }
            return BuildQueueController.Start(plan.Value, out error);
        }

        public static void Cancel()
        {
            BuildQueueController.RequestCancel();
        }

        static List<int> ResolveTargets(
            IList<BuildTargetEntry> targets,
            IList<string> targetIds,
            out List<string> unknownTargetIds)
        {
            var indices = new List<int>();
            unknownTargetIds = new List<string>();
            if (targets == null)
                return indices;

            if (targetIds == null || targetIds.Count == 0)
            {
                for (int i = 0; i < targets.Count; i++)
                {
                    if (targets[i] != null && targets[i].Enabled)
                        indices.Add(i);
                }
                return indices;
            }

            var selected = new HashSet<string>(targetIds, StringComparer.Ordinal);
            for (int i = 0; i < targets.Count; i++)
            {
                if (targets[i] != null && selected.Contains(targets[i].Id))
                {
                    indices.Add(i);
                    selected.Remove(targets[i].Id);
                }
            }
            unknownTargetIds.AddRange(selected);
            return indices;
        }

        static BuildValidationMessage[] AddUnknownTargetMessages(
            BuildValidationMessage[] messages,
            IList<string> unknownTargetIds)
        {
            int originalCount = messages == null ? 0 : messages.Length;
            int unknownCount = unknownTargetIds == null ? 0 : unknownTargetIds.Count;
            if (unknownCount == 0)
                return messages ?? new BuildValidationMessage[0];

            var combined = new BuildValidationMessage[originalCount + unknownCount];
            if (originalCount > 0)
                Array.Copy(messages, combined, originalCount);
            for (int i = 0; i < unknownCount; i++)
            {
                combined[originalCount + i] = new BuildValidationMessage(
                    BuildValidationLevel.Error,
                    "target.unknown",
                    "targetIds",
                    unknownTargetIds[i],
                    "Build target '" + unknownTargetIds[i]
                        + "' is not in the project profile.");
            }
            return combined;
        }

        static BuildValidationMessage[] ConvertIssues(
            IList<ValidationIssue> issues)
        {
            if (issues == null)
                return new BuildValidationMessage[0];
            var messages = new BuildValidationMessage[issues.Count];
            for (int i = 0; i < issues.Count; i++)
            {
                ValidationIssue issue = issues[i];
                messages[i] = issue == null
                    ? new BuildValidationMessage(
                        BuildValidationLevel.Error,
                        "validation.missing",
                        null,
                        null,
                        "A validation result was missing.")
                    : new BuildValidationMessage(
                        issue.Severity == ValidationSeverity.Error
                            ? BuildValidationLevel.Error
                            : BuildValidationLevel.Warning,
                        issue.Code,
                        issue.Field,
                        issue.TargetId,
                        issue.Message);
            }
            return messages;
        }

        static void NotifyRunChanged()
        {
            Action changed = RunChanged;
            if (changed == null)
                return;
            Delegate[] listeners = changed.GetInvocationList();
            for (int i = 0; i < listeners.Length; i++)
            {
                try
                {
                    ((Action)listeners[i])();
                }
                catch (Exception exception)
                {
                    UnityEngine.Debug.LogException(exception);
                }
            }
        }
    }
}
