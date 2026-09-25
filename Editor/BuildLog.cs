using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Pierotechnical.BuildAndUploadTool.Editor
{
    public static class BuildLog
    {
        public const string FileName = "last-build.log";

        public static string LogPath(string buildsRoot)
        {
            return Path.Combine(buildsRoot ?? string.Empty, FileName);
        }

        public static void Reset(string buildsRoot, string notice)
        {
            var builder = new StringBuilder();
            builder.Append("Build started ");
            builder.AppendLine(DateTime.Now.ToString("u", CultureInfo.InvariantCulture));
            if (!string.IsNullOrEmpty(notice))
                builder.AppendLine(notice);

            Write(buildsRoot, builder.ToString(), append: false);
        }

        public static void Append(string buildsRoot, string message)
        {
            if (message == null)
                message = string.Empty;

            Write(buildsRoot, message + Environment.NewLine, append: true);
        }

        static void Write(string buildsRoot, string contents, bool append)
        {
            if (string.IsNullOrEmpty(buildsRoot))
                return;

            try
            {
                Directory.CreateDirectory(buildsRoot);
                string path = LogPath(buildsRoot);
                if (append)
                    File.AppendAllText(path, contents);
                else
                    File.WriteAllText(path, contents);
            }
            catch (Exception exception)
            {
                Debug.LogError("Could not write build log: " + exception.Message);
            }
        }
    }
}
