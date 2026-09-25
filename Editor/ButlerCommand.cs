using System;
using System.Text;

namespace Pierotechnical.BuildAndUploadTool.Editor
{
    public static class ButlerCommand
    {
        public const string LoginRequiredMessage = "Butler is not logged in. Run butler login, then try again.";

        public static bool ShouldUpload(bool uploadRequested, bool buildSucceeded)
        {
            return uploadRequested && buildSucceeded;
        }

        public static string Quote(string value)
        {
            if (value == null)
                value = string.Empty;

            var builder = new StringBuilder(value.Length + 2);
            builder.Append('"');
            for (int i = 0; i < value.Length; i++)
            {
                if (value[i] == '"')
                    builder.Append('\\');
                builder.Append(value[i]);
            }

            if (value.EndsWith("\\", System.StringComparison.Ordinal))
                builder.Append('\\');

            builder.Append('"');
            return builder.ToString();
        }

        public static string PushArguments(string folder, string itchUser, string itchGame, string channel, string version)
        {
            string target = ItchTarget(itchUser, itchGame) + ":" + (channel ?? string.Empty);
            return "push " + Quote(folder) + " " + Quote(target) + " --userversion " + Quote(version);
        }

        public static string StatusArguments(string itchUser, string itchGame)
        {
            return "status " + Quote(ItchTarget(itchUser, itchGame));
        }

        public static string ItchTarget(string itchUser, string itchGame)
        {
            return BuildPaths.ToItchSlug(itchUser) + "/" + BuildPaths.ToItchSlug(itchGame);
        }

        public static bool IsLoginFailure(string output)
        {
            if (string.IsNullOrEmpty(output))
                return false;
            if (output.IndexOf("required argument 'target'", StringComparison.OrdinalIgnoreCase) >= 0
                || output.IndexOf("required argument \"target\"", StringComparison.OrdinalIgnoreCase) >= 0)
                return false;

            string lower = output.ToLowerInvariant();
            if (lower.Contains("not logged in"))
                return true;
            if (lower.Contains("no saved credentials"))
                return true;
            if (lower.Contains("butler login"))
                return true;
            if (lower.Contains("please log in") || lower.Contains("please login"))
                return true;
            if (lower.Contains("api key not found") || lower.Contains("no api key") || lower.Contains("missing api key"))
                return true;
            if (lower.Contains("unauthenticated"))
                return true;

            return false;
        }

        public static string DescribeStatusFailure(string output)
        {
            string body = string.IsNullOrWhiteSpace(output) ? "Butler exited with an error." : output.Trim();
            if (!IsLoginFailure(body))
                return body;

            return LoginRequiredMessage + Environment.NewLine + body;
        }
    }
}
