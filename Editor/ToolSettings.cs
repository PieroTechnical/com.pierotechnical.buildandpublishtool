using System;
using System.IO;
using System.Text;
using UnityEditor;

namespace Pierotechnical.BuildAndUploadTool.Editor
{
    public static class ToolSettings
    {
        public const string VersionFilePath = "Assets/version.txt";
        const string ItchUserKey = "Pierotechnical.BuildTool.ItchUser";
        const string ItchGameKey = "Pierotechnical.BuildTool.ItchGame";
        const string EnabledPrefix = "Pierotechnical.BuildTool.Enabled.";
        const string PublishItchKey = "Pierotechnical.BuildTool.PublishItch";
        const string PublishSteamKey = "Pierotechnical.BuildTool.PublishSteam";
        const string SteamUserKey = "Pierotechnical.BuildTool.SteamUser";
        const string SteamAppIdKey = "Pierotechnical.BuildTool.SteamAppId";
        const string SteamBranchKey = "Pierotechnical.BuildTool.SteamBranch";
        const string SteamDepotPrefix = "Pierotechnical.BuildTool.SteamDepot.";

        public static string LoadItchUser()
        {
            if (EditorPrefs.HasKey(ItchUserKey))
                return EditorPrefs.GetString(ItchUserKey, string.Empty);

            return PlayerSettings.companyName ?? string.Empty;
        }

        public static void SaveItchUser(string value)
        {
            EditorPrefs.SetString(ItchUserKey, value ?? string.Empty);
        }

        public static string LoadItchGame()
        {
            if (EditorPrefs.HasKey(ItchGameKey))
                return EditorPrefs.GetString(ItchGameKey, string.Empty);

            return PlayerSettings.productName ?? string.Empty;
        }

        public static void SaveItchGame(string value)
        {
            EditorPrefs.SetString(ItchGameKey, value ?? string.Empty);
        }

        public static bool LoadEnabled(string platformId, bool defaultValue)
        {
            string key = EnabledPrefix + platformId;
            if (!EditorPrefs.HasKey(key))
                return defaultValue;

            return EditorPrefs.GetBool(key, defaultValue);
        }

        public static void SaveEnabled(string platformId, bool enabled)
        {
            EditorPrefs.SetBool(EnabledPrefix + platformId, enabled);
        }

        public static bool LoadPublishItch()
        {
            return EditorPrefs.GetBool(PublishItchKey, true);
        }

        public static void SavePublishItch(bool enabled)
        {
            EditorPrefs.SetBool(PublishItchKey, enabled);
        }

        public static bool LoadPublishSteam()
        {
            return EditorPrefs.GetBool(PublishSteamKey, false);
        }

        public static void SavePublishSteam(bool enabled)
        {
            EditorPrefs.SetBool(PublishSteamKey, enabled);
        }

        public static string LoadSteamUser()
        {
            return EditorPrefs.GetString(SteamUserKey, string.Empty);
        }

        public static void SaveSteamUser(string value)
        {
            EditorPrefs.SetString(SteamUserKey, value ?? string.Empty);
        }

        public static string LoadSteamAppId()
        {
            return EditorPrefs.GetString(SteamAppIdKey, string.Empty);
        }

        public static void SaveSteamAppId(string value)
        {
            EditorPrefs.SetString(SteamAppIdKey, value ?? string.Empty);
        }

        public static string LoadSteamBranch()
        {
            return EditorPrefs.GetString(SteamBranchKey, string.Empty);
        }

        public static void SaveSteamBranch(string value)
        {
            EditorPrefs.SetString(SteamBranchKey, value ?? string.Empty);
        }

        public static string LoadSteamDepot(string platformId)
        {
            return EditorPrefs.GetString(SteamDepotPrefix + platformId, string.Empty);
        }

        public static void SaveSteamDepot(string platformId, string value)
        {
            EditorPrefs.SetString(SteamDepotPrefix + platformId, value ?? string.Empty);
        }

        public static bool TryReadVersionFile(out string version)
        {
            version = null;
            if (!File.Exists(VersionFilePath))
                return false;

            version = File.ReadAllText(VersionFilePath).Trim();
            return true;
        }

        public static bool TryCommitVersion(string version, out string error)
        {
            error = null;
            int major;
            int minor;
            int patch;
            if (!GameVersion.TryParse(version, out major, out minor, out patch))
            {
                error = "Version must be major.minor.patch using non-negative integers.";
                return false;
            }

            string normalized = major + "." + minor + "." + patch;
            try
            {
                File.WriteAllText(VersionFilePath, normalized + "\n", new UTF8Encoding(false));
                AssetDatabase.ImportAsset(VersionFilePath);
                PlayerSettings.bundleVersion = normalized;
                AssetDatabase.SaveAssets();
            }
            catch (Exception exception)
            {
                error = "Could not save the version: " + exception.Message;
                return false;
            }

            return true;
        }
    }
}
