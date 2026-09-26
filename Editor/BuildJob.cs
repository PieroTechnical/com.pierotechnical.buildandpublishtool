using System;
using System.Collections.Generic;

namespace Pierotechnical.BuildAndUploadTool.Editor
{
    [Serializable]
    internal sealed class TargetResult
    {
        public string TargetId;
        public string Label;
        public bool BuildSucceeded;
        public bool UploadAttempted;
        public bool UploadSucceeded;
        public bool Cancelled;
        public string Message;
        public string ArtifactPath;
        public long ArtifactBytes;
        public string StartedUtc;
        public string FinishedUtc;
    }

    internal enum BuildQueuePhase
    {
        None,
        Preparing,
        CheckingPublishers,
        SwitchingTarget,
        Building,
        PublishingArtifact,
        PublishingAggregate,
        RestoringTarget,
        Finalizing,
        Completed,
        Cancelled,
        Failed
    }

    internal enum PublishJobStatus
    {
        Pending,
        Running,
        Succeeded,
        Failed,
        Cancelled,
        Skipped,
        Interrupted
    }

    [Serializable]
    internal sealed class PublishJobResult
    {
        public string JobId;
        public string ProviderId;
        public PublishJobStatus Status;
        public string Message;
        public string StartedUtc;
        public string FinishedUtc;
        public long DurationMilliseconds;
        public int ExitCode = int.MinValue;
        public bool TimedOut;
        public bool OutputTruncated;
        public bool TerminationUnconfirmed;
        public string LatestOutput;
    }

    [Serializable]
    internal sealed class BuildQueueState
    {
        public const int CurrentSchemaVersion = 2;

        public int SchemaVersion = CurrentSchemaVersion;
        public string Id;
        public string Notice;
        public BuildPlan Plan;
        public BuildQueuePhase Phase;
        public int CurrentTargetIndex;
        public int CurrentReadinessIndex;
        public int CurrentPublishJobIndex;
        public List<TargetResult> TargetResults = new List<TargetResult>();
        public List<PublishJobResult> PublishResults = new List<PublishJobResult>();
        public List<string> DisabledPublisherIds = new List<string>();
        public string ActiveOperationId;
        public string ActiveOperationStartedUtc;
        public bool CancelRequested;
        public string StartedUtc;
        public string FinishedUtc;
        public string PhaseStartedUtc;
        public string LatestMessage;
        public bool OriginalSettingsRestored;
        public BuildQueuePhase TerminalPhase;
        public int SwitchAttemptedIndex = -1;
        public string SwitchDomainToken;
        public string SwitchStartedUtc;
        public bool SwitchObserved;
        public int BuildAttemptedIndex = -1;
    }
}
