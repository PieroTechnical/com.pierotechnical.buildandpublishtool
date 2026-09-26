using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Pierotechnical.BuildAndUploadTool.Editor
{
    internal static class BuildMaintenance
    {
        const string MenuRoot = "Tools/Build and Publish/Cleanup/";

        [MenuItem(MenuRoot + "Failed Work Folders\u2026")]
        static void CleanupFailedWork()
        {
            CleanupManagedFolder(
                BuildPaths.TempFolderName,
                "Delete failed and interrupted work folders?",
                "Successful artifacts and publisher history will not be removed.");
        }

        [MenuItem(MenuRoot + "Steam Chunk Cache\u2026")]
        static void CleanupSteamCache()
        {
            CleanupManagedFolder(
                BuildPaths.SteamCacheFolderName,
                "Delete the Steam chunk cache?",
                "The next Steam upload may take longer because every chunk will be regenerated.");
        }

        [MenuItem(MenuRoot + "Run Logs and History\u2026")]
        static void CleanupRunLogs()
        {
            CleanupManagedFolder(
                BuildLog.OperationsFolderName,
                "Delete all Build and Publish run logs and history?",
                "Built artifacts will not be removed. This action cannot be undone.");
        }

        [MenuItem(MenuRoot + "Previous Artifact Backups\u2026")]
        static void CleanupPreviousArtifacts()
        {
            string root = BuildsRoot();
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
                return;
            if (!EditorUtility.DisplayDialog(
                "Build and Publish cleanup",
                "Delete every .previous artifact backup under Builds?\n\n"
                    + "Current successful artifacts will not be removed.",
                "Delete Backups",
                "Cancel"))
            {
                return;
            }

            var backups = new List<string>();
            try
            {
                CollectPreviousFolders(root, root, backups);
                int deleted = 0;
                for (int i = 0; i < backups.Count; i++)
                {
                    string error;
                    if (SafeFileSystem.TryDeleteDirectory(root, backups[i], out error))
                        deleted++;
                    else
                        Debug.LogError("Could not delete " + backups[i] + ": " + error);
                }
                Debug.Log("Deleted " + deleted + " previous artifact backup(s).");
            }
            catch (Exception exception)
            {
                Debug.LogError("Could not clean previous artifacts: " + exception.Message);
            }
        }

        [MenuItem(MenuRoot + "Failed Work Folders\u2026", true)]
        [MenuItem(MenuRoot + "Steam Chunk Cache\u2026", true)]
        [MenuItem(MenuRoot + "Run Logs and History\u2026", true)]
        [MenuItem(MenuRoot + "Previous Artifact Backups\u2026", true)]
        static bool ValidateCleanup()
        {
            return !BuildQueueController.HasPending;
        }

        internal static string BuildsRoot()
        {
            DirectoryInfo root = Directory.GetParent(Application.dataPath);
            return root == null ? string.Empty : Path.Combine(root.FullName, "Builds");
        }

        static void CleanupManagedFolder(
            string folderName,
            string question,
            string detail)
        {
            string root = BuildsRoot();
            if (string.IsNullOrEmpty(root))
                return;
            string path = Path.Combine(root, folderName);
            if (!Directory.Exists(path))
            {
                EditorUtility.DisplayDialog(
                    "Build and Publish cleanup",
                    "Nothing to remove at " + path + ".",
                    "OK");
                return;
            }

            if (!EditorUtility.DisplayDialog(
                "Build and Publish cleanup",
                question + "\n\n" + detail + "\n\n" + path,
                "Delete",
                "Cancel"))
            {
                return;
            }

            string error;
            if (!SafeFileSystem.TryDeleteDirectory(root, path, out error))
                EditorUtility.DisplayDialog("Cleanup failed", error, "OK");
        }

        static void CollectPreviousFolders(
            string root,
            string directory,
            List<string> results)
        {
            foreach (string child in Directory.EnumerateDirectories(directory))
            {
                FileAttributes attributes = File.GetAttributes(child);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    continue;
                if (child.EndsWith(".previous", StringComparison.OrdinalIgnoreCase))
                {
                    string contained;
                    string error;
                    if (SafeFileSystem.TryGetContainedPath(root, child, out contained, out error))
                        results.Add(contained);
                    continue;
                }

                CollectPreviousFolders(root, child, results);
            }
        }
    }
}
