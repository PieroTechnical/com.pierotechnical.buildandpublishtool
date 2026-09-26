using System;
using System.IO;
using System.Text;
using UnityEditor;

namespace Pierotechnical.BuildAndUploadTool.Editor
{
    internal enum CatalogLookupResults
    {
        Keep,
        Apply,
        Clear
    }

    internal struct CatalogLookupDecision
    {
        public bool Remember;
        public CatalogLookupResults Results;
        public string Message;
    }

    internal sealed class CatalogLookup<TResult>
    {
        public bool InProgress { get; private set; }
        public string Message { get; private set; }

        bool scheduled;
        string fetchedKey;
        string displayedKey;
        EditorWindow owner;
        Func<string> currentKey;
        Func<string> blockedMessage;
        string progressMessage;
        Action<string, Action<TResult>> start;
        Func<TResult, CatalogLookupDecision> interpret;
        Action<TResult> apply;
        Action clear;

        public void Request(
            EditorWindow owner,
            Func<string> currentKey,
            Func<string> blockedMessage,
            string progressMessage,
            Action<string, Action<TResult>> start,
            Func<TResult, CatalogLookupDecision> interpret,
            Action<TResult> apply,
            Action clear)
        {
            this.owner = owner;
            this.currentKey = currentKey;
            this.blockedMessage = blockedMessage;
            this.progressMessage = progressMessage;
            this.start = start;
            this.interpret = interpret;
            this.apply = apply;
            this.clear = clear;
            RefreshDisplayedKey();
            TryStart();
        }

        public void Invalidate()
        {
            fetchedKey = null;
        }

        void TryStart()
        {
            if (scheduled || InProgress || currentKey == null)
                return;

            string key = currentKey();
            if (string.IsNullOrEmpty(key))
            {
                fetchedKey = null;
                displayedKey = null;
                Message = null;
                if (clear != null)
                    clear();
                return;
            }

            if (key == fetchedKey)
                return;

            string blocked = blockedMessage == null ? null : blockedMessage();
            if (!string.IsNullOrEmpty(blocked))
            {
                Message = blocked;
                return;
            }

            scheduled = true;
            string captured = key;
            EditorApplication.delayCall += () => Run(captured);
        }

        void Run(string captured)
        {
            scheduled = false;
            if (owner == null || start == null)
                return;

            string key = currentKey == null ? null : currentKey();
            if (key != captured)
            {
                TryStart();
                return;
            }

            InProgress = true;
            Message = progressMessage;
            owner.Repaint();
            start(captured, result => Complete(captured, result));
        }

        void Complete(string captured, TResult result)
        {
            InProgress = false;
            if (owner == null)
                return;

            string key = currentKey == null ? null : currentKey();
            if (key != captured)
            {
                TryStart();
                owner.Repaint();
                return;
            }

            CatalogLookupDecision decision = interpret == null
                ? new CatalogLookupDecision()
                : interpret(result);

            if (decision.Remember)
                fetchedKey = captured;

            if (decision.Results == CatalogLookupResults.Apply)
            {
                if (apply != null)
                    apply(result);
            }
            else if (decision.Results == CatalogLookupResults.Clear)
            {
                if (clear != null)
                    clear();
            }

            Message = decision.Message;
            owner.Repaint();
        }

        void RefreshDisplayedKey()
        {
            if (currentKey == null)
                return;

            string key = currentKey();
            if (key == displayedKey)
                return;

            displayedKey = key;
            Message = null;
            if (clear != null)
                clear();
        }
    }

    internal static class CatalogLookupKey
    {
        public static string Itch(string executablePath, string owner, string project)
        {
            string ownerSlug = BuildPaths.ToItchSlug(owner);
            string projectSlug = BuildPaths.ToItchSlug(project);
            if (string.IsNullOrEmpty(ownerSlug) || string.IsNullOrEmpty(projectSlug))
                return null;

            return Compose(NormalizePath(executablePath), ownerSlug, projectSlug);
        }

        public static string Steam(string executablePath, string username, string appId)
        {
            string canonicalAppId;
            if (!SteamCommand.TryParseSteamId(appId, out canonicalAppId))
                return null;

            string normalizedUser = string.IsNullOrWhiteSpace(username)
                ? string.Empty
                : username.Trim().ToLowerInvariant();
            return Compose(NormalizePath(executablePath), normalizedUser, canonicalAppId);
        }

        static string Compose(params string[] parts)
        {
            var builder = new StringBuilder();
            for (int i = 0; i < parts.Length; i++)
            {
                string value = parts[i] ?? string.Empty;
                builder.Append(value.Length).Append(':').Append(value);
            }

            return builder.ToString();
        }

        static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            try
            {
                string fullPath = Path.GetFullPath(path.Trim());
                return Path.DirectorySeparatorChar == '\\'
                    ? fullPath.ToUpperInvariant()
                    : fullPath;
            }
            catch (Exception)
            {
                return path.Trim();
            }
        }
    }
}
