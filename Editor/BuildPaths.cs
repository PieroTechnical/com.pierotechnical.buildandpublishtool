using System;
using System.IO;
using UnityEditor;

namespace Pierotechnical.BuildAndUploadTool.Editor
{
    internal static class BuildPaths
    {
        public const string TempFolderName = ".in-progress";
        public const string SteamCacheFolderName = ".steam-cache";

        static readonly string[] ReservedWindowsNames =
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        };

        public static string SanitizePathSegment(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "game";

            string trimmed = name.Trim();
            var buffer = new char[trimmed.Length];
            for (int i = 0; i < trimmed.Length; i++)
            {
                char character = trimmed[i];
                bool invalidChar = character < 32
                    || character == '<'
                    || character == '>'
                    || character == ':'
                    || character == '"'
                    || character == '/'
                    || character == '\\'
                    || character == '|'
                    || character == '?'
                    || character == '*';
                buffer[i] = invalidChar ? '_' : character;
            }

            string sanitized = new string(buffer).Trim(' ', '.');
            if (sanitized.Length == 0 || sanitized == "." || sanitized == "..")
                return "game";
            if (sanitized.IndexOf('/') >= 0 || sanitized.IndexOf('\\') >= 0)
                return "game";
            if (IsReservedWindowsName(sanitized))
                sanitized += "_";

            return sanitized;
        }

        public static string ToPortableKey(string name)
        {
            string value = SanitizePathSegment(name).ToLowerInvariant();
            var builder = new System.Text.StringBuilder(value.Length);
            bool separator = false;
            for (int i = 0; i < value.Length; i++)
            {
                char character = value[i];
                bool allowed = (character >= 'a' && character <= 'z')
                    || (character >= '0' && character <= '9');
                if (allowed)
                {
                    builder.Append(character);
                    separator = false;
                }
                else if (!separator && builder.Length > 0)
                {
                    builder.Append('-');
                    separator = true;
                }
            }

            string key = builder.ToString().Trim('-');
            return string.IsNullOrEmpty(key) ? "target" : key;
        }

        public static string VersionedFolder(string buildsRoot, string gameName, string version, string platformId)
        {
            return Path.Combine(
                buildsRoot,
                SanitizePathSegment(gameName),
                SanitizePathSegment(version),
                SanitizePathSegment(platformId));
        }

        public static string TempFolder(string buildsRoot, string gameName, string version, string platformId)
        {
            return TempFolder(buildsRoot, gameName, version, platformId, "legacy");
        }

        internal static string TempFolder(
            string buildsRoot,
            string gameName,
            string version,
            string platformId,
            string queueId)
        {
            return Path.Combine(
                buildsRoot,
                TempFolderName,
                SanitizePathSegment(queueId),
                SanitizePathSegment(gameName),
                SanitizePathSegment(version),
                SanitizePathSegment(platformId));
        }

        public static string StagingFolder(string buildsRoot, string gameName, string version, string platformId)
        {
            return Path.Combine(
                buildsRoot,
                TempFolderName,
                SanitizePathSegment(gameName),
                SanitizePathSegment(version),
                SanitizePathSegment(platformId) + "-stage");
        }

        public static string SteamWorkFolder(string buildsRoot, string queueId)
        {
            return Path.Combine(
                buildsRoot,
                TempFolderName,
                "steam",
                SanitizePathSegment(queueId));
        }

        public static string SteamCacheFolder(string buildsRoot, string appId)
        {
            return Path.Combine(
                buildsRoot ?? string.Empty,
                SteamCacheFolderName,
                SanitizePathSegment(appId));
        }

        public static string PlayerLocation(string directory, string platformId, string gameName)
        {
            switch (platformId)
            {
                case "windows":
                    return Path.Combine(directory, SanitizePathSegment(gameName) + ".exe");
                case "mac":
                    return Path.Combine(directory, "MacBuild.app");
                case "linux":
                    return Path.Combine(directory, "LinuxBuild.x86_64");
                case "webgl":
                    return Path.Combine(directory, "WebGLBuild");
                default:
                    return Path.Combine(directory, SanitizePathSegment(gameName));
            }
        }

