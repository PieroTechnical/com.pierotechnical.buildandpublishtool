using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Pierotechnical.BuildAndUploadTool.Editor
{
    internal static class BuildRunStatusSection
    {
        internal static void Draw(
            BuildQueueStatus status,
            float availableWidth,
            Action cancel,
            Action openLog,
            Action revealArtifact,
            Action openItch,
            Action openSteam,
            Action clearFinished)
        {
            if (status == null)
                return;

            bool terminal = BuildQueueController.IsTerminal(status.Phase);
            MessageType messageType = status.Phase == BuildQueuePhase.Failed
                ? MessageType.Error
                : status.Phase == BuildQueuePhase.Cancelled
                    ? MessageType.Warning
                    : MessageType.Info;
            string heading = terminal ? "Latest run" : "Build and Publish is running";
            EditorGUILayout.HelpBox(
                heading + ": " + FriendlyPhase(status.Phase),
                messageType);

            EditorGUILayout.LabelField(
                "Progress",
                status.CompletedTargets + " of " + status.TotalTargets + " targets");
            if (!string.IsNullOrEmpty(status.CurrentTarget))
                EditorGUILayout.LabelField("Target", status.CurrentTarget);
            if (!string.IsNullOrEmpty(status.CurrentPublisher))
                EditorGUILayout.LabelField("Publisher", status.CurrentPublisher);
            EditorGUILayout.LabelField("Elapsed", FormatDuration(status.ElapsedSeconds));
            if (!string.IsNullOrEmpty(status.LatestMessage))
            {
                EditorGUILayout.LabelField("Latest");
                EditorGUILayout.SelectableLabel(
                    status.LatestMessage,
                    EditorStyles.textArea,
                    GUILayout.MinHeight(EditorGUIUtility.singleLineHeight * 2f));
            }

            DrawButtons(
                status,
                availableWidth,
                cancel,
                openLog,
                revealArtifact,
                openItch,
                openSteam,
                clearFinished);
        }

        static void DrawButtons(
            BuildQueueStatus status,
            float availableWidth,
            Action cancel,
            Action openLog,
            Action revealArtifact,
            Action openItch,
            Action openSteam,
            Action clearFinished)
        {
            bool horizontal = availableWidth >= 600f;
            if (horizontal)
                EditorGUILayout.BeginHorizontal();

            if (!BuildQueueController.IsTerminal(status.Phase))
            {
                EditorGUI.BeginDisabledGroup(!status.CanCancel || status.CancellationRequested);
                string cancelLabel = status.Phase == BuildQueuePhase.Building
                    ? "Stop After Current Build"
                    : "Cancel";
                if (GUILayout.Button(new GUIContent(
                    cancelLabel,
                    status.Phase == BuildQueuePhase.Building
                        ? "Unity builds cannot be interrupted safely. The queue will stop when this target finishes."
                        : "Cancel the current staging or external process and stop the queue.")))
                {
                    cancel();
                }
                EditorGUI.EndDisabledGroup();
            }
            else if (GUILayout.Button("Dismiss Run"))
            {
                clearFinished();
            }

            if (GUILayout.Button("Open Log"))
                openLog();
            EditorGUI.BeginDisabledGroup(string.IsNullOrEmpty(status.LatestArtifactPath));
            if (GUILayout.Button("Reveal Artifact"))
                revealArtifact();
            EditorGUI.EndDisabledGroup();
            if (!string.IsNullOrEmpty(status.ItchUrl) && GUILayout.Button("itch.io"))
                openItch();
            if (!string.IsNullOrEmpty(status.SteamUrl) && GUILayout.Button("Steamworks"))
                openSteam();

            if (horizontal)
                EditorGUILayout.EndHorizontal();
        }

        static string FriendlyPhase(BuildQueuePhase phase)
        {
            switch (phase)
            {
                case BuildQueuePhase.CheckingPublishers:
                    return "Checking publishers";
                case BuildQueuePhase.SwitchingTarget:
                    return "Switching target";
                case BuildQueuePhase.PublishingArtifact:
                    return "Publishing artifact";
                case BuildQueuePhase.PublishingAggregate:
                    return "Publishing aggregate";
                case BuildQueuePhase.RestoringTarget:
                    return "Restoring Editor settings";
                default:
                    return phase.ToString();
            }
        }

        static string FormatDuration(long totalSeconds)
        {
            TimeSpan duration = TimeSpan.FromSeconds(Math.Max(0L, totalSeconds));
            if (duration.TotalHours >= 1d)
                return ((int)duration.TotalHours) + duration.ToString(@"\:mm\:ss");
            return duration.ToString(@"mm\:ss");
        }
    }

    internal static class BuildPreflightSection
    {
        internal static void Draw(IList<ValidationIssue> issues)
        {
            if (issues == null || issues.Count == 0)
                return;

            GUILayout.Space(8f);
            EditorGUILayout.LabelField("Preflight", EditorStyles.boldLabel);
            for (int i = 0; i < issues.Count; i++)
            {
                ValidationIssue issue = issues[i];
                if (issue == null || string.IsNullOrEmpty(issue.Message))
                    continue;
                EditorGUILayout.HelpBox(
                    issue.Message,
                    issue.Severity == ValidationSeverity.Error
                        ? MessageType.Error
                        : MessageType.Warning);
            }
        }
    }

    internal static class BuildHistorySection
    {
        internal static bool Draw(BuildRunHistory history, bool open)
        {
            if (history == null || history.Runs == null || history.Runs.Count == 0)
                return open;

            GUILayout.Space(8f);
            open = EditorGUILayout.Foldout(
                open,
                "Run history (" + history.Runs.Count + ")",
                true);
            if (!open)
                return false;

            int count = Math.Min(5, history.Runs.Count);
            for (int i = 0; i < count; i++)
            {
                BuildRunHistoryEntry entry = history.Runs[i];
                if (entry == null)
                    continue;
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField(
                    string.IsNullOrEmpty(entry.Status) ? "Unknown" : entry.Status,
                    string.IsNullOrEmpty(entry.FinishedUtc)
                        ? entry.StartedUtc
                        : entry.FinishedUtc);
                EditorGUILayout.SelectableLabel(
                    entry.QueueId ?? string.Empty,
                    EditorStyles.miniLabel,
                    GUILayout.Height(EditorGUIUtility.singleLineHeight));
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Reveal Log"))
                    EditorUtility.RevealInFinder(entry.LogPath);
                if (GUILayout.Button("Reveal Record"))
                    EditorUtility.RevealInFinder(entry.RecordPath);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.HelpBox(
                "History and caches are retained until you use Tools > Build and Publish > Cleanup.",
                MessageType.Info);
            return true;
        }
    }
}
