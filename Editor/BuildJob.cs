using System;
using System.Collections.Generic;

namespace Pierotechnical.BuildAndUploadTool.Editor
{
    [Serializable]
    public class BuildRequest
    {
        public string Version;
        public string ItchUser;
        public string ItchGame;
        public string PlatformId;
        public string ChannelName;
        public string Label;
        public string OutputRoot;
        public bool Upload;
        public bool PublishItch;
        public bool PublishSteam;
        public string SteamAppId;
        public string SteamDepotId;
        public string SteamBranch;
        public string SteamUser;
        public string SteamCmdPath;
    }

    [Serializable]
    public class TargetResult
    {
        public string Label;
        public bool BuildSucceeded;
        public bool UploadAttempted;
        public bool UploadSucceeded;
        public bool Cancelled;
        public string Message;
    }

    [Serializable]
    public class BuildQueueState
    {
        public string Id;
        public string Notice;
        public List<BuildRequest> Pending = new List<BuildRequest>();
        public List<TargetResult> Completed = new List<TargetResult>();
        public bool ScenesSaved;
        public bool VersionCommitted;
        public bool UploadGateResolved;
        public bool SteamUploadAttempted;
        public bool SteamUploadFinished;
        public int SwitchAttemptedIndex = -1;
        public string SwitchDomainToken;
        public string SwitchStartedUtc;
        public bool SwitchObserved;
        public int BuildAttemptedIndex = -1;
    }
}
