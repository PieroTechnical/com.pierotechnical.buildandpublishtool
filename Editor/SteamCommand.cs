using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Pierotechnical.BuildAndUploadTool.Editor
{
    public sealed class SteamDepotUpload
    {
        public string PlatformId;
        public string Label;
        public string DepotId;
        public string ContentRoot;
        public string ScriptName;
    }

    public static class SteamCommand
    {
        public const string LoginRequiredMessage = "steamcmd is not logged in. Use Login with steamcmd, then try again.";
        public const string MissingCacheMessage = "steamcmd has no saved login for this username. Use Login with steamcmd, finish the password and Steam Guard prompt, then try again.";
        public const string SetLiveWithheldMessage = "Steam branch was not set live because a platform selected for Steam did not build. The upload was not set live.";
        public const string DefaultBranchMessage = "Steam cannot set the default branch live from steamcmd.";
        public const string InterruptedMessage = "The Editor reloaded during the Steam upload. Check the Steamworks backend before uploading again.";
        public const string NoDepotsMessage = "Steam did not list any depots for this app. Publish the depot setup on Steamworks, then try again.";
        public const string AppIdFileName = "steam_appid.txt";

        public static bool TryParseSteamId(string text, out string id)
        {
            id = null;
            if (string.IsNullOrWhiteSpace(text))
                return false;

            ulong value;
            if (!ulong.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out value))
                return false;
            if (value < 1 || value > uint.MaxValue)
                return false;

            id = value.ToString(CultureInfo.InvariantCulture);
            return true;
        }

        public static bool IsLoginFailure(string output)
        {
            if (string.IsNullOrEmpty(output))
                return false;

            string lower = output.ToLowerInvariant();
            if (lower.Contains("password"))
                return true;
            if (lower.Contains("steam guard"))
                return true;
            if (lower.Contains("guard code"))
                return true;
            if (lower.Contains("two-factor") || lower.Contains("two factor"))
                return true;
            if (lower.Contains("invalid password"))
                return true;
            if (lower.Contains("login failure"))
                return true;
            if (lower.Contains("failed to log in") || lower.Contains("failed to login"))
                return true;
            if (lower.Contains("not logged in"))
                return true;
            if (lower.Contains("cached credentials not found"))
                return true;

            return false;
        }

        public static bool HasCachedAccount(string configText, string username)
        {
            if (string.IsNullOrWhiteSpace(configText) || string.IsNullOrWhiteSpace(username))
                return false;

            List<VdfPair> root = ParseVdf(configText);
            if (!HasAccount(FindChildren(root, "Accounts"), username.Trim()))
                return false;

            return HasToken(FindChildren(root, "ConnectCache"));
        }

        static bool HasAccount(List<VdfPair> accounts, string username)
        {
            if (accounts == null)
                return false;

            for (int i = 0; i < accounts.Count; i++)
            {
                if (string.Equals(accounts[i].Key, username, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        static bool HasToken(List<VdfPair> cache)
        {
            if (cache == null)
                return false;

            for (int i = 0; i < cache.Count; i++)
            {
                if (!string.IsNullOrEmpty(cache[i].Text))
                    return true;
            }

            return false;
        }

        public static string DescribeLoginFailure(string output)
        {
            string body = string.IsNullOrWhiteSpace(output) ? "steamcmd exited with an error." : output.Trim();
            if (!IsLoginFailure(body))
                return body;

            return LoginRequiredMessage + Environment.NewLine + body;
        }

        public static string LoginCheckArguments(string username)
        {
            return "+login " + ButlerCommand.Quote(username) + " +quit";
        }

        public static string InteractiveLoginArguments(string username)
        {
            return "+login " + ButlerCommand.Quote(username);
        }

        public static string UploadArguments(string username, string appScriptPath)
        {
            return "+login " + ButlerCommand.Quote(username) + " +run_app_build " + ButlerCommand.Quote(appScriptPath) + " +quit";
        }

        public static string AppInfoArguments(string username, string appId)
        {
            return "+login " + ButlerCommand.Quote(username) + " +app_info_print " + (appId ?? string.Empty) + " +quit";
        }

        public static bool IsDefaultBranch(string branch)
        {
            if (string.IsNullOrWhiteSpace(branch))
                return false;

            return string.Equals(branch.Trim(), "default", StringComparison.OrdinalIgnoreCase);
        }

        public static string BuildsPageUrl(string appId)
        {
            return "https://partner.steamgames.com/apps/builds/" + (appId ?? string.Empty);
        }

        public static string ResolveSetLive(string branch, bool everySteamTargetSucceeded)
        {
            if (string.IsNullOrWhiteSpace(branch) || IsDefaultBranch(branch) || !everySteamTargetSucceeded)
                return string.Empty;

            return branch.Trim();
        }

        public static string DescribeUnsetLive(string branch, bool everySteamTargetSucceeded, string appId)
        {
            if (IsDefaultBranch(branch))
                return DefaultBranchMessage + " Set the build live at " + BuildsPageUrl(appId) + ".";

            if (!string.IsNullOrWhiteSpace(branch) && !everySteamTargetSucceeded)
                return SetLiveWithheldMessage;

            return string.Empty;
        }

        public static bool TryParseBuildId(string output, out string buildId)
        {
            buildId = null;
            if (string.IsNullOrEmpty(output))
                return false;

            int search = 0;
            while (search < output.Length)
            {
                int found = IndexOfOrdinalIgnoreCase(output, "buildid", search);
                if (found < 0)
                    return false;

                int cursor = found + 7;
                while (cursor < output.Length && !char.IsDigit(output[cursor]))
                {
                    if (char.IsLetter(output[cursor]))
                        break;
                    cursor++;
                }

                if (cursor < output.Length && char.IsDigit(output[cursor]))
                {
                    int start = cursor;
                    while (cursor < output.Length && char.IsDigit(output[cursor]))
                        cursor++;

                    string candidate = output.Substring(start, cursor - start);
                    if (TryParseSteamId(candidate, out buildId))
                        return true;
                }

                search = found + 7;
            }

            return false;
        }

        public static bool EverySteamTargetSucceeded(IList<BuildRequest> pending, IList<TargetResult> completed)
        {
            if (pending == null)
                return true;

            for (int i = 0; i < pending.Count; i++)
            {
                BuildRequest request = pending[i];
                if (request == null || !request.PublishSteam)
                    continue;
                if (!PlatformCatalog.SupportsSteamPlatform(request.PlatformId))
                    continue;
                if (completed == null || i >= completed.Count || completed[i] == null || !completed[i].BuildSucceeded)
                    return false;
            }

            return true;
        }

        public static bool HasPublishableBuild(IList<BuildRequest> pending, IList<TargetResult> completed)
        {
            return SelectDepots(pending, completed).Count > 0;
        }

        public static List<SteamDepotUpload> SelectDepots(IList<BuildRequest> pending, IList<TargetResult> completed)
        {
            var depots = new List<SteamDepotUpload>();
            if (pending == null)
                return depots;

            for (int i = 0; i < pending.Count; i++)
            {
                BuildRequest request = pending[i];
                if (request == null || !request.PublishSteam)
                    continue;
                if (!ButlerCommand.ShouldUpload(true, completed != null && i < completed.Count && completed[i] != null && completed[i].BuildSucceeded))
                    continue;
                if (!PlatformCatalog.SupportsSteamPlatform(request.PlatformId))
                    continue;

                string depotId;
                SteamCommand.TryParseSteamId(request.SteamDepotId, out depotId);
                depots.Add(new SteamDepotUpload
                {
                    PlatformId = request.PlatformId,
                    Label = string.IsNullOrEmpty(request.Label) ? request.PlatformId : request.Label,
                    DepotId = depotId,
                    ContentRoot = BuildPaths.VersionedFolder(request.OutputRoot, request.ItchGame, request.Version, request.PlatformId),
                    ScriptName = DepotScriptName(request.PlatformId)
                });
            }

            return depots;
        }

        public static string DepotScriptName(string platformId)
        {
            string safe = string.IsNullOrEmpty(platformId) ? "depot" : platformId;
            return "depot_" + safe + ".vdf";
        }

        public static bool TryFindDuplicateDepotId(IList<SteamDepotUpload> depots, out string depotId)
        {
            depotId = null;
            if (depots == null)
                return false;

            for (int i = 0; i < depots.Count; i++)
            {
                if (depots[i] == null || string.IsNullOrEmpty(depots[i].DepotId))
                    continue;

                for (int j = i + 1; j < depots.Count; j++)
                {
                    if (depots[j] == null)
                        continue;
                    if (depots[i].DepotId == depots[j].DepotId)
                    {
                        depotId = depots[i].DepotId;
                        return true;
                    }
                }
            }

            return false;
        }

        public static string DescribeBuild(string version, IList<SteamDepotUpload> depots)
        {
            var builder = new StringBuilder();
            builder.Append(string.IsNullOrEmpty(version) ? "build" : version.Trim());
            builder.Append(" (");
            bool wrote = false;
            if (depots != null)
            {
                for (int i = 0; i < depots.Count; i++)
                {
                    if (depots[i] == null)
                        continue;
                    if (wrote)
                        builder.Append(", ");
                    builder.Append(string.IsNullOrEmpty(depots[i].Label) ? depots[i].PlatformId : depots[i].Label);
                    wrote = true;
                }
            }

            if (!wrote)
                builder.Append("none");

            builder.Append(')');
            return builder.ToString();
        }

        public static string BuildAppScript(string appId, string description, string buildOutput, string setlive, IList<SteamDepotUpload> depots)
        {
            var builder = new StringBuilder();
            builder.AppendLine("\"appbuild\"");
            builder.AppendLine("{");
            builder.Append('\t').Append("\"appid\" \"").Append(Escape(appId)).AppendLine("\"");
            builder.Append('\t').Append("\"desc\" \"").Append(Escape(description)).AppendLine("\"");
            builder.Append('\t').Append("\"buildoutput\" \"").Append(ToVdfPath(buildOutput)).AppendLine("\"");
            builder.Append('\t').AppendLine("\"contentroot\" \"\"");
            builder.Append('\t').Append("\"setlive\" \"").Append(Escape(setlive)).AppendLine("\"");
            builder.Append('\t').AppendLine("\"preview\" \"0\"");
            builder.Append('\t').AppendLine("\"local\" \"\"");
            builder.Append('\t').AppendLine("\"depots\"");
            builder.Append('\t').AppendLine("{");
            if (depots != null)
            {
                for (int i = 0; i < depots.Count; i++)
                {
                    if (depots[i] == null)
                        continue;
                    builder.Append("\t\t\"")
                        .Append(Escape(depots[i].DepotId))
                        .Append("\" \"")
                        .Append(Escape(depots[i].ScriptName))
                        .AppendLine("\"");
                }
            }

            builder.Append('\t').AppendLine("}");
            builder.Append('}');
            builder.AppendLine();
            return builder.ToString();
        }

        public static string BuildDepotScript(SteamDepotUpload depot)
        {
            var builder = new StringBuilder();
            builder.AppendLine("\"DepotBuildConfig\"");
            builder.AppendLine("{");
            builder.Append('\t').Append("\"DepotID\" \"").Append(Escape(depot == null ? string.Empty : depot.DepotId)).AppendLine("\"");
            builder.Append('\t').Append("\"ContentRoot\" \"").Append(ToVdfPath(depot == null ? string.Empty : depot.ContentRoot)).AppendLine("\"");
            builder.Append('\t').AppendLine("\"FileMapping\"");
            builder.Append('\t').AppendLine("{");
            builder.Append("\t\t").AppendLine("\"LocalPath\" \"*\"");
            builder.Append("\t\t").AppendLine("\"DepotPath\" \".\"");
            builder.Append("\t\t").AppendLine("\"recursive\" \"1\"");
            builder.Append('\t').AppendLine("}");

            string[] patterns = PublishExclusions.ExclusionPatterns();
            for (int i = 0; i < patterns.Length; i++)
                builder.Append('\t').Append("\"FileExclusion\" \"").Append(Escape(patterns[i])).AppendLine("\"");

            builder.Append('\t').Append("\"FileExclusion\" \"").Append(Escape(AppIdFileName)).AppendLine("\"");
            builder.Append('}');
            builder.AppendLine();
            return builder.ToString();
        }

        public static string ToVdfPath(string path)
        {
            string value = path ?? string.Empty;
            return Escape(value.Replace('\\', '/'));
        }

        public static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        public static SteamDepotLookup ParseAppDepots(string appInfo)
        {
            var lookup = new SteamDepotLookup();
            lookup.Depots = new List<SteamCatalogDepot>();
            lookup.Branches = new List<string>();
            List<VdfPair> depots = FindDepotBlock(appInfo);
            if (depots == null || depots.Count == 0)
            {
                lookup.Message = NoDepotsMessage;
                return lookup;
            }

            for (int i = 0; i < depots.Count; i++)
            {
                if (string.Equals(depots[i].Key, "branches", StringComparison.OrdinalIgnoreCase))
                {
                    AddBranchNames(lookup.Branches, depots[i].Children);
                    continue;
                }

                string depotId;
                if (!TryParseSteamId(depots[i].Key, out depotId))
                    continue;
                if (depots[i].Children == null)
                    continue;

                string platform = SingleOs(FindValue(depots[i].Children, "oslist"));
                lookup.Depots.Add(new SteamCatalogDepot
                {
                    Id = depotId,
                    PlatformId = platform,
                    MenuLabel = DepotMenuLabel(depotId, platform)
                });
            }

            if (lookup.Depots.Count == 0)
            {
                lookup.Message = NoDepotsMessage;
                return lookup;
            }

            lookup.FoundDepots = true;
            return lookup;
        }

        public static string ChooseDepotId(string currentValue, string platformId, IList<SteamCatalogDepot> depots)
        {
            if (!string.IsNullOrWhiteSpace(currentValue))
                return currentValue.Trim();

            if (depots == null || string.IsNullOrEmpty(platformId))
                return string.Empty;

            string match = null;
            int count = 0;
            for (int i = 0; i < depots.Count; i++)
            {
                if (depots[i] == null || depots[i].PlatformId != platformId)
                    continue;

                count++;
                match = depots[i].Id;
            }

            if (count == 1 && !string.IsNullOrEmpty(match))
                return match;

            return string.Empty;
        }

        static void AddBranchNames(List<string> branches, List<VdfPair> branchBlock)
        {
            if (branches == null || branchBlock == null)
                return;

            for (int i = 0; i < branchBlock.Count; i++)
            {
                if (string.IsNullOrEmpty(branchBlock[i].Key))
                    continue;
                branches.Add(branchBlock[i].Key);
            }
        }

        static string DepotMenuLabel(string depotId, string platformId)
        {
            string os = "All OSes";
            if (platformId == "windows")
                os = "Windows";
            else if (platformId == "mac")
                os = "Mac";
            else if (platformId == "linux")
                os = "Linux";

            return depotId + " \u2014 " + os;
        }

        static string SingleOs(string osList)
        {
            if (string.IsNullOrWhiteSpace(osList))
                return null;

            string lower = osList.Trim().ToLowerInvariant();
            if (lower.IndexOf("all", StringComparison.Ordinal) >= 0)
                return null;
            if (lower.IndexOf(',') >= 0 || lower.IndexOf(' ') >= 0 || lower.IndexOf(';') >= 0)
                return null;
            if (lower == "windows")
                return "windows";
            if (lower == "macos")
                return "mac";
            if (lower == "linux")
                return "linux";

            return null;
        }

        static string FindValue(List<VdfPair> pairs, string key)
        {
            if (pairs == null)
                return null;

            for (int i = 0; i < pairs.Count; i++)
            {
                if (string.Equals(pairs[i].Key, key, StringComparison.OrdinalIgnoreCase))
                    return pairs[i].Text;

                string nested = FindValue(pairs[i].Children, key);
                if (nested != null)
                    return nested;
            }

            return null;
        }

        static List<VdfPair> FindDepotBlock(string appInfo)
        {
            List<VdfPair> root = ParseVdf(appInfo);
            return FindChildren(root, "depots");
        }

        static List<VdfPair> FindChildren(List<VdfPair> pairs, string key)
        {
            if (pairs == null)
                return null;

            for (int i = 0; i < pairs.Count; i++)
            {
                if (string.Equals(pairs[i].Key, key, StringComparison.OrdinalIgnoreCase) && pairs[i].Children != null)
                    return pairs[i].Children;

                List<VdfPair> nested = FindChildren(pairs[i].Children, key);
                if (nested != null)
                    return nested;
            }

            return null;
        }

        static List<VdfPair> ParseVdf(string text)
        {
            var tokens = new List<VdfToken>();
            Tokenize(text, tokens);
            int index = 0;
            return ParsePairs(tokens, ref index, false);
        }

        static List<VdfPair> ParsePairs(List<VdfToken> tokens, ref int index, bool stopAtClose)
        {
            var pairs = new List<VdfPair>();
            while (index < tokens.Count)
            {
                if (tokens[index].Kind == VdfTokenKind.Close)
                {
                    if (stopAtClose)
                        index++;
                    break;
                }

                if (tokens[index].Kind != VdfTokenKind.Text)
                {
                    index++;
                    continue;
                }

                string key = tokens[index].Text;
                index++;
                if (index >= tokens.Count)
                    break;

                var pair = new VdfPair();
                pair.Key = key;
                if (tokens[index].Kind == VdfTokenKind.Open)
                {
                    index++;
                    pair.Children = ParsePairs(tokens, ref index, true);
                }
                else if (tokens[index].Kind == VdfTokenKind.Text)
                {
                    pair.Text = tokens[index].Text;
                    index++;
                }

                pairs.Add(pair);
            }

            return pairs;
        }

        static void Tokenize(string text, List<VdfToken> tokens)
        {
            if (string.IsNullOrEmpty(text))
                return;

            int index = 0;
            while (index < text.Length)
            {
                char character = text[index];
                if (char.IsWhiteSpace(character))
                {
                    index++;
                    continue;
                }

                if (character == '{')
                {
                    tokens.Add(new VdfToken { Kind = VdfTokenKind.Open });
                    index++;
                    continue;
                }

                if (character == '}')
                {
                    tokens.Add(new VdfToken { Kind = VdfTokenKind.Close });
                    index++;
                    continue;
                }

                if (character == '"')
                {
                    tokens.Add(new VdfToken { Kind = VdfTokenKind.Text, Text = ReadQuoted(text, ref index) });
                    continue;
                }

                int start = index;
                while (index < text.Length && !char.IsWhiteSpace(text[index]) && text[index] != '{' && text[index] != '}' && text[index] != '"')
                    index++;

                tokens.Add(new VdfToken { Kind = VdfTokenKind.Text, Text = text.Substring(start, index - start) });
            }
        }

        static string ReadQuoted(string text, ref int index)
        {
            index++;
            var builder = new StringBuilder();
            while (index < text.Length)
            {
                char character = text[index];
                if (character == '\\' && index + 1 < text.Length)
                {
                    builder.Append(text[index + 1]);
                    index += 2;
                    continue;
                }

                if (character == '"')
                {
                    index++;
                    break;
                }

                builder.Append(character);
                index++;
            }

            return builder.ToString();
        }

        static int IndexOfOrdinalIgnoreCase(string text, string value, int start)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(value) || start < 0 || start > text.Length)
                return -1;

            return text.IndexOf(value, start, StringComparison.OrdinalIgnoreCase);
        }

        sealed class VdfPair
        {
            public string Key;
            public string Text;
            public List<VdfPair> Children;
        }

        struct VdfToken
        {
            public VdfTokenKind Kind;
            public string Text;
        }

        enum VdfTokenKind
        {
            Text,
            Open,
            Close
        }
    }

    public sealed class SteamCatalogDepot
    {
        public string Id;
        public string PlatformId;
        public string MenuLabel;
    }

    public sealed class SteamDepotLookup
    {
        public bool FoundDepots;
        public string Message;
        public List<SteamCatalogDepot> Depots = new List<SteamCatalogDepot>();
        public List<string> Branches = new List<string>();
    }
}
