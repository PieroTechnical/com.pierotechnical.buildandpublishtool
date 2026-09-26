using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor.PackageManager;
using UnityEngine;

namespace Pierotechnical.BuildAndUploadTool.Editor
{
    [Serializable]
    internal sealed class BuildRunRecord
    {
        internal const int CurrentSchemaVersion = 1;

        public int SchemaVersion = CurrentSchemaVersion;
        public string QueueId;
        public string PlanId;
        public string PackageVersion;
        public string UnityVersion;
        public string Status;
        public string StartedUtc;
        public string FinishedUtc;
        public long DurationMilliseconds;
        public string Version;
        public string GameName;
        public string OutputRoot;
        public string[] Scenes;
        public bool AndroidAppBundle;
        public string Notice;
        public List<BuildRunTargetRecord> Targets = new List<BuildRunTargetRecord>();
        public List<BuildRunPublisherRecord> Publishers = new List<BuildRunPublisherRecord>();
    }

    [Serializable]
    internal sealed class BuildRunTargetRecord
    {
        public string Id;
        public string Label;
        public int BuildTargetValue;
        public string OutputKey;
        public bool Succeeded;
        public string Message;
        public string ArtifactPath;
        public long ArtifactBytes;
        public string StartedUtc;
        public string FinishedUtc;
        public long DurationMilliseconds;
    }

    [Serializable]
    internal sealed class BuildRunPublisherRecord
    {
        public string JobId;
        public string ProviderId;
        public string Scope;
        public string Status;
        public string Message;
        public string StartedUtc;
        public string FinishedUtc;
        public long DurationMilliseconds;
        public int ExitCode;
        public bool TimedOut;
        public bool OutputTruncated;
        public bool TerminationUnconfirmed;
        public string LatestOutput;
    }

    [Serializable]
    internal sealed class BuildRunHistory
    {
        public int SchemaVersion = 1;
        public List<BuildRunHistoryEntry> Runs = new List<BuildRunHistoryEntry>();
    }

    [Serializable]
    internal sealed class BuildRunHistoryEntry
    {
        public string QueueId;
        public string Status;
        public string StartedUtc;
        public string FinishedUtc;
        public string LogPath;
        public string RecordPath;
    }

    internal static class BuildLog
    {
        internal const string FileName = "last-build.log";
        internal const string OperationsFolderName = ".build-and-publish";
        internal const string RunsFolderName = "runs";
        internal const string RunLogFileName = "run.log";
        internal const string RunRecordFileName = "run.json";
        internal const string HistoryFileName = "history.json";
        const int HistoryEntryLimit = 50;

        static readonly object WriteGate = new object();

        internal static string LogPath(string buildsRoot)
        {
            return Path.Combine(buildsRoot ?? string.Empty, FileName);
        }

        internal static string RunFolder(string buildsRoot, string queueId)
        {
            return Path.Combine(
                buildsRoot ?? string.Empty,
                OperationsFolderName,
                RunsFolderName,
                BuildPaths.ToPortableKey(queueId));
        }

        internal static string RunLogPath(string buildsRoot, string queueId)
        {
            return Path.Combine(RunFolder(buildsRoot, queueId), RunLogFileName);
        }

        internal static string RunRecordPath(string buildsRoot, string queueId)
        {
            return Path.Combine(RunFolder(buildsRoot, queueId), RunRecordFileName);
        }

        internal static string HistoryPath(string buildsRoot)
        {
            return Path.Combine(
                buildsRoot ?? string.Empty,
                OperationsFolderName,
                HistoryFileName);
        }

        internal static void Begin(BuildQueueState queue)
        {
            if (queue == null || queue.Plan == null)
                return;

            var builder = new StringBuilder();
            builder.Append("Build and Publish run ").AppendLine(queue.Id);
            builder.Append("Started: ").AppendLine(queue.StartedUtc);
            builder.Append("Plan: ").AppendLine(queue.Plan.Id);
            builder.Append("Version: ").AppendLine(queue.Plan.Version);
            builder.Append("Unity: ").AppendLine(Application.unityVersion);
            builder.Append("Package: ").AppendLine(PackageVersion());
            if (!string.IsNullOrEmpty(queue.Notice))
                builder.AppendLine(ProcessOutput.Redact(queue.Notice));

            string runLog = RunLogPath(queue.Plan.OutputRoot, queue.Id);
            Write(runLog, builder.ToString(), false);
            WriteLastSummary(
                queue.Plan.OutputRoot,
                "Build and Publish run started.\nRun: " + queue.Id
                    + "\nLog: " + runLog);
        }