        public static string PlayerLocation(string directory, BuildTarget target, string gameName)
        {
            return PlayerLocation(directory, target, gameName, false);
        }

        public static string PlayerLocation(string directory, BuildTarget target, string gameName, bool androidAppBundle)
        {
            BuildOutputKind outputKind = BuildTargetCatalog.Find(target).OutputKind;
            if (outputKind == BuildOutputKind.WindowsExecutable)
                return Path.Combine(directory, SanitizePathSegment(gameName) + ".exe");
            if (outputKind == BuildOutputKind.MacApplication)
                return Path.Combine(directory, "MacBuild.app");
            if (outputKind == BuildOutputKind.LinuxExecutable)
                return Path.Combine(directory, "LinuxBuild.x86_64");
            if (outputKind == BuildOutputKind.WebDirectory)
                return Path.Combine(directory, "WebGLBuild");
            if (outputKind == BuildOutputKind.AndroidPackage)
            {
                string extension = androidAppBundle ? ".aab" : ".apk";
                return Path.Combine(directory, SanitizePathSegment(gameName) + extension);
            }

            return Path.Combine(directory, SanitizePathSegment(gameName));
        }

        public static string ToItchSlug(string input)
        {
            string slug;
            return TryNormalizeItchSlug(input, out slug) ? slug : string.Empty;
        }

        public static bool TryNormalizeItchSlug(string input, out string slug)
        {
            slug = string.Empty;
            if (string.IsNullOrWhiteSpace(input))
                return false;

            string value = input.Trim().ToLowerInvariant();
            var builder = new System.Text.StringBuilder(value.Length);
            bool pendingHyphen = false;
            for (int i = 0; i < value.Length; i++)
            {
                char character = value[i];
                bool letterOrDigit = (character >= 'a' && character <= 'z')
                    || (character >= '0' && character <= '9');
                if (letterOrDigit)
                {
                    if (pendingHyphen && builder.Length > 0)
                        builder.Append('-');
                    builder.Append(character);
                    pendingHyphen = false;
                    continue;
                }

                if (character == '-' || char.IsWhiteSpace(character))
                {
                    pendingHyphen = true;
                    continue;
                }

                return false;
            }

            slug = builder.ToString();
            return slug.Length > 0;
        }

        public static string ItchPageUrl(string user, string game)
        {
            string slugUser = ToItchSlug(user);
            string slugGame = ToItchSlug(game);
            if (string.IsNullOrEmpty(slugUser) || string.IsNullOrEmpty(slugGame))
                return string.Empty;

            return "https://" + slugUser + ".itch.io/" + slugGame;
        }

        public static string GetRelativePath(string relativeToDirectory, string path)
        {
            string fromPath = AppendDirectorySeparator(Path.GetFullPath(relativeToDirectory));
            string toPath = Path.GetFullPath(path);
            var fromUri = new Uri(fromPath);
            var toUri = new Uri(toPath);
            string relative = Uri.UnescapeDataString(fromUri.MakeRelativeUri(toUri).ToString());
            relative = relative.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            if (relative.StartsWith("." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                relative = relative.Substring(2);

            return relative;
        }

        static string AppendDirectorySeparator(string path)
        {
            if (path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                || path.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal))
            {
                return path;
            }

            return path + Path.DirectorySeparatorChar;
        }

        static bool IsReservedWindowsName(string value)
        {
            string head = value;
            int dot = value.IndexOf('.');
            if (dot >= 0)
                head = value.Substring(0, dot);

            for (int i = 0; i < ReservedWindowsNames.Length; i++)
            {
                if (string.Equals(head, ReservedWindowsNames[i], StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
    }
}
