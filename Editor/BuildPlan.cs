using System;
using System.Collections.Generic;

namespace Pierotechnical.BuildAndUploadTool.Editor
{
    internal enum ValidationSeverity
    {
        Warning,
        Error
    }

    [Serializable]
    internal sealed class ValidationIssue
    {
        public ValidationSeverity Severity;
        public string Code;
        public string Field;
        public string TargetId;
        public string Message;
    }

    internal sealed class BuildPlanResult
    {
        internal BuildPlan Plan;
        internal readonly List<ValidationIssue> Issues = new List<ValidationIssue>();

        internal bool HasErrors
        {
            get
            {
                for (int i = 0; i < Issues.Count; i++)
                {
                    if (Issues[i] != null && Issues[i].Severity == ValidationSeverity.Error)
                        return true;
                }

                return false;
            }
        }
    }

    internal sealed class BuildConfiguration
    {
        internal IList<BuildTargetEntry> Targets;
        internal IList<int> SelectedIndices;
        internal bool Upload;
        internal string Version;
        internal string GameName;
        internal string ItchOwner;
        internal string ItchProject;
        internal string ButlerPath;
        internal string SteamUser;
        internal string SteamAppId;
        internal string SteamBranch;
        internal string SteamCmdPath;
    }

    internal sealed class BuildEnvironmentSnapshot
    {
        internal string ProjectRoot;
        internal string[] Scenes = new string[0];
        internal bool AndroidAppBundle;
        internal int ActiveBuildTargetValue;
        internal bool IsPlaying;
        internal bool IsCompiling;
        internal readonly HashSet<int> UnsupportedTargetValues = new HashSet<int>();
    }

    [Serializable]
    internal sealed class BuildPlan
    {
        internal const int CurrentSchemaVersion = 1;

        public int SchemaVersion = CurrentSchemaVersion;
        public string Id;
        public string Version;
        public string GameName;
        public string OutputRoot;
        public string[] Scenes;
        public bool AndroidAppBundle;
        public int OriginalBuildTargetValue;
        public bool OriginalAndroidAppBundle;
        public List<BuildTargetPlan> Targets = new List<BuildTargetPlan>();
        public List<PublishJob> PublishJobs = new List<PublishJob>();
    }

    [Serializable]
    internal sealed class BuildTargetPlan
    {
        public string Id;
        public int TargetValue;
        public string DescriptorId;
        public string Label;
        public string OutputKey;
    }

    internal enum PublishJobScope
    {
        Artifact,
        Aggregate
    }

    [Serializable]
    internal sealed class PublishJob
    {
        public string Id;
        public string ProviderId;
        public PublishJobScope Scope;
        public int TargetIndex = -1;
        public List<int> TargetIndices = new List<int>();
        public ItchPublishPayload Itch;
        public SteamPublishPayload Steam;
    }

    [Serializable]
    internal sealed class ItchPublishPayload
    {
        public string Owner;
        public string Project;
        public string Channel;
        public string ButlerPath;
        public string Version;
    }

    [Serializable]
    internal sealed class SteamPublishPayload
    {
        public string AppId;
        public string Branch;
        public string Username;
        public string SteamCmdPath;
        public List<SteamTargetPayload> Targets = new List<SteamTargetPayload>();
    }

    [Serializable]
    internal sealed class SteamTargetPayload
    {
        public int TargetIndex;
        public string DepotId;
    }

    internal static class PublisherIds
    {
        internal const string Itch = "itch";
        internal const string Steam = "steam";
    }
}