        internal static void Append(string buildsRoot, string queueId, string message)
        {
            if (string.IsNullOrEmpty(buildsRoot) || string.IsNullOrEmpty(queueId))
                return;
            Write(
                RunLogPath(buildsRoot, queueId),
                ProcessOutput.Redact(message ?? string.Empty) + Environment.NewLine,
                true);
        }

        internal static void Complete(BuildQueueState queue, string summary)
        {
            if (queue == null || queue.Plan == null)
                return;

            Append(queue.Plan.OutputRoot, queue.Id, summary);
            BuildRunRecord record = CreateRecord(queue);
            string recordPath = RunRecordPath(queue.Plan.OutputRoot, queue.Id);
            lock (WriteGate)
            {
                try
                {
                    SafeFileSystem.WriteAtomic(
                        recordPath,
                        JsonUtility.ToJson(record, true));
                    UpdateHistory(queue.Plan.OutputRoot, record);
                }
                catch (Exception exception)
                {
                    Append(
                        queue.Plan.OutputRoot,
                        queue.Id,
                        "Could not write structured run record: " + exception.Message);
                }
            }

            WriteLastSummary(
                queue.Plan.OutputRoot,
                ProcessOutput.Redact(summary)
                    + Environment.NewLine + "Log: "
                    + RunLogPath(queue.Plan.OutputRoot, queue.Id)
                    + Environment.NewLine + "Record: " + recordPath);
        }

        internal static BuildRunRecord CreateRecord(BuildQueueState queue)
        {
            var record = new BuildRunRecord
            {
                QueueId = queue.Id,
                PlanId = queue.Plan == null ? string.Empty : queue.Plan.Id,
                PackageVersion = PackageVersion(),
                UnityVersion = Application.unityVersion,
                Status = queue.Phase.ToString(),
                StartedUtc = queue.StartedUtc,
                FinishedUtc = queue.FinishedUtc,
                DurationMilliseconds = DurationMilliseconds(queue.StartedUtc, queue.FinishedUtc),
                Version = queue.Plan == null ? string.Empty : queue.Plan.Version,
                GameName = queue.Plan == null ? string.Empty : queue.Plan.GameName,
                OutputRoot = queue.Plan == null ? string.Empty : queue.Plan.OutputRoot,
                Scenes = queue.Plan == null || queue.Plan.Scenes == null
                    ? new string[0]
                    : (string[])queue.Plan.Scenes.Clone(),
                AndroidAppBundle = queue.Plan != null && queue.Plan.AndroidAppBundle,
                Notice = ProcessOutput.Redact(queue.Notice ?? string.Empty)
            };

            AddTargetRecords(queue, record);
            AddPublisherRecords(queue, record);
            return record;
        }

        internal static BuildRunHistory LoadHistory(string buildsRoot)
        {
            string path = HistoryPath(buildsRoot);
            if (!File.Exists(path))
                return new BuildRunHistory();
            try
            {
                BuildRunHistory history = JsonUtility.FromJson<BuildRunHistory>(
                    File.ReadAllText(path));
                if (history == null)
                    history = new BuildRunHistory();
                if (history.Runs == null)
                    history.Runs = new List<BuildRunHistoryEntry>();
                return history;
            }
            catch (Exception)
            {
                return new BuildRunHistory();
            }
        }

        static void AddTargetRecords(BuildQueueState queue, BuildRunRecord record)
        {
            if (queue.Plan == null || queue.Plan.Targets == null)
                return;
            for (int i = 0; i < queue.Plan.Targets.Count; i++)
            {
                BuildTargetPlan target = queue.Plan.Targets[i];
                TargetResult result = queue.TargetResults != null && i < queue.TargetResults.Count
                    ? queue.TargetResults[i]
                    : null;
                record.Targets.Add(new BuildRunTargetRecord
                {
                    Id = target == null ? string.Empty : target.Id,
                    Label = target == null ? string.Empty : target.Label,
                    BuildTargetValue = target == null ? 0 : target.TargetValue,
                    OutputKey = target == null ? string.Empty : target.OutputKey,
                    Succeeded = result != null && result.BuildSucceeded,
                    Message = ProcessOutput.Redact(result == null ? "Not run." : result.Message),
                    ArtifactPath = result == null ? string.Empty : result.ArtifactPath,
                    ArtifactBytes = result == null ? 0L : result.ArtifactBytes,
                    StartedUtc = result == null ? string.Empty : result.StartedUtc,
                    FinishedUtc = result == null ? string.Empty : result.FinishedUtc,
                    DurationMilliseconds = result == null
                        ? 0L
                        : DurationMilliseconds(result.StartedUtc, result.FinishedUtc)
                });
            }
        }

