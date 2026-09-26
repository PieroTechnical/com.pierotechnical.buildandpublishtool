using System;

namespace Pierotechnical.BuildAndUploadTool.Editor
{
    internal static class PublishExclusions
    {
        static readonly string[] FolderSuffixes =
        {
            "_DoNotShip",
            "BackUpThisFolder_ButDontShipItWithYourGame",
            "BurstDebugInformation_DoNotShip"
        };

        public static bool IsExcluded(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath))
                return false;

            string[] parts = relativePath.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                for (int suffixIndex = 0; suffixIndex < FolderSuffixes.Length; suffixIndex++)
                {
                    if (parts[i].EndsWith(FolderSuffixes[suffixIndex], StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }

            return false;
        }

        public static string[] ExclusionPatterns()
        {
            var patterns = new string[FolderSuffixes.Length];
            for (int i = 0; i < FolderSuffixes.Length; i++)
                patterns[i] = "*" + FolderSuffixes[i] + "*";

            return patterns;
        }
    }
}
