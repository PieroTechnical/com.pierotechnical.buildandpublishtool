using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Pierotechnical.BuildAndUploadTool.Editor
{
    internal static class ToolSettings
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
        const string TargetsKey = "Pierotechnical.BuildTool.Targets";
        const string CorruptTargetsBackupKey = "Pierotechnical.BuildTool.Targets.CorruptBackup";
        const int TargetListVersion = 1;

        public static string LoadGameName()
        {
            return BuildToolProjectSettings.instance.GameName;
        }

        public static void SaveGameName(string value)
        {
            BuildToolProjectSettings.instance.GameName = value;
            BuildToolProjectSettings.instance.Persist();
        }

        public static string LoadItchUser()
        {
            return BuildToolProjectSettings.instance.ItchOwner;
        }

        public static void SaveItchUser(string value)
        {
            BuildToolProjectSettings.instance.ItchOwner = value;
            BuildToolProjectSettings.instance.Persist();
        }

        public static string LoadItchGame()
        {
            return BuildToolProjectSettings.instance.ItchProject;
        }

        public static void SaveItchGame(string value)
        {
            BuildToolProjectSettings.instance.ItchProject = value;
            BuildToolProjectSettings.instance.Persist();
        }

        public static bool LoadEnabled(string platformId, bool defaultValue)
        {
            return LoadBool(EnabledPrefix + platformId, defaultValue);
        }

        static void SaveEnabled(string platformId, bool enabled)
        {
            SaveBool(EnabledPrefix + platformId, enabled);
        }

        public static bool LoadPublishItch()
        {
            return LoadBool(PublishItchKey, true);
        }

        static void SavePublishItch(bool enabled)
        {
            SaveBool(PublishItchKey, enabled);
        }

        public static bool LoadPublishSteam()
        {
            return LoadBool(PublishSteamKey, false);
        }

        static void SavePublishSteam(bool enabled)
        {
            SaveBool(PublishSteamKey, enabled);
        }

        public static string LoadSteamUser()
        {
            return LoadString(SteamUserKey, string.Empty);
        }

        public static void SaveSteamUser(string value)
        {
            SaveString(SteamUserKey, value);
        }

        public static string LoadSteamAppId()
        {
            return BuildToolProjectSettings.instance.SteamAppId;
        }

        public static void SaveSteamAppId(string value)
        {
            BuildToolProjectSettings.instance.SteamAppId = value;
            BuildToolProjectSettings.instance.Persist();
        }

        public static string LoadSteamBranch()
        {
            return BuildToolProjectSettings.instance.SteamBranch;
        }

        public static void SaveSteamBranch(string value)
        {
            BuildToolProjectSettings.instance.SteamBranch = value;
            BuildToolProjectSettings.instance.Persist();
        }

        public static string LoadSteamDepot(string platformId)
        {
            return LoadString(SteamDepotPrefix + platformId, string.Empty);
        }

        static void SaveSteamDepot(string platformId, string value)
        {
            SaveString(SteamDepotPrefix + platformId, value);
        }

        public static List<BuildTargetEntry> LoadTargets()
        {
            List<BuildTargetEntry> targets = BuildToolProjectSettings.instance.Targets;
            BuildToolProjectSettings.NormalizeTargets(targets);
            return targets;
        }

        public static void SaveTargets(List<BuildTargetEntry> targets)
        {
            BuildToolProjectSettings.instance.Targets = targets;
            BuildToolProjectSettings.instance.Persist();
        }

        internal static void SaveProjectConfiguration(
            string gameName,
            string itchOwner,
            string itchProject,
            string steamAppId,
            string steamBranch,
            List<BuildTargetEntry> targets)
        {
            BuildToolProjectSettings settings = BuildToolProjectSettings.instance;
            settings.GameName = gameName;
            settings.ItchOwner = itchOwner;
            settings.ItchProject = itchProject;
            settings.SteamAppId = steamAppId;
            settings.SteamBranch = steamBranch;
            settings.Targets = targets;
            settings.Persist();
        }

        public static bool HasLegacyProjectSettings()
        {
            if (HasProjectSettingsFile())
                return false;

            return EditorPrefs.HasKey(TargetsKey)
                || EditorPrefs.HasKey(ItchUserKey)
                || EditorPrefs.HasKey(ItchGameKey)
                || EditorPrefs.HasKey(SteamAppIdKey)
                || EditorPrefs.HasKey(SteamBranchKey);
        }

        public static bool TryImportLegacyProjectSettings(out string error)
        {
            error = null;
            try
            {
                string importedGame = LoadString(ItchGameKey, PlayerSettings.productName);
                BuildToolProjectSettings.instance.Import(
                    importedGame,
                    LoadString(ItchUserKey, PlayerSettings.companyName),
                    BuildPaths.ToItchSlug(importedGame),
                    LoadString(SteamAppIdKey, string.Empty),
                    LoadString(SteamBranchKey, string.Empty),
                    LoadLegacyTargets());
                return true;
            }
            catch (Exception exception)
            {
                error = "Could not import legacy settings: " + exception.Message;
                return false;
            }
        }

        public static void InitializeProjectDefaults()
        {
            BuildToolProjectSettings.instance.Persist();
        }

        public static string ProjectSettingsError()
        {
            return BuildToolProjectSettings.instance.IsFutureVersion
                ? "Project build settings were written by a newer package version. Update the package before editing them."
                : null;
        }

        static bool HasProjectSettingsFile()
        {
            DirectoryInfo root = Directory.GetParent(Application.dataPath);
            if (root == null)
                return false;

            return File.Exists(Path.Combine(
                root.FullName,
                "ProjectSettings",
                "BuildAndPublishToolSettings.asset"));
        }

        static List<BuildTargetEntry> LoadLegacyTargets()
        {
            if (!EditorPrefs.HasKey(TargetsKey))
                return CreateLegacySeed();

            string json = EditorPrefs.GetString(TargetsKey, string.Empty);
            TargetListFile file;
            try
            {
                file = string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<TargetListFile>(json);
            }
            catch (Exception exception)
            {
                return RecoverTargets(json, exception.Message);
            }

            if (file == null || file.Targets == null)
                return RecoverTargets(json, "The saved target list was empty or malformed.");

            if (file.Version > TargetListVersion)
            {
                Debug.LogError("The saved build target settings were written by a newer package version and were not loaded.");
                return new List<BuildTargetEntry>();
            }

            if (file.Version < TargetListVersion)
                ApplySharedPublishFlags(file.Targets);

            return file.Targets;
        }

        static List<BuildTargetEntry> RecoverTargets(string json, string reason)
        {
            if (!string.IsNullOrEmpty(json))
                EditorPrefs.SetString(CorruptTargetsBackupKey, json);

            Debug.LogError("Could not load build target settings. A backup was kept. " + reason);
            return CreateLegacySeed();
        }

        static void ApplySharedPublishFlags(List<BuildTargetEntry> targets)
        {
            bool publishItch = LoadPublishItch();
            bool publishSteam = LoadPublishSteam();
            for (int i = 0; i < targets.Count; i++)
            {
                BuildTargetEntry entry = targets[i];
                if (entry == null)
                    continue;

                entry.PublishItch = publishItch;
                entry.PublishSteam = publishSteam && BuildTargetSet.SupportsSteam((BuildTarget)entry.TargetValue);
            }
        }

        static List<BuildTargetEntry> CreateLegacySeed()
        {
            var list = new List<BuildTargetEntry>();
            PlatformCatalog.Entry[] platforms = PlatformCatalog.All();
            for (int i = 0; i < platforms.Length; i++)
            {
                list.Add(BuildTargetSet.Seed(
                    platforms[i].Id,
                    platforms[i].Label,
                    platforms[i].Target,
                    platforms[i].Channel,
                    LoadEnabled(platforms[i].Id, false),
                    LoadSteamDepot(platforms[i].Id),
                    LoadPublishItch(),
                    LoadPublishSteam()));
            }

            return list;
        }

        static string LoadString(string key, string missingValue)
        {
            if (!EditorPrefs.HasKey(key))
                return missingValue ?? string.Empty;

            return EditorPrefs.GetString(key, string.Empty);
        }

        static void SaveString(string key, string value)
        {
            EditorPrefs.SetString(key, value ?? string.Empty);
        }

        static bool LoadBool(string key, bool defaultValue)
        {
            if (!EditorPrefs.HasKey(key))
                return defaultValue;

            return EditorPrefs.GetBool(key, defaultValue);
        }

        static void SaveBool(string key, bool value)
        {
            EditorPrefs.SetBool(key, value);
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
            bool fileExisted = File.Exists(VersionFilePath);
            byte[] previousFile = null;
            string previousBundleVersion = PlayerSettings.bundleVersion;
            try
            {
                if (fileExisted)
                    previousFile = File.ReadAllBytes(VersionFilePath);
                SafeFileSystem.WriteAtomic(VersionFilePath, normalized + "\n");
                AssetDatabase.ImportAsset(VersionFilePath);
                PlayerSettings.bundleVersion = normalized;
                AssetDatabase.SaveAssets();
            }
            catch (Exception exception)
            {
                error = "Could not save the version: " + exception.Message;
                try
                {
                    if (fileExisted)
                        File.WriteAllBytes(VersionFilePath, previousFile ?? new byte[0]);
                    else if (File.Exists(VersionFilePath))
                        File.Delete(VersionFilePath);
                    PlayerSettings.bundleVersion = previousBundleVersion;
                    AssetDatabase.ImportAsset(VersionFilePath);
                    AssetDatabase.SaveAssets();
                }
                catch (Exception rollbackException)
                {
                    error += " The previous version could not be fully restored: "
                        + rollbackException.Message;
                }
                return false;
            }

            return true;
        }

        [Serializable]
        class TargetListFile
        {
            public int Version = 0;
            public List<BuildTargetEntry> Targets = new List<BuildTargetEntry>();
        }
    }
}
