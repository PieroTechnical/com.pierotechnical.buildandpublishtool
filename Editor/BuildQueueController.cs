using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Pierotechnical.BuildAndUploadTool.Editor
{
    internal sealed class BuildQueueStatus
    {
        internal string QueueId;
        internal BuildQueuePhase Phase;
        internal string CurrentTarget;
        internal string CurrentPublisher;
        internal int CompletedTargets;
        internal int TotalTargets;
        internal string StartedUtc;
        internal long ElapsedSeconds;
        internal string LatestMessage;
        internal bool CanCancel;
        internal bool CancellationRequested;
        internal string OutputRoot;
        internal string LatestArtifactPath;
        internal string ItchUrl;
        internal string SteamUrl;
    }

    [InitializeOnLoad]
    internal static class BuildQueueController
    {
        const int SwitchTimeoutSeconds = 300;
        static readonly string DomainToken = Guid.NewGuid().ToString("N");

        static BuildQueueState state;
        static bool loaded;
        static bool stepScheduled;
        static bool stepping;
        static IPublishOperation activeOperation;
        static string recoveryMessage;

        internal static event Action StatusChanged;

        internal static string RecoveryMessage
        {
            get
            {
                EnsureLoaded();
                return recoveryMessage;
            }
        }

        static BuildQueueController()
        {
            EditorApplication.delayCall += ResumeIfNeeded;
        }

        internal static bool HasPending
        {
            get
            {
                EnsureLoaded();
                return state != null && !IsTerminal(state.Phase);
            }
        }

        internal static BuildQueueState CurrentState
        {
            get
            {
                EnsureLoaded();
                return state;
            }
        }

        internal static BuildQueueStatus Status
        {
            get
            {
                EnsureLoaded();
                if (state == null)
                    return null;

                string operationStatus = activeOperation == null
                    ? string.Empty
                    : activeOperation.Status;
                return new BuildQueueStatus
                {
                    QueueId = state.Id,
                    Phase = state.Phase,
                    CurrentTarget = CurrentTargetLabel(state),
                    CurrentPublisher = CurrentPublisherName(state),
                    CompletedTargets = state.TargetResults == null ? 0 : state.TargetResults.Count,
                    TotalTargets = state.Plan == null || state.Plan.Targets == null ? 0 : state.Plan.Targets.Count,
                    StartedUtc = state.StartedUtc,
                    ElapsedSeconds = ElapsedSeconds(state.StartedUtc, state.FinishedUtc),
                    LatestMessage = string.IsNullOrEmpty(operationStatus)
                        ? state.LatestMessage
                        : operationStatus,
                    CanCancel = state.Phase != BuildQueuePhase.Finalizing
                        && state.Phase != BuildQueuePhase.RestoringTarget
                        && !IsTerminal(state.Phase),
                    CancellationRequested = state.CancelRequested,
                    OutputRoot = state.Plan == null ? string.Empty : state.Plan.OutputRoot,
                    LatestArtifactPath = LatestArtifactPath(state),
                    ItchUrl = ItchUrl(state),
                    SteamUrl = SteamUrl(state)
                };
            }
        }

        internal static bool Start(BuildPlan plan, out string error)
        {
            error = null;
            EnsureLoaded();
            if (state != null && !IsTerminal(state.Phase))
            {
                error = "A build queue is already running.";
                return false;
            }

            if (plan == null || plan.Targets == null || plan.Targets.Count == 0)
            {
                error = "Select at least one build target.";
                return false;
            }

            if (plan.SchemaVersion != BuildPlan.CurrentSchemaVersion)
            {
                error = "The build plan schema is not supported.";
                return false;
            }

            state = new BuildQueueState
            {
                Id = string.IsNullOrEmpty(plan.Id) ? Guid.NewGuid().ToString("N") : plan.Id,
                Plan = plan,
                Phase = BuildQueuePhase.Preparing,
                CurrentTargetIndex = 0,
                CurrentReadinessIndex = 0,
                CurrentPublishJobIndex = 0,
                StartedUtc = UtcNow(),
                PhaseStartedUtc = UtcNow(),
                LatestMessage = "Preparing build plan."
            };
            if (!Save(out error))
            {
                state = null;
                return false;
            }

            BuildLog.Begin(state);
            Schedule();
            return true;
        }

        internal static void RequestCancel()
        {
            EnsureLoaded();
            if (state == null || IsTerminal(state.Phase))
                return;

            state.CancelRequested = true;
            state.LatestMessage = state.Phase == BuildQueuePhase.Building
                && state.BuildAttemptedIndex == state.CurrentTargetIndex
                ? "The queue will stop after the current Unity build."
                : "Cancelling the current operation.";
            SaveIgnoringFailure();
            if (activeOperation != null && activeOperation.CanCancel)
                activeOperation.Cancel();
            else
                Schedule();
        }

        internal static void ClearFinished()
        {
            EnsureLoaded();
            if (state == null || IsTerminal(state.Phase))
            {
                BuildQueueStore.Clear();
                state = null;
                NotifyChanged();
            }
        }

        static void ResumeIfNeeded()
        {
            EnsureLoaded();
            if (state == null)
                return;

            RecoverInterruptedOperation();
            if (!IsTerminal(state.Phase))
                Schedule();
        }

        static void RecoverInterruptedOperation()
        {
            if (state == null || string.IsNullOrEmpty(state.ActiveOperationId))
                return;

            if (state.Phase == BuildQueuePhase.CheckingPublishers)
            {
                state.ActiveOperationId = null;
                state.ActiveOperationStartedUtc = null;
                state.LatestMessage = "Restarting publisher readiness check after reload.";
                SaveIgnoringFailure();
                return;
            }

            if (state.Phase == BuildQueuePhase.PublishingArtifact
                || state.Phase == BuildQueuePhase.PublishingAggregate)
            {
                PublishJob job = CurrentPublishJob(state);
                AddPublishResult(
                    state,
                    job,
                    PublishJobStatus.Interrupted,
                    "The Editor reloaded during this upload. Check the publisher backend before retrying.");
                state.ActiveOperationId = null;
                state.ActiveOperationStartedUtc = null;
                BeginFinalization(
                    BuildQueuePhase.Failed,
                    "An interrupted upload requires backend reconciliation before another upload.");
                return;
            }

            if (state.Phase == BuildQueuePhase.Building)
            {
                AddInterruptedBuild(state);
                state.ActiveOperationId = null;
                state.ActiveOperationStartedUtc = null;
                state.Phase = BuildQueuePhase.PublishingArtifact;
                state.CurrentPublishJobIndex = 0;
                SaveIgnoringFailure();
            }
        }

        static void Schedule()
        {
            if (stepScheduled)
                return;

            stepScheduled = true;
            EditorApplication.delayCall += Step;
        }

        static void Step()
        {
            stepScheduled = false;
            if (stepping)
                return;

            stepping = true;
            try
            {
                EnsureLoaded();
                if (state == null || IsTerminal(state.Phase))
                    return;

                if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                {
                    Schedule();
                    return;
                }

                if (state.CancelRequested && activeOperation == null && state.Phase != BuildQueuePhase.Building)
                {
                    BeginFinalization(BuildQueuePhase.Cancelled, "Build queue cancelled.");
                    return;
                }

                switch (state.Phase)
                {
                    case BuildQueuePhase.Preparing:
                        Prepare();
                        break;
                    case BuildQueuePhase.CheckingPublishers:
                        CheckNextPublisher();
                        break;
                    case BuildQueuePhase.SwitchingTarget:
                        SwitchToCurrentTarget();
                        break;
                    case BuildQueuePhase.Building:
                        BuildCurrentTarget();
                        break;
                    case BuildQueuePhase.PublishingArtifact:
                        PublishNextArtifactJob();
                        break;
                    case BuildQueuePhase.PublishingAggregate:
                        PublishNextAggregateJob();
                        break;
                    case BuildQueuePhase.RestoringTarget:
                        RestoreOriginalSettings();
                        break;
                    case BuildQueuePhase.Finalizing:
                        Finish();
                        break;
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                FailQueue(exception.Message);
            }
            finally
            {
                stepping = false;
            }
        }

        static void Prepare()
        {
            string error;
            if (!ToolSettings.TryCommitVersion(state.Plan.Version, out error))
            {
                FailQueue(error);
                return;
            }

            state.Phase = BuildQueuePhase.CheckingPublishers;
            state.CurrentReadinessIndex = 0;
            SetPhaseMessage("Checking publisher readiness.");
            SaveAndSchedule();
        }

        static void CheckNextPublisher()
        {
            List<IPublishProvider> providers = PublisherRegistry.SelectedProviders(state.Plan);
            if (state.CurrentReadinessIndex >= providers.Count)
            {
                state.CurrentTargetIndex = 0;
                state.CurrentPublishJobIndex = 0;
                state.Phase = BuildQueuePhase.SwitchingTarget;
                SetPhaseMessage("Starting target builds.");
                SaveAndSchedule();
                return;
            }

            IPublishProvider provider = providers[state.CurrentReadinessIndex];
            if (IsDisabled(provider.Id))
            {
                state.CurrentReadinessIndex++;
                SaveAndSchedule();
                return;
            }

            PublishJob job = PublisherRegistry.FirstJob(state.Plan, provider.Id);
            if (job == null)
            {
                state.CurrentReadinessIndex++;
                SaveAndSchedule();
                return;
            }

            string operationId;
            if (!BeginOperation("Checking " + provider.DisplayName + ".", out operationId))
                return;
            string queueId = state.Id;
            bool completedSynchronously = false;
            IPublishOperation operation = provider.BeginCheckReady(job, check =>
            {
                completedSynchronously = true;
                CompleteReadiness(queueId, operationId, provider.Id, check);
            });
            if (!completedSynchronously)
                activeOperation = operation;
        }

        static void CompleteReadiness(string queueId, string operationId, string providerId, UploadCheck check)
        {
            if (!MatchesOperation(queueId, operationId))
                return;

            activeOperation = null;
            state.ActiveOperationId = null;
            state.ActiveOperationStartedUtc = null;
            IPublishProvider provider = PublisherRegistry.Find(providerId);
            if (provider == null)
            {
                FailQueue("Publisher '" + providerId + "' is no longer registered.");
                return;
            }

            if (check.Cancelled)
            {
                state.Notice = AppendNotice(state.Notice, check.Error);
                BeginFinalization(BuildQueuePhase.Cancelled, check.Error);
                return;
            }

            if (!check.Ok)
            {
                bool proceed = EditorUtility.DisplayDialog(
                    provider.DisplayName,
                    check.Error + "\n\n" + provider.ContinueWithoutUpload,
                    "Build anyway",
                    "Cancel");
                if (!proceed)
                {
                    state.Notice = AppendNotice(state.Notice, check.Error);
                    BeginFinalization(BuildQueuePhase.Cancelled, check.Error);
                    return;
                }

                if (!state.DisabledPublisherIds.Contains(provider.Id))
                    state.DisabledPublisherIds.Add(provider.Id);
                state.Notice = AppendNotice(
                    state.Notice,
                    check.Error + " " + provider.BuildAnywayNotice);
            }

            state.CurrentReadinessIndex++;
            state.LatestMessage = provider.DisplayName + " readiness check finished.";
            SaveAndSchedule();
        }

        static void SwitchToCurrentTarget()
        {
            if (state.CurrentTargetIndex >= state.Plan.Targets.Count)
            {
                state.CurrentPublishJobIndex = 0;
                state.Phase = BuildQueuePhase.PublishingAggregate;
                SetPhaseMessage("Publishing aggregate jobs.");
                SaveAndSchedule();
                return;
            }

            BuildTargetPlan targetPlan = state.Plan.Targets[state.CurrentTargetIndex];
            BuildTarget target = (BuildTarget)targetPlan.TargetValue;
            if (EditorUserBuildSettings.activeBuildTarget == target)
            {
                state.SwitchAttemptedIndex = -1;
                state.SwitchDomainToken = null;
                state.SwitchStartedUtc = null;
                state.Phase = BuildQueuePhase.Building;
                SetPhaseMessage("Building " + targetPlan.Label + ".");
                SaveAndSchedule();
                return;
            }

            if (state.SwitchAttemptedIndex == state.CurrentTargetIndex)
            {
                DateTime started;
                if (!DateTime.TryParse(
                    state.SwitchStartedUtc,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out started))
                {
                    started = DateTime.UtcNow;
                }

                if ((DateTime.UtcNow - started).TotalSeconds > SwitchTimeoutSeconds)
                {
                    RecordTargetFailure("Timed out waiting for the Editor to switch to " + targetPlan.Label + ".");
                    return;
                }

                Schedule();
                return;
            }

            state.SwitchAttemptedIndex = state.CurrentTargetIndex;
            state.SwitchDomainToken = DomainToken;
            state.SwitchStartedUtc = UtcNow();
            state.LatestMessage = "Switching build target to " + targetPlan.Label + ".";
            if (!PersistBeforeEffect())
                return;
            bool switched = EditorUserBuildSettings.SwitchActiveBuildTarget(
                PlatformCatalog.Group(target),
                target);
            if (!switched)
            {
                RecordTargetFailure("Could not switch the active build target to " + targetPlan.Label + ".");
                return;
            }

            Schedule();
        }

        static void BuildCurrentTarget()
        {
            if (state.CurrentTargetIndex < state.TargetResults.Count)
            {
                state.Phase = BuildQueuePhase.PublishingArtifact;
                state.CurrentPublishJobIndex = 0;
                SaveAndSchedule();
                return;
            }

            if (state.BuildAttemptedIndex == state.CurrentTargetIndex)
            {
                AddInterruptedBuild(state);
                state.Phase = BuildQueuePhase.PublishingArtifact;
                state.CurrentPublishJobIndex = 0;
                SaveAndSchedule();
                return;
            }

            if (state.CancelRequested)
            {
                BeginFinalization(
                    BuildQueuePhase.Cancelled,
                    "Build queue cancelled before the next Unity build.");
                return;
            }

            state.BuildAttemptedIndex = state.CurrentTargetIndex;
            state.ActiveOperationId = Guid.NewGuid().ToString("N");
            state.ActiveOperationStartedUtc = UtcNow();
            state.LatestMessage = "Building " + state.Plan.Targets[state.CurrentTargetIndex].Label + ".";
            if (!PersistBeforeEffect())
                return;

            TargetResult result = ProjectBuilder.Execute(state.Plan, state.CurrentTargetIndex);
            result.StartedUtc = state.ActiveOperationStartedUtc;
            result.FinishedUtc = UtcNow();
            state.TargetResults.Add(result);
            state.BuildAttemptedIndex = -1;
            state.ActiveOperationId = null;
            state.ActiveOperationStartedUtc = null;
            state.CurrentPublishJobIndex = 0;
            state.Phase = BuildQueuePhase.PublishingArtifact;
            BuildLog.Append(
                state.Plan.OutputRoot,
                state.Id,
                result.Label + ": " + result.Message);
            SaveAndSchedule();
        }

        static void PublishNextArtifactJob()
        {
            PublishJob job = FindNextJob(PublishJobScope.Artifact, state.CurrentTargetIndex);
            if (job == null)
            {
                state.CurrentTargetIndex++;
                state.CurrentPublishJobIndex = 0;
                state.Phase = BuildQueuePhase.SwitchingTarget;
                SetPhaseMessage("Moving to the next target.");
                SaveAndSchedule();
                return;
            }

            ExecutePublishJob(job);
        }

        static void PublishNextAggregateJob()
        {
            PublishJob job = FindNextJob(PublishJobScope.Aggregate, -1);
            if (job == null)
            {
                BeginFinalization(BuildQueuePhase.Completed, "Build queue finished.");
                return;
            }

            ExecutePublishJob(job);
        }

        static PublishJob FindNextJob(PublishJobScope scope, int targetIndex)
        {
            while (state.CurrentPublishJobIndex < state.Plan.PublishJobs.Count)
            {
                PublishJob job = state.Plan.PublishJobs[state.CurrentPublishJobIndex];
                if (job != null
                    && job.Scope == scope
                    && (scope != PublishJobScope.Artifact || job.TargetIndex == targetIndex))
                {
                    return job;
                }

                state.CurrentPublishJobIndex++;
            }

            return null;
        }

        static void ExecutePublishJob(PublishJob job)
        {
            if (IsDisabled(job.ProviderId))
            {
                AddPublishResult(
                    state,
                    job,
                    PublishJobStatus.Skipped,
                    "Publishing was disabled after its readiness check.");
                state.CurrentPublishJobIndex++;
                SaveAndSchedule();
                return;
            }

            if (job.Scope == PublishJobScope.Artifact
                && (job.TargetIndex < 0
                    || job.TargetIndex >= state.TargetResults.Count
                    || !state.TargetResults[job.TargetIndex].BuildSucceeded))
            {
                AddPublishResult(
                    state,
                    job,
                    PublishJobStatus.Skipped,
                    "Upload skipped because the target did not build successfully.");
                state.CurrentPublishJobIndex++;
                SaveAndSchedule();
                return;
            }

            IPublishProvider provider = PublisherRegistry.Find(job.ProviderId);
            if (provider == null)
            {
                AddPublishResult(
                    state,
                    job,
                    PublishJobStatus.Failed,
                    "Publisher '" + job.ProviderId + "' is not registered.");
                state.CurrentPublishJobIndex++;
                SaveAndSchedule();
                return;
            }

            string operationId;
            if (!BeginOperation("Publishing with " + provider.DisplayName + ".", out operationId))
                return;
            string queueId = state.Id;
            bool completedSynchronously = false;
            IPublishOperation operation = provider.BeginPublish(
                job,
                new PublishExecutionContext
                {
                    Plan = state.Plan,
                    TargetResults = state.TargetResults,
                    QueueId = state.Id
                },
                publishResult =>
                {
                    completedSynchronously = true;
                    CompletePublish(queueId, operationId, job.Id, publishResult);
                });
            if (!completedSynchronously)
                activeOperation = operation;
        }

        static void CompletePublish(
            string queueId,
            string operationId,
            string jobId,
            PublishOperationResult result)
        {
            if (!MatchesOperation(queueId, operationId))
                return;

            activeOperation = null;
            state.ActiveOperationId = null;
            PublishJob job = FindJob(jobId);
            if (job == null)
            {
                FailQueue("The active publish job disappeared from the build plan.");
                return;
            }

            PublishJobStatus status = result == null ? PublishJobStatus.Failed : result.Status;
            string message = result == null ? "Publisher returned no result." : result.Message;
            AddPublishResult(state, job, status, message, result);
            state.ActiveOperationStartedUtc = null;
            state.CurrentPublishJobIndex++;
            if (status == PublishJobStatus.Cancelled)
                state.CancelRequested = true;
            SaveAndSchedule();
        }

        static void BeginFinalization(BuildQueuePhase terminalPhase, string message)
        {
            state.LatestMessage = message;
            state.Notice = AppendNotice(state.Notice, message);
            state.TerminalPhase = terminalPhase;
            state.Phase = BuildQueuePhase.RestoringTarget;
            state.PhaseStartedUtc = UtcNow();
            state.CancelRequested = terminalPhase == BuildQueuePhase.Cancelled;
            SaveAndSchedule();
        }

        static void RestoreOriginalSettings()
        {
            EditorUserBuildSettings.buildAppBundle = state.Plan.OriginalAndroidAppBundle;
            BuildTarget original = (BuildTarget)state.Plan.OriginalBuildTargetValue;
            if (original != BuildTarget.NoTarget
                && Enum.IsDefined(typeof(BuildTarget), state.Plan.OriginalBuildTargetValue)
                && EditorUserBuildSettings.activeBuildTarget != original)
            {
                if (state.SwitchAttemptedIndex != -2)
                {
                    state.SwitchAttemptedIndex = -2;
                    state.SwitchStartedUtc = UtcNow();
                    SaveIgnoringFailure();
                    bool switched = EditorUserBuildSettings.SwitchActiveBuildTarget(
                        PlatformCatalog.Group(original),
                        original);
                    if (!switched)
                    {
                        state.Notice = AppendNotice(
                            state.Notice,
                            "Could not restore the original active build target.");
                        state.OriginalSettingsRestored = true;
                    }
                    else
                    {
                        Schedule();
                        return;
                    }
                }
                else
                {
                    DateTime started;
                    DateTime.TryParse(
                        state.SwitchStartedUtc,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind,
                        out started);
                    if (started != default(DateTime)
                        && (DateTime.UtcNow - started).TotalSeconds <= SwitchTimeoutSeconds)
                    {
                        Schedule();
                        return;
                    }

                    state.Notice = AppendNotice(
                        state.Notice,
                        "Timed out restoring the original active build target.");
                }
            }

            state.OriginalSettingsRestored = true;
            state.Phase = BuildQueuePhase.Finalizing;
            SetPhaseMessage("Finalizing build report.");
            SaveAndSchedule();
        }

        static void Finish()
        {
            state.Phase = state.TerminalPhase == BuildQueuePhase.None
                ? BuildQueuePhase.Completed
                : state.TerminalPhase;
            state.LatestMessage = state.Phase == BuildQueuePhase.Cancelled
                ? "Build queue cancelled."
                : state.Phase == BuildQueuePhase.Failed
                    ? "Build queue failed."
                    : "Build queue completed.";
            state.FinishedUtc = UtcNow();
            string summary = Summarize(state);
            BuildLog.Complete(state, summary);
            SaveIgnoringFailure();
            NotifyChanged();
            EditorUtility.DisplayDialog("Build", TrimForDialog(summary), "OK");
        }

        static void FailQueue(string message)
        {
            if (state == null)
                return;
            state.Notice = AppendNotice(state.Notice, message);
            state.LatestMessage = message;
            BeginFinalization(BuildQueuePhase.Failed, "Build queue failed.");
        }

        static void RecordTargetFailure(string message)
        {
            BuildTargetPlan target = state.Plan.Targets[state.CurrentTargetIndex];
            state.TargetResults.Add(new TargetResult
            {
                TargetId = target.Id,
                Label = target.Label,
                Message = message,
                StartedUtc = UtcNow(),
                FinishedUtc = UtcNow()
            });
            state.SwitchAttemptedIndex = -1;
            state.BuildAttemptedIndex = -1;
            state.CurrentPublishJobIndex = 0;
            state.Phase = BuildQueuePhase.PublishingArtifact;
            SaveAndSchedule();
        }

        static void AddInterruptedBuild(BuildQueueState queue)
        {
            if (queue.Plan == null
                || queue.Plan.Targets == null
                || queue.CurrentTargetIndex < 0
                || queue.CurrentTargetIndex >= queue.Plan.Targets.Count)
            {
                return;
            }

            BuildTargetPlan target = queue.Plan.Targets[queue.CurrentTargetIndex];
            queue.TargetResults.Add(new TargetResult
            {
                TargetId = target.Id,
                Label = target.Label,
                Message = "The Editor reloaded during this build. The previous successful artifact was left in place.",
                StartedUtc = string.IsNullOrEmpty(queue.ActiveOperationStartedUtc)
                    ? queue.PhaseStartedUtc
                    : queue.ActiveOperationStartedUtc,
                FinishedUtc = UtcNow()
            });
            queue.BuildAttemptedIndex = -1;
        }

        static bool BeginOperation(string message, out string id)
        {
            id = Guid.NewGuid().ToString("N");
            state.ActiveOperationId = id;
            state.ActiveOperationStartedUtc = UtcNow();
            state.LatestMessage = message;
            if (PersistBeforeEffect())
                return true;

            id = null;
            return false;
        }

        static bool PersistBeforeEffect()
        {
            string error;
            if (Save(out error))
                return true;

            state.ActiveOperationId = null;
            state.ActiveOperationStartedUtc = null;
            FailQueue(string.IsNullOrEmpty(error)
                ? "Could not save the build queue."
                : error);
            return false;
        }

        static bool MatchesOperation(string queueId, string operationId)
        {
            EnsureLoaded();
            return MatchesOperation(state, queueId, operationId);
        }

        internal static bool MatchesOperation(
            BuildQueueState queue,
            string queueId,
            string operationId)
        {
            return queue != null
                && !string.IsNullOrEmpty(queueId)
                && !string.IsNullOrEmpty(operationId)
                && queue.Id == queueId
                && queue.ActiveOperationId == operationId;
        }

        static PublishJob CurrentPublishJob(BuildQueueState queue)
        {
            if (queue == null
                || queue.Plan == null
                || queue.Plan.PublishJobs == null
                || queue.CurrentPublishJobIndex < 0
                || queue.CurrentPublishJobIndex >= queue.Plan.PublishJobs.Count)
            {
                return null;
            }

            return queue.Plan.PublishJobs[queue.CurrentPublishJobIndex];
        }

        static PublishJob FindJob(string id)
        {
            if (state == null || state.Plan == null || state.Plan.PublishJobs == null)
                return null;
            for (int i = 0; i < state.Plan.PublishJobs.Count; i++)
            {
                PublishJob job = state.Plan.PublishJobs[i];
                if (job != null && job.Id == id)
                    return job;
            }

            return null;
        }

        static void AddPublishResult(
            BuildQueueState queue,
            PublishJob job,
            PublishJobStatus status,
            string message,
            PublishOperationResult operationResult = null)
        {
            if (queue.PublishResults == null)
                queue.PublishResults = new List<PublishJobResult>();
            ProcessRunResult process = operationResult == null
                ? null
                : operationResult.Process;
            queue.PublishResults.Add(new PublishJobResult
            {
                JobId = job == null ? string.Empty : job.Id,
                ProviderId = job == null ? string.Empty : job.ProviderId,
                Status = status,
                Message = message,
                StartedUtc = string.IsNullOrEmpty(queue.ActiveOperationStartedUtc)
                    ? queue.PhaseStartedUtc
                    : queue.ActiveOperationStartedUtc,
                FinishedUtc = UtcNow(),
                DurationMilliseconds = process == null ? 0L : process.DurationMilliseconds,
                ExitCode = process == null ? int.MinValue : process.ExitCode,
                TimedOut = process != null && process.TimedOut,
                OutputTruncated = process != null && process.OutputTruncated,
                TerminationUnconfirmed = process != null && process.TerminationUnconfirmed,
                LatestOutput = process == null
                    ? string.Empty
                    : ProcessOutput.Redact(process.LatestLine)
            });
            queue.LatestMessage = message;
            if (queue.Plan != null)
            {
                BuildLog.Append(
                    queue.Plan.OutputRoot,
                    queue.Id,
                    (job == null ? "Publisher" : job.ProviderId) + ": " + message);
            }
        }

        static bool IsDisabled(string providerId)
        {
            return state.DisabledPublisherIds != null
                && state.DisabledPublisherIds.Contains(providerId);
        }

        static void SetPhaseMessage(string message)
        {
            state.LatestMessage = message;
            state.PhaseStartedUtc = UtcNow();
        }

        static void SaveAndSchedule()
        {
            SaveIgnoringFailure();
            Schedule();
        }

        static bool Save(out string error)
        {
            bool saved = BuildQueueStore.TrySave(state, out error);
            if (saved)
                NotifyChanged();
            return saved;
        }

        static void SaveIgnoringFailure()
        {
            string error;
            if (!Save(out error))
                Debug.LogError(error);
        }

        static void EnsureLoaded()
        {
            if (loaded)
                return;

            loaded = true;
            string error;
            if (!BuildQueueStore.TryLoad(out state, out error) && !string.IsNullOrEmpty(error))
            {
                recoveryMessage = error
                    + " The unreadable journal was backed up under Library/BuildAndPublishTool.";
                Debug.LogError(recoveryMessage);
            }
        }

        internal static bool IsTerminal(BuildQueuePhase phase)
        {
            return phase == BuildQueuePhase.Completed
                || phase == BuildQueuePhase.Cancelled
                || phase == BuildQueuePhase.Failed;
        }

        static long ElapsedSeconds(string startedUtc, string finishedUtc)
        {
            DateTime started;
            if (!DateTime.TryParse(
                startedUtc,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out started))
            {
                return 0L;
            }

            DateTime finished;
            if (!DateTime.TryParse(
                finishedUtc,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out finished))
            {
                finished = DateTime.UtcNow;
            }

            return (long)Math.Max(0, (finished - started).TotalSeconds);
        }

        static string LatestArtifactPath(BuildQueueState queue)
        {
            if (queue == null || queue.TargetResults == null)
                return string.Empty;
            for (int i = queue.TargetResults.Count - 1; i >= 0; i--)
            {
                TargetResult result = queue.TargetResults[i];
                if (result != null
                    && result.BuildSucceeded
                    && !string.IsNullOrEmpty(result.ArtifactPath))
                {
                    return result.ArtifactPath;
                }
            }

            return string.Empty;
        }

        static string ItchUrl(BuildQueueState queue)
        {
            if (queue == null || queue.Plan == null || queue.Plan.PublishJobs == null)
                return string.Empty;
            for (int i = 0; i < queue.Plan.PublishJobs.Count; i++)
            {
                PublishJob job = queue.Plan.PublishJobs[i];
                if (job != null && job.Itch != null)
                    return BuildPaths.ItchPageUrl(job.Itch.Owner, job.Itch.Project);
            }

            return string.Empty;
        }

        static string SteamUrl(BuildQueueState queue)
        {
            if (queue == null || queue.Plan == null || queue.Plan.PublishJobs == null)
                return string.Empty;
            for (int i = 0; i < queue.Plan.PublishJobs.Count; i++)
            {
                PublishJob job = queue.Plan.PublishJobs[i];
                if (job != null
                    && job.Steam != null
                    && !string.IsNullOrEmpty(job.Steam.AppId))
                {
                    return SteamCommand.BuildsPageUrl(job.Steam.AppId);
                }
            }

            return string.Empty;
        }

        static string CurrentTargetLabel(BuildQueueState queue)
        {
            if (queue == null
                || queue.Plan == null
                || queue.Plan.Targets == null
                || queue.CurrentTargetIndex < 0
                || queue.CurrentTargetIndex >= queue.Plan.Targets.Count)
            {
                return string.Empty;
            }

            return queue.Plan.Targets[queue.CurrentTargetIndex].Label;
        }

        static string CurrentPublisherName(BuildQueueState queue)
        {
            PublishJob job = CurrentPublishJob(queue);
            IPublishProvider provider = job == null ? null : PublisherRegistry.Find(job.ProviderId);
            return provider == null ? string.Empty : provider.DisplayName;
        }

        static string Summarize(BuildQueueState queue)
        {
            var builder = new StringBuilder();
            if (!string.IsNullOrEmpty(queue.Notice))
            {
                builder.AppendLine(queue.Notice);
                builder.AppendLine();
            }

            if (queue.TargetResults == null || queue.TargetResults.Count == 0)
                builder.AppendLine("No builds ran.");
            else
            {
                for (int i = 0; i < queue.TargetResults.Count; i++)
                {
                    TargetResult result = queue.TargetResults[i];
                    if (result == null)
                        continue;
                    builder.Append(result.BuildSucceeded ? "Built: " : "Failed: ");
                    builder.AppendLine(result.Label);
                    if (!string.IsNullOrEmpty(result.Message))
                        builder.AppendLine(result.Message);
                }
            }

            if (queue.PublishResults != null)
            {
                for (int i = 0; i < queue.PublishResults.Count; i++)
                {
                    PublishJobResult result = queue.PublishResults[i];
                    if (result == null)
                        continue;
                    builder.Append(result.ProviderId).Append(" — ").Append(result.Status).AppendLine();
                    if (!string.IsNullOrEmpty(result.Message))
                        builder.AppendLine(result.Message);
                }
            }

            if (queue.Plan != null && !string.IsNullOrEmpty(queue.Plan.OutputRoot))
            {
                builder.AppendLine();
                builder.Append("Details: ").Append(
                    BuildLog.RunLogPath(queue.Plan.OutputRoot, queue.Id));
            }

            return builder.ToString().Trim();
        }

        internal static string AppendNotice(string notice, string message)
        {
            if (string.IsNullOrEmpty(message))
                return notice ?? string.Empty;
            if (string.IsNullOrEmpty(notice))
                return message;
            return notice + "\n" + message;
        }

        static string TrimForDialog(string text)
        {
            const int limit = 1200;
            if (string.IsNullOrEmpty(text) || text.Length <= limit)
                return text ?? string.Empty;
            return text.Substring(0, limit) + "\n\n... Full details are in Builds/last-build.log";
        }

        static string UtcNow()
        {
            return DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
        }

        static void NotifyChanged()
        {
            Action changed = StatusChanged;
            if (changed != null)
                changed();
        }
    }
}
