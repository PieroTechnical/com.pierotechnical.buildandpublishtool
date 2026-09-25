using System;

namespace Pierotechnical.BuildAndUploadTool.Editor
{
    public static class GameVersion
    {
        public static bool TryParse(string text, out int major, out int minor, out int patch)
        {
            major = 0;
            minor = 0;
            patch = 0;

            if (string.IsNullOrWhiteSpace(text))
                return false;

            string[] parts = text.Trim().Split('.');
            if (parts.Length != 3)
                return false;

            if (!TryParsePart(parts[0], out major))
                return false;
            if (!TryParsePart(parts[1], out minor))
                return false;
            if (!TryParsePart(parts[2], out patch))
                return false;

            return true;
        }

        public static bool TryIncrementMinor(string version, out string result)
        {
            result = null;
            int major;
            int minor;
            if (!TryParse(version, out major, out minor, out _))
                return false;
            if (minor == int.MaxValue)
                return false;

            result = major + "." + (minor + 1) + ".0";
            return true;
        }

        public static bool TryIncrementPatch(string version, out string result)
        {
            result = null;
            int major;
            int minor;
            int patch;
            if (!TryParse(version, out major, out minor, out patch))
                return false;
            if (patch == int.MaxValue)
                return false;

            result = major + "." + minor + "." + (patch + 1);
            return true;
        }

        static bool TryParsePart(string part, out int value)
        {
            value = 0;
            if (string.IsNullOrEmpty(part))
                return false;

            for (int i = 0; i < part.Length; i++)
            {
                if (part[i] < '0' || part[i] > '9')
                    return false;
            }

            return int.TryParse(part, out value);
        }
    }
}
