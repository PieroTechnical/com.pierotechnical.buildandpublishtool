using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Pierotechnical.BuildAndUploadTool.Editor
{
    internal static class ButlerCommand
    {
        public const string LoginRequiredMessage = "Butler is not logged in. Run butler login, then try again.";
        public const string NoChannelsMessage = "No itch channels were found for this page.";

        public static bool ShouldUpload(bool uploadRequested, bool buildSucceeded)
        {
            return uploadRequested && buildSucceeded;
        }

        public static string Quote(string value)
        {
            if (value == null)
                value = string.Empty;

            var builder = new StringBuilder(value.Length + 4);
            builder.Append('"');
            int backslashes = 0;
            for (int i = 0; i < value.Length; i++)
            {
                char character = value[i];
                if (character == '\\')
                {
                    backslashes++;
                    continue;
                }

                if (character == '"')
                {
                    builder.Append('\\', backslashes * 2 + 1);
                    builder.Append('"');
                    backslashes = 0;
                    continue;
                }

                if (backslashes > 0)
                {
                    builder.Append('\\', backslashes);
                    backslashes = 0;
                }
                builder.Append(character);
            }

            if (backslashes > 0)
                builder.Append('\\', backslashes * 2);
            builder.Append('"');
            return builder.ToString();
        }

        public static string PushArguments(string folder, string itchUser, string itchGame, string channel, string version)
        {
            string target = ItchTarget(itchUser, itchGame) + ":" + (channel ?? string.Empty);
            var arguments = new StringBuilder();
            arguments.Append("push ")
                .Append(Quote(folder))
                .Append(' ')
                .Append(Quote(target))
                .Append(" --userversion ")
                .Append(Quote(version));
            string[] exclusions = PublishExclusions.ExclusionPatterns();
            for (int i = 0; i < exclusions.Length; i++)
                arguments.Append(" --ignore ").Append(Quote(exclusions[i]));
            return arguments.ToString();
        }

        public static string StatusArguments(string itchUser, string itchGame)
        {
            return "status " + Quote(ItchTarget(itchUser, itchGame));
        }

        public static string StatusJsonArguments(string itchUser, string itchGame)
        {
            return StatusArguments(itchUser, itchGame) + " --json";
        }

        public static string LoginArguments()
        {
            return "login";
        }

        public static string ItchTarget(string itchUser, string itchGame)
        {
            return BuildPaths.ToItchSlug(itchUser) + "/" + BuildPaths.ToItchSlug(itchGame);
        }

        public static bool IsValidChannel(string channel)
        {
            if (string.IsNullOrWhiteSpace(channel))
                return false;

            string value = channel.Trim();
            for (int i = 0; i < value.Length; i++)
            {
                char character = value[i];
                if (char.IsControl(character)
                    || char.IsWhiteSpace(character)
                    || character == ':'
                    || character == '/'
                    || character == '\\'
                    || character == '"')
                {
                    return false;
                }
            }

            return true;
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

        public static List<string> ParseStatusChannels(string output)
        {
            var names = new List<string>();
            if (string.IsNullOrEmpty(output))
                return names;

            int search = 0;
            while (search < output.Length)
            {
                int channelsAt = output.IndexOf("\"channels\"", search, StringComparison.Ordinal);
                if (channelsAt < 0)
                    break;

                int arrayAt = output.IndexOf('[', channelsAt);
                if (arrayAt < 0)
                    break;

                int arrayEnd = FindMatchingBracket(output, arrayAt);
                if (arrayEnd < 0)
                {
                    search = arrayAt + 1;
                    continue;
                }

                CollectChannelNames(output, arrayAt, arrayEnd, names);
                search = arrayEnd + 1;
            }

            return names;
        }

        public static string ChooseChannel(string currentValue, IList<string> channels)
        {
            if (!string.IsNullOrWhiteSpace(currentValue))
                return currentValue.Trim();

            if (channels == null || channels.Count != 1 || string.IsNullOrEmpty(channels[0]))
                return string.Empty;

            return channels[0];
        }

        static void CollectChannelNames(string text, int start, int end, List<string> names)
        {
            int i = start;
            while (i < end)
            {
                int key = text.IndexOf("\"name\"", i, StringComparison.Ordinal);
                if (key < 0 || key >= end)
                    break;

                int cursor = key + 6;
                while (cursor < end && char.IsWhiteSpace(text[cursor]))
                    cursor++;
                if (cursor >= end || text[cursor] != ':')
                {
                    i = key + 6;
                    continue;
                }

                cursor++;
                while (cursor < end && char.IsWhiteSpace(text[cursor]))
                    cursor++;
                string value;
                int next;
                if (cursor >= end || text[cursor] != '"' || !TryReadJsonString(text, cursor, out value, out next) || next > end + 1)
                {
                    i = Math.Min(cursor + 1, end);
                    continue;
                }

                if (!string.IsNullOrEmpty(value) && !ContainsChannel(names, value))
                    names.Add(value);
                i = next;
            }
        }

        static bool ContainsChannel(List<string> names, string value)
        {
            for (int i = 0; i < names.Count; i++)
            {
                if (string.Equals(names[i], value, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        static int FindMatchingBracket(string text, int openIndex)
        {
            int depth = 0;
            bool inString = false;
            bool escape = false;
            for (int i = openIndex; i < text.Length; i++)
            {
                char c = text[i];
                if (inString)
                {
                    if (escape)
                        escape = false;
                    else if (c == '\\')
                        escape = true;
                    else if (c == '"')
                        inString = false;
                    continue;
                }

                if (c == '"')
                {
                    inString = true;
                    continue;
                }

                if (c == '[')
                    depth++;
                else if (c == ']')
                {
                    depth--;
                    if (depth == 0)
                        return i;
                }
            }

            return -1;
        }

        static bool TryReadJsonString(string text, int quoteIndex, out string value, out int next)
        {
            value = null;
            next = quoteIndex;
            if (quoteIndex < 0 || quoteIndex >= text.Length || text[quoteIndex] != '"')
                return false;

            var builder = new StringBuilder();
            bool escape = false;
            for (int i = quoteIndex + 1; i < text.Length; i++)
            {
                char c = text[i];
                if (escape)
                {
                    escape = false;
                    if (c == 'u' && i + 4 < text.Length)
                    {
                        int code;
                        if (int.TryParse(text.Substring(i + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
                        {
                            builder.Append((char)code);
                            i += 4;
                            continue;
                        }
                    }

                    if (c == 'n')
                        builder.Append('\n');
                    else
                        builder.Append(c);
                    continue;
                }

                if (c == '\\')
                {
                    escape = true;
                    continue;
                }

                if (c == '"')
                {
                    value = builder.ToString();
                    next = i + 1;
                    return true;
                }

                builder.Append(c);
            }

            return false;
        }
    }
}
