using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Pierotechnical.BuildAndUploadTool.Editor
{
    internal static class SafeFileSystem
    {
        internal static bool TryGetContainedPath(
            string root,
            string candidate,
            out string fullPath,
            out string error)
        {
            fullPath = null;
            error = null;
            if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(candidate))
            {
                error = "A managed filesystem path was empty.";
                return false;
            }

            try
            {
                string fullRoot = TrimTrailingSeparators(Path.GetFullPath(root));
                string fullCandidate = Path.GetFullPath(candidate);
                StringComparison comparison = IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal;
                if (!string.Equals(fullRoot, fullCandidate, comparison)
                    && !fullCandidate.StartsWith(
                        fullRoot + Path.DirectorySeparatorChar,
                        comparison)
                    && (Path.AltDirectorySeparatorChar == Path.DirectorySeparatorChar
                        || !fullCandidate.StartsWith(
                            fullRoot + Path.AltDirectorySeparatorChar,
                            comparison)))
                {
                    error = "Path escaped the managed root: " + candidate;
                    return false;
                }

                fullPath = fullCandidate;
                return true;
            }
            catch (Exception exception)
            {
                error = "Could not canonicalize path '" + candidate + "': " + exception.Message;
                return false;
            }
        }

        internal static bool TryEnsureLinkFree(
            string root,
            string candidate,
            bool includeDescendants,
            out string error)
        {
            string fullPath;
            if (!TryGetContainedPath(root, candidate, out fullPath, out error))
                return false;

            string fullRoot = TrimTrailingSeparators(Path.GetFullPath(root));
            string relative = BuildPaths.GetRelativePath(fullRoot, fullPath);
            string current = fullRoot;
            if (Exists(current) && IsLink(current))
            {
                error = "Managed root is a symbolic link or junction: " + current;
                return false;
            }

            if (!string.IsNullOrEmpty(relative) && relative != ".")
            {
                string[] parts = relative.Split(
                    new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                    StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < parts.Length; i++)
                {
                    current = Path.Combine(current, parts[i]);
                    if (Exists(current) && IsLink(current))
                    {
                        error = "Symbolic links and junctions are not allowed in managed paths: " + current;
                        return false;
                    }
                }
            }

            if (!includeDescendants || !Directory.Exists(fullPath))
                return true;

            var pending = new Stack<string>();
            pending.Push(fullPath);
            while (pending.Count > 0)
            {
                string directory = pending.Pop();
                foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
                {
                    if (IsLink(entry))
                    {
                        error = "Symbolic links and junctions are not allowed while staging: " + entry;
                        return false;
                    }

                    if (Directory.Exists(entry))
                        pending.Push(entry);
                }
            }

            return true;
        }

        internal static bool TryDeleteDirectory(
            string root,
            string path,
            out string error)
        {
            error = null;
            string fullPath;
            if (!TryGetContainedPath(root, path, out fullPath, out error))
                return false;
            if (!Directory.Exists(fullPath))
                return true;
            if (!TryEnsureLinkFree(root, fullPath, false, out error))
                return false;

            try
            {
                DeleteTreeWithoutFollowingLinks(fullPath);
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        internal static bool TryCopyTree(
            string sourceRoot,
            string destinationRoot,
            Func<bool> isCancellationRequested,
            Action<long, long, string> reportProgress,
            out string error)
        {
            error = null;
            string source;
            if (!TryGetContainedPath(sourceRoot, sourceRoot, out source, out error))
                return false;
            if (!Directory.Exists(source))
            {
                error = "Source folder was not found: " + source;
                return false;
            }
            if (!TryEnsureLinkFree(source, source, true, out error))
                return false;

            string destination = Path.GetFullPath(destinationRoot);
            string containedDestination;
            string ignoredError;
            if (TryGetContainedPath(
                source,
                destination,
                out containedDestination,
                out ignoredError))
            {
                error = "The staging destination cannot be inside the source folder.";
                return false;
            }
            string destinationVolume = Path.GetPathRoot(destination);
            if (!TryEnsureLinkFree(
                destinationVolume,
                destination,
                false,
                out error))
            {
                return false;
            }
            if (Directory.Exists(destination))
            {
                error = "The staging destination already exists: " + destination;
                return false;
            }

            long totalBytes = 0;
            long totalFiles = 0;
            foreach (string file in Directory.EnumerateFiles(
                source,
                "*",
                SearchOption.AllDirectories))
            {
                string relative = BuildPaths.GetRelativePath(source, file);
                if (PublishExclusions.IsExcluded(relative))
                    continue;
                totalFiles++;
                totalBytes += new FileInfo(file).Length;
            }

            long copiedBytes = 0;
            long copiedFiles = 0;
            try
            {
                Directory.CreateDirectory(destination);
                foreach (string file in Directory.EnumerateFiles(
                    source,
                    "*",
                    SearchOption.AllDirectories))
                {
                    if (isCancellationRequested != null && isCancellationRequested())
                    {
                        error = "Staging cancelled.";
                        return false;
                    }

                    string relative = BuildPaths.GetRelativePath(source, file);
                    if (PublishExclusions.IsExcluded(relative))
                        continue;
                    string target;
                    if (!TryGetContainedPath(
                        destination,
                        Path.Combine(destination, relative),
                        out target,
                        out error))
                    {
                        return false;
                    }

                    string parent = Path.GetDirectoryName(target);
                    if (!string.IsNullOrEmpty(parent))
                        Directory.CreateDirectory(parent);
                    File.Copy(file, target, true);
                    File.SetAttributes(target, File.GetAttributes(file));
                    copiedFiles++;
                    copiedBytes += new FileInfo(file).Length;
                    if (reportProgress != null)
                    {
                        reportProgress(
                            copiedFiles,
                            totalFiles,
                            relative + " (" + copiedBytes + "/" + totalBytes + " bytes)");
                    }
                }

                return true;
            }
            catch (Exception exception)
            {
                error = "Could not stage build: " + exception.Message;
                return false;
            }
        }

        internal static bool TryPromote(
            string outputRoot,
            string temporaryPath,
            string finalPath,
            string queueId,
            out string error)
        {
            error = null;
            string temp;
            string final;
            if (!TryGetContainedPath(outputRoot, temporaryPath, out temp, out error)
                || !TryGetContainedPath(outputRoot, finalPath, out final, out error)
                || !TryEnsureLinkFree(outputRoot, temp, false, out error)
                || !TryEnsureLinkFree(outputRoot, final, false, out error))
            {
                return false;
            }

            string suffix = BuildPaths.ToPortableKey(queueId);
            string replacement = final + ".replacing-" + suffix;
            string previous = final + ".previous";
            string journal = final + ".promotion";
            if (!TryRecoverPromotion(outputRoot, final, replacement, previous, journal, out error))
                return false;

            try
            {
                string parent = Path.GetDirectoryName(final);
                if (!string.IsNullOrEmpty(parent))
                    Directory.CreateDirectory(parent);
                if (Directory.Exists(replacement)
                    && !TryDeleteDirectory(outputRoot, replacement, out error))
                {
                    return false;
                }

                WriteAtomic(journal, "moving-current\n");
                if (Directory.Exists(final))
                    Directory.Move(final, replacement);
                WriteAtomic(journal, "installing-new\n");
                Directory.Move(temp, final);
                WriteAtomic(journal, "new-installed\n");

                if (Directory.Exists(replacement))
                {
                    if (Directory.Exists(previous)
                        && !TryDeleteDirectory(outputRoot, previous, out error))
                    {
                        return false;
                    }
                    Directory.Move(replacement, previous);
                }

                TryDeleteFile(journal);
                return true;
            }
            catch (Exception exception)
            {
                string recoveryError;
                TryRecoverPromotion(
                    outputRoot,
                    final,
                    replacement,
                    previous,
                    journal,
                    out recoveryError);
                error = "Could not replace the previous build folder: " + exception.Message;
                if (!string.IsNullOrEmpty(recoveryError))
                    error += " Recovery also failed: " + recoveryError;
                return false;
            }
        }

        internal static bool TryRecoverPromotion(
            string outputRoot,
            string finalPath,
            string replacementPath,
            string previousPath,
            string journalPath,
            out string error)
        {
            error = null;
            string final;
            string replacement;
            string previous;
            string journal;
            if (!TryGetContainedPath(outputRoot, finalPath, out final, out error)
                || !TryGetContainedPath(outputRoot, replacementPath, out replacement, out error)
                || !TryGetContainedPath(outputRoot, previousPath, out previous, out error)
                || !TryGetContainedPath(outputRoot, journalPath, out journal, out error)
                || !TryEnsureLinkFree(outputRoot, final, false, out error)
                || !TryEnsureLinkFree(outputRoot, replacement, false, out error)
                || !TryEnsureLinkFree(outputRoot, previous, false, out error))
            {
                return false;
            }

            try
            {
                if (!Directory.Exists(final))
                {
                    if (Directory.Exists(replacement))
                        Directory.Move(replacement, final);
                    else if (Directory.Exists(previous))
                        Directory.Move(previous, final);
                }
                else if (Directory.Exists(replacement))
                {
                    if (Directory.Exists(previous)
                        && !TryDeleteDirectory(outputRoot, previous, out error))
                    {
                        return false;
                    }
                    Directory.Move(replacement, previous);
                }

                TryDeleteFile(journal);
                return true;
            }
            catch (Exception exception)
            {
                error = "Could not recover artifact promotion: " + exception.Message;
                return false;
            }
        }

        internal static void WriteAtomic(string path, string contents)
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            string temp = path + ".tmp";
            File.WriteAllText(temp, contents ?? string.Empty, new UTF8Encoding(false));
            if (!File.Exists(path))
            {
                File.Move(temp, path);
                return;
            }

            try
            {
                File.Replace(temp, path, null);
            }
            catch (PlatformNotSupportedException)
            {
                File.Delete(path);
                File.Move(temp, path);
            }
        }

        static bool Exists(string path)
        {
            return File.Exists(path) || Directory.Exists(path);
        }

        static bool IsLink(string path)
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }

        static void DeleteTreeWithoutFollowingLinks(string path)
        {
            FileAttributes attributes = File.GetAttributes(path);
            bool directory = (attributes & FileAttributes.Directory) != 0;
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                if (directory)
                    Directory.Delete(path, false);
                else
                    File.Delete(path);
                return;
            }

            if (!directory)
            {
                File.SetAttributes(path, FileAttributes.Normal);
                File.Delete(path);
                return;
            }

            foreach (string entry in Directory.EnumerateFileSystemEntries(path))
                DeleteTreeWithoutFollowingLinks(entry);
            File.SetAttributes(path, FileAttributes.Normal);
            Directory.Delete(path, false);
        }

        static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception)
            {
                // A later recovery pass can remove the journal.
            }
        }

        static string TrimTrailingSeparators(string path)
        {
            string root = Path.GetPathRoot(path);
            while (path.Length > root.Length
                && (path[path.Length - 1] == Path.DirectorySeparatorChar
                    || path[path.Length - 1] == Path.AltDirectorySeparatorChar))
            {
                path = path.Substring(0, path.Length - 1);
            }
            return path;
        }

        static bool IsWindows()
        {
            PlatformID platform = Environment.OSVersion.Platform;
            return platform == PlatformID.Win32NT
                || platform == PlatformID.Win32S
                || platform == PlatformID.Win32Windows
                || platform == PlatformID.WinCE;
        }
    }
}