        static void AddPublisherRecords(BuildQueueState queue, BuildRunRecord record)
        {
            if (queue.PublishResults == null)
                return;
            for (int i = 0; i < queue.PublishResults.Count; i++)
            {
                PublishJobResult result = queue.PublishResults[i];
                if (result == null)
                    continue;
                PublishJob job = FindJob(queue.Plan, result.JobId);
                record.Publishers.Add(new BuildRunPublisherRecord
                {
                    JobId = result.JobId,
                    ProviderId = result.ProviderId,
                    Scope = job == null ? string.Empty : job.Scope.ToString(),
                    Status = result.Status.ToString(),
                    Message = ProcessOutput.Redact(result.Message),
                    StartedUtc = result.StartedUtc,
                    FinishedUtc = result.FinishedUtc,
                    DurationMilliseconds = result.DurationMilliseconds > 0
                        ? result.DurationMilliseconds
                        : DurationMilliseconds(result.StartedUtc, result.FinishedUtc),
                    ExitCode = result.ExitCode,
                    TimedOut = result.TimedOut,
                    OutputTruncated = result.OutputTruncated,
                    TerminationUnconfirmed = result.TerminationUnconfirmed,
                    LatestOutput = ProcessOutput.Redact(result.LatestOutput)
                });
            }
        }

        static PublishJob FindJob(BuildPlan plan, string jobId)
        {
            if (plan == null || plan.PublishJobs == null)
                return null;
            for (int i = 0; i < plan.PublishJobs.Count; i++)
            {
                if (plan.PublishJobs[i] != null && plan.PublishJobs[i].Id == jobId)
                    return plan.PublishJobs[i];
            }
            return null;
        }

        static void UpdateHistory(string buildsRoot, BuildRunRecord record)
        {
            BuildRunHistory history = LoadHistory(buildsRoot);
            for (int i = history.Runs.Count - 1; i >= 0; i--)
            {
                if (history.Runs[i] != null && history.Runs[i].QueueId == record.QueueId)
                    history.Runs.RemoveAt(i);
            }

            history.Runs.Insert(0, new BuildRunHistoryEntry
            {
                QueueId = record.QueueId,
                Status = record.Status,
                StartedUtc = record.StartedUtc,
                FinishedUtc = record.FinishedUtc,
                LogPath = RunLogPath(buildsRoot, record.QueueId),
                RecordPath = RunRecordPath(buildsRoot, record.QueueId)
            });
            if (history.Runs.Count > HistoryEntryLimit)
                history.Runs.RemoveRange(HistoryEntryLimit, history.Runs.Count - HistoryEntryLimit);
            SafeFileSystem.WriteAtomic(
                HistoryPath(buildsRoot),
                JsonUtility.ToJson(history, true));
        }

        static long DurationMilliseconds(string startedUtc, string finishedUtc)
        {
            DateTime started;
            DateTime finished;
            if (!DateTime.TryParse(
                    startedUtc,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out started)
                || !DateTime.TryParse(
                    finishedUtc,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out finished))
            {
                return 0L;
            }

            return (long)Math.Max(0d, (finished - started).TotalMilliseconds);
        }

        static string PackageVersion()
        {
            try
            {
                PackageInfo package = PackageInfo.FindForAssembly(typeof(BuildLog).Assembly);
                return package == null ? string.Empty : package.version;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        static void WriteLastSummary(string buildsRoot, string contents)
        {
            if (string.IsNullOrEmpty(buildsRoot))
                return;
            lock (WriteGate)
            {
                try
                {
                    SafeFileSystem.WriteAtomic(
                        LogPath(buildsRoot),
                        ProcessOutput.Redact(contents ?? string.Empty) + Environment.NewLine);
                }
                catch (Exception)
                {
                    // The per-run log remains authoritative.
                }
            }
        }

        static void Write(string path, string contents, bool append)
        {
            if (string.IsNullOrEmpty(path))
                return;
            lock (WriteGate)
            {
                try
                {
                    string directory = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(directory))
                        Directory.CreateDirectory(directory);
                    string safe = ProcessOutput.Redact(contents ?? string.Empty);
                    if (append)
                        File.AppendAllText(path, safe, new UTF8Encoding(false));
                    else
                        File.WriteAllText(path, safe, new UTF8Encoding(false));
                }
                catch (Exception)
                {
                    // Queue execution continues even if diagnostics cannot be written.
                }
            }
        }
    }
}
