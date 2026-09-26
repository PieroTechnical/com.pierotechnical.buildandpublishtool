using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Pierotechnical.BuildAndUploadTool.Editor
{
    [FilePath("ProjectSettings/BuildAndPublishToolSettings.asset", FilePathAttribute.Location.ProjectFolder)]
    internal sealed class BuildToolProjectSettings : ScriptableSingleton<BuildToolProjectSettings>
    {
        internal const int CurrentSchemaVersion = 1;

        [SerializeField] int schemaVersion;
        [SerializeField] bool initialized;
        [SerializeField] string gameName;
        [SerializeField] string itchOwner;
        [SerializeField] string itchProject;
        [SerializeField] string steamAppId;
        [SerializeField] string steamBranch;
        [SerializeField] List<BuildTargetEntry> targets = new List<BuildTargetEntry>();

        internal int SchemaVersion
        {
            get { return schemaVersion; }
        }

        internal bool IsFutureVersion
        {
            get { return schemaVersion > CurrentSchemaVersion; }
        }

        internal string GameName
        {
            get { EnsureDefaults(); return gameName ?? string.Empty; }
            set { EnsureWritable(); gameName = value ?? string.Empty; }
        }

        internal string ItchOwner
        {
            get { EnsureDefaults(); return itchOwner ?? string.Empty; }
            set { EnsureWritable(); itchOwner = value ?? string.Empty; }
        }

        internal string ItchProject
        {
            get { EnsureDefaults(); return itchProject ?? string.Empty; }
            set { EnsureWritable(); itchProject = value ?? string.Empty; }
        }

        internal string SteamAppId
        {
            get { EnsureDefaults(); return steamAppId ?? string.Empty; }
            set { EnsureWritable(); steamAppId = value ?? string.Empty; }
        }

        internal string SteamBranch
        {
            get { EnsureDefaults(); return steamBranch ?? string.Empty; }
            set { EnsureWritable(); steamBranch = value ?? string.Empty; }
        }

        internal List<BuildTargetEntry> Targets
        {
            get
            {
                EnsureDefaults();
                return targets;
            }
            set
            {
                EnsureWritable();
                targets = value ?? new List<BuildTargetEntry>();
            }
        }

        internal void Persist()
        {
            EnsureWritable();
            NormalizeTargets(targets);
            BackupExistingFile();
            Save(true);
        }

        internal void Import(
            string importedGameName,
            string importedOwner,
            string importedProject,
            string importedSteamAppId,
            string importedSteamBranch,
            List<BuildTargetEntry> importedTargets)
        {
            EnsureWritable();
            gameName = importedGameName ?? string.Empty;
            itchOwner = importedOwner ?? string.Empty;
            itchProject = importedProject ?? string.Empty;
            steamAppId = importedSteamAppId ?? string.Empty;
            steamBranch = importedSteamBranch ?? string.Empty;
            targets = importedTargets ?? CreateDefaultTargets();
            NormalizeTargets(targets);
            Persist();
        }

        internal static void NormalizeTargets(List<BuildTargetEntry> entries)
        {
            if (entries == null)
                return;

            var ids = new HashSet<string>(StringComparer.Ordinal);
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < entries.Count; i++)
            {
                BuildTargetEntry entry = entries[i];
                if (entry == null)
                    continue;

                if (string.IsNullOrEmpty(entry.Id) || ids.Contains(entry.Id))
                    entry.Id = Guid.NewGuid().ToString("N");
                ids.Add(entry.Id);

                string key = BuildPaths.SanitizePathSegment(entry.FolderKey);
                if (string.IsNullOrEmpty(entry.FolderKey))
                    key = BuildPaths.SanitizePathSegment(entry.Name);
                string unique = key;
                int suffix = 2;
                while (keys.Contains(unique))
                {
                    unique = key + "-" + suffix;
                    suffix++;
                }

                entry.FolderKey = unique;
                keys.Add(unique);

                BuildTarget target = (BuildTarget)entry.TargetValue;
                if (!BuildTargetSet.SupportsSteam(target))
                    entry.PublishSteam = false;
            }
        }

        internal static List<BuildTargetEntry> CreateDefaultTargets()
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
                    false,
                    string.Empty,
                    true,
                    false));
            }

            return list;
        }

        void EnsureDefaults()
        {
            if (initialized || IsFutureVersion)
                return;

            initialized = true;
            schemaVersion = CurrentSchemaVersion;
            gameName = string.IsNullOrWhiteSpace(PlayerSettings.productName)
                ? "Game"
                : PlayerSettings.productName;
            itchOwner = BuildPaths.ToItchSlug(PlayerSettings.companyName);
            itchProject = BuildPaths.ToItchSlug(gameName);
            steamAppId = string.Empty;
            steamBranch = string.Empty;
            targets = CreateDefaultTargets();
        }

        void EnsureWritable()
        {
            EnsureDefaults();
            if (IsFutureVersion)
            {
                throw new InvalidOperationException(
                    "Project build settings were written by a newer package version.");
            }

            schemaVersion = CurrentSchemaVersion;
            initialized = true;
        }

        static void BackupExistingFile()
        {
            try
            {
                DirectoryInfo root = Directory.GetParent(Application.dataPath);
                if (root == null)
                    return;

                string path = Path.Combine(
                    root.FullName,
                    "ProjectSettings",
                    "BuildAndPublishToolSettings.asset");
                if (File.Exists(path))
                    File.Copy(path, path + ".backup", true);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Could not back up build tool settings: " + exception.Message);
            }
        }
    }
}
