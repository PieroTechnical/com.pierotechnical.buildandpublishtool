using System;
using System.Collections.Generic;

namespace Pierotechnical.BuildAndUploadTool.Editor
{
    internal interface IPublishOperation
    {
        bool CanCancel { get; }
        string Status { get; }
        void Cancel();
    }

    internal sealed class CompletedPublishOperation : IPublishOperation
    {
        internal static readonly CompletedPublishOperation Instance = new CompletedPublishOperation();

        public bool CanCancel { get { return false; } }
        public string Status { get { return string.Empty; } }
        public void Cancel() {}
    }

    internal sealed class PublishExecutionContext
    {
        internal BuildPlan Plan;
        internal IList<TargetResult> TargetResults;
        internal string QueueId;
    }

    internal sealed class PublishOperationResult
    {
        internal PublishJobStatus Status;
        internal string Message;
        internal ProcessRunResult Process;
    }

    internal interface IPublishProvider
    {
        string Id { get; }
        string DisplayName { get; }
        string ContinueWithoutUpload { get; }
        string BuildAnywayNotice { get; }
        IPublishOperation BeginCheckReady(PublishJob job, Action<UploadCheck> onComplete);
        IPublishOperation BeginPublish(
            PublishJob job,
            PublishExecutionContext context,
            Action<PublishOperationResult> onComplete);
    }

    internal static class PublisherRegistry
    {
        static readonly IPublishProvider[] Providers =
        {
            new ItchPublishProvider(),
            new SteamPublishProvider()
        };

        static PublisherRegistry()
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < Providers.Length; i++)
            {
                if (Providers[i] == null
                    || string.IsNullOrEmpty(Providers[i].Id)
                    || !ids.Add(Providers[i].Id))
                {
                    throw new InvalidOperationException("Publisher provider IDs must be non-empty and unique.");
                }
            }
        }

        internal static IPublishProvider Find(string id)
        {
            for (int i = 0; i < Providers.Length; i++)
            {
                if (Providers[i].Id == id)
                    return Providers[i];
            }

            return null;
        }

        internal static List<IPublishProvider> SelectedProviders(BuildPlan plan)
        {
            var selected = new List<IPublishProvider>();
            if (plan == null || plan.PublishJobs == null)
                return selected;

            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < plan.PublishJobs.Count; i++)
            {
                PublishJob job = plan.PublishJobs[i];
                if (job == null || !ids.Add(job.ProviderId))
                    continue;

                IPublishProvider provider = Find(job.ProviderId);
                if (provider != null)
                    selected.Add(provider);
            }

            return selected;
        }

        internal static PublishJob FirstJob(BuildPlan plan, string providerId)
        {
            if (plan == null || plan.PublishJobs == null)
                return null;
            for (int i = 0; i < plan.PublishJobs.Count; i++)
            {
                PublishJob job = plan.PublishJobs[i];
                if (job != null && job.ProviderId == providerId)
                    return job;
            }

            return null;
        }
    }

    internal sealed class ItchPublishProvider : IPublishProvider
    {
        public string Id { get { return PublisherIds.Itch; } }
        public string DisplayName { get { return "Butler"; } }
        public string ContinueWithoutUpload { get { return "Builds will continue without uploading to itch.io."; } }
        public string BuildAnywayNotice { get { return "Builds will not be uploaded to itch.io."; } }

        public IPublishOperation BeginCheckReady(PublishJob job, Action<UploadCheck> onComplete)
        {
            ItchPublishPayload payload = job == null ? null : job.Itch;
            if (payload == null)
            {
                onComplete(new UploadCheck { Error = "Enter an itch owner and project slug before uploading." });
                return CompletedPublishOperation.Instance;
            }

            return ButlerUploader.CheckLoginAsync(
                payload.ButlerPath,
                payload.Owner,
                payload.Project,
                onComplete);
        }

        public IPublishOperation BeginPublish(
            PublishJob job,
            PublishExecutionContext context,
            Action<PublishOperationResult> onComplete)
        {
            TargetResult targetResult = GetTargetResult(job, context);
            if (targetResult == null || !targetResult.BuildSucceeded || string.IsNullOrEmpty(targetResult.ArtifactPath))
            {
                onComplete(new PublishOperationResult
                {
                    Status = PublishJobStatus.Skipped,
                    Message = "itch.io upload skipped because the target did not build successfully."
                });
                return CompletedPublishOperation.Instance;
            }

            ItchPublishPayload payload = job.Itch;
            return ButlerUploader.BeginPush(
                payload,
                targetResult.Label,
                targetResult.ArtifactPath,
                context.Plan.OutputRoot,
                context.QueueId,
                onComplete);
        }

        static TargetResult GetTargetResult(PublishJob job, PublishExecutionContext context)
        {
            if (job == null
                || context == null
                || context.TargetResults == null
                || job.TargetIndex < 0
                || job.TargetIndex >= context.TargetResults.Count)
            {
                return null;
            }

            return context.TargetResults[job.TargetIndex];
        }
    }

    internal sealed class SteamPublishProvider : IPublishProvider
    {
        public string Id { get { return PublisherIds.Steam; } }
        public string DisplayName { get { return "Steam"; } }
        public string ContinueWithoutUpload { get { return "Builds will continue without uploading to Steam."; } }
        public string BuildAnywayNotice { get { return "Builds will not be uploaded to Steam."; } }

        public IPublishOperation BeginCheckReady(PublishJob job, Action<UploadCheck> onComplete)
        {
            SteamPublishPayload payload = job == null ? null : job.Steam;
            if (payload == null)
            {
                onComplete(new UploadCheck { Error = "Enter a Steam username before uploading." });
                return CompletedPublishOperation.Instance;
            }

            return SteamUploader.CheckLoginAsync(
                payload.SteamCmdPath,
                payload.Username,
                onComplete);
        }

        public IPublishOperation BeginPublish(
            PublishJob job,
            PublishExecutionContext context,
            Action<PublishOperationResult> onComplete)
        {
            return SteamUploader.BeginPublish(job, context, onComplete);
        }
    }
}
