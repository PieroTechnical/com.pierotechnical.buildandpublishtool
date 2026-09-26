using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;

namespace Pierotechnical.BuildAndUploadTool.Editor
{
    [Serializable]
    internal sealed class BuildTargetEntry
    {
        public string Id;
        public int TargetValue;
        public string Name;
        public string Channel;
        public string SteamDepotId;
        public bool Enabled;
        public string FolderKey;
        public bool PublishItch;
        public bool PublishSteam;
    }

    internal static class BuildTargetSet
    {
        public static BuildTargetEntry Seed(string folderKey, string name, BuildTarget target, string channel, bool enabled, string depotId, bool publishItch, bool publishSteam)
        {
            return new BuildTargetEntry
            {
                Id = folderKey,
                TargetValue = (int)target,
                Name = name,
                Channel = channel ?? string.Empty,
                SteamDepotId = depotId ?? string.Empty,
                Enabled = enabled,
                FolderKey = folderKey,
                PublishItch = publishItch,
                PublishSteam = publishSteam && SupportsSteam(target)
            };
        }

        public static BuildTargetEntry Create(BuildTarget target, IList<BuildTargetEntry> existing)
        {
            string name = DisplayName(target);
            return new BuildTargetEntry
            {
                Id = Guid.NewGuid().ToString("N"),
                TargetValue = (int)target,
                Name = name,
                Channel = BuildPaths.ToItchSlug(name),
                SteamDepotId = string.Empty,
                Enabled = true,
                FolderKey = AllocateFolderKey(name, existing),
                PublishItch = true,
                PublishSteam = false
            };
        }

        public static string AllocateFolderKey(string name, IList<BuildTargetEntry> existing)
        {
            string baseKey = BuildPaths.ToPortableKey(string.IsNullOrWhiteSpace(name) ? "target" : name.Trim());
            string candidate = baseKey;
            int suffix = 2;
            while (FolderKeyTaken(candidate, existing))
            {
                candidate = baseKey + "-" + suffix.ToString(CultureInfo.InvariantCulture);
                suffix++;
            }

            return candidate;
        }

        public static BuildTarget[] ListAddableTargets()
        {
            return BuildTargetCatalog.AllTargets();
        }

        public static string DisplayName(BuildTarget target)
        {
            return BuildTargetCatalog.Find(target).Label;
        }

        public static bool SupportsSteam(BuildTarget target)
        {
            return BuildTargetCatalog.Find(target).SupportsSteam;
        }

        internal static bool Publishes(
            IList<BuildTargetEntry> targets,
            IList<int> indices,
            bool steam)
        {
            if (indices == null || targets == null)
                return false;

            for (int i = 0; i < indices.Count; i++)
            {
                int index = indices[i];
                if (index < 0 || index >= targets.Count || targets[index] == null)
                    continue;

                BuildTargetEntry entry = targets[index];
                if (steam)
                {
                    if (entry.PublishSteam && SupportsSteam((BuildTarget)entry.TargetValue))
                        return true;
                }
                else if (entry.PublishItch)
                {
                    return true;
                }
            }

            return false;
        }

        public static string SteamPlatformId(BuildTarget target)
        {
            return BuildTargetCatalog.Find(target).SteamPlatformId;
        }

        public static bool ChangeTarget(BuildTargetEntry entry, BuildTarget target)
        {
            if (entry == null || entry.TargetValue == (int)target)
                return false;

            BuildTarget previous = (BuildTarget)entry.TargetValue;
            BuildTargetDescriptor previousDescriptor = BuildTargetCatalog.Find(previous);
            BuildTargetDescriptor nextDescriptor = BuildTargetCatalog.Find(target);
            if (string.IsNullOrEmpty(entry.Name) || entry.Name == previousDescriptor.Label)
                entry.Name = nextDescriptor.Label;
            if (string.IsNullOrEmpty(entry.Channel) || entry.Channel == previousDescriptor.DefaultChannel)
                entry.Channel = nextDescriptor.DefaultChannel;

            entry.TargetValue = (int)target;
            entry.SteamDepotId = string.Empty;
            if (!nextDescriptor.SupportsSteam)
                entry.PublishSteam = false;
            return true;
        }

        public static int DropIndex(int from, int insertIndex, int count)
        {
            if (count <= 1)
                return from;

            int to = insertIndex;
            if (to > from)
                to--;
            if (to < 0)
                to = 0;
            if (to > count - 1)
                to = count - 1;
            return to;
        }

        public static void Move<T>(IList<T> items, int from, int insertIndex)
        {
            if (items == null || from < 0 || from >= items.Count || items.Count < 2)
                return;

            int to = DropIndex(from, insertIndex, items.Count);
            if (to == from)
                return;

            T item = items[from];
            items.RemoveAt(from);
            items.Insert(to, item);
        }

        static bool FolderKeyTaken(string candidate, IList<BuildTargetEntry> existing)
        {
            if (existing == null || string.IsNullOrEmpty(candidate))
                return false;

            for (int i = 0; i < existing.Count; i++)
            {
                if (existing[i] == null)
                    continue;
                if (string.Equals(existing[i].FolderKey, candidate, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

    }
}
