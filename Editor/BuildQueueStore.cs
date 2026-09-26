using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace Pierotechnical.BuildAndUploadTool.Editor
{
    internal static class BuildQueueStore
    {
        const string FolderName = "BuildAndPublishTool";
        const string QueueFileName = "queue.json";

        internal static string QueuePath
        {
            get
            {
                DirectoryInfo root = Directory.GetParent(Application.dataPath);
                if (root == null)
                    return null;
                return Path.Combine(root.FullName, "Library", FolderName, QueueFileName);
            }
        }

        internal static bool TryLoad(out BuildQueueState state, out string error)
        {
            state = null;
            error = null;
            string path = QueuePath;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return true;

            try
            {
                state = Deserialize(File.ReadAllText(path));
                return true;
            }
            catch (Exception exception)
            {
                string backup = path + ".previous";
                try
                {
                    if (File.Exists(backup))
                    {
                        state = Deserialize(File.ReadAllText(backup));
                        state.Notice = BuildQueueController.AppendNotice(
                            state.Notice,
                            "Recovered the build queue from its previous journal.");
                        BackupCorrupt(path);
                        File.Copy(backup, path, true);
                        return true;
                    }
                }
                catch (Exception backupException)
                {
                    error = "Could not restore the build queue or its backup: "
                        + exception.Message + " Backup: " + backupException.Message;
                }

                if (string.IsNullOrEmpty(error))
                    error = "Could not restore the build queue: " + exception.Message;
                BackupCorrupt(path);
                TryDelete(path);
                state = null;
                return false;
            }
        }

        internal static bool TrySave(BuildQueueState state, out string error)
        {
            error = null;
            if (state == null)
            {
                error = "Build queue state was missing.";
                return false;
            }

            string path = QueuePath;
            if (string.IsNullOrEmpty(path))
            {
                error = "Could not resolve the queue journal path.";
                return false;
            }

            string temp = path + ".tmp";
            string backup = path + ".previous";
            try
            {
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                File.WriteAllText(temp, JsonUtility.ToJson(state, true), new UTF8Encoding(false));
                if (!File.Exists(path))
                {
                    File.Move(temp, path);
                    return true;
                }

                try
                {
                    File.Replace(temp, path, backup);
                }
                catch (PlatformNotSupportedException)
                {
                    File.Copy(path, backup, true);
                    TryDelete(path);
                    File.Move(temp, path);
                }

                return true;
            }
            catch (Exception exception)
            {
                error = "Could not save the build queue: " + exception.Message;
                TryDelete(temp);
                return false;
            }
        }

        internal static void Clear()
        {
            string path = QueuePath;
            if (string.IsNullOrEmpty(path))
                return;
            TryDelete(path);
            TryDelete(path + ".tmp");
            TryDelete(path + ".previous");
        }

        static BuildQueueState Deserialize(string json)
        {
            BuildQueueState value = JsonUtility.FromJson<BuildQueueState>(json);
            if (value == null || string.IsNullOrEmpty(value.Id))
                throw new InvalidDataException("The queue journal did not contain a queue ID.");
            if (value.SchemaVersion != BuildQueueState.CurrentSchemaVersion)
            {
                throw new InvalidDataException(
                    "Queue schema " + value.SchemaVersion + " is not supported by schema "
                    + BuildQueueState.CurrentSchemaVersion + ".");
            }

            Normalize(value);
            return value;
        }

        static void Normalize(BuildQueueState state)
        {
            if (state.TargetResults == null)
                state.TargetResults = new System.Collections.Generic.List<TargetResult>();
            if (state.PublishResults == null)
                state.PublishResults = new System.Collections.Generic.List<PublishJobResult>();
            if (state.DisabledPublisherIds == null)
                state.DisabledPublisherIds = new System.Collections.Generic.List<string>();
            if (state.Plan == null)
                throw new InvalidDataException("The queue journal did not contain a build plan.");
            if (state.Plan.SchemaVersion != BuildPlan.CurrentSchemaVersion)
                throw new InvalidDataException("The persisted build plan schema is not supported.");
        }

        static void BackupCorrupt(string path)
        {
            try
            {
                if (!File.Exists(path))
                    return;
                string stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
                File.Copy(path, path + ".corrupt-" + stamp, true);
            }
            catch (Exception)
            {
                // The original path remains available when backup fails.
            }
        }

        static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception)
            {
                // Cleanup will be retried by the next queue operation.
            }
        }
    }
}
