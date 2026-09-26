using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using Debug = UnityEngine.Debug;

namespace Pierotechnical.BuildAndUploadTool.Editor
{
    internal sealed class ProcessRunResult
    {
        internal int ExitCode = -1;
        internal string StandardOutput = string.Empty;
        internal string StandardError = string.Empty;
        internal string LatestLine = string.Empty;
        internal bool TimedOut;
        internal bool Cancelled;
        internal bool OutputTruncated;
        internal bool TerminationUnconfirmed;
        internal long DurationMilliseconds;
    }

    internal static class ProcessOutput
    {
        static readonly Regex SensitiveValue = new Regex(
            @"(?ix)
            \b(password|passwd|steam[\s_-]*guard(?:[\s_-]*code)?|
               two[\s_-]*factor(?:[\s_-]*code)?|
               authorization|api[\s_-]*key|access[\s_-]*token|
               refresh[\s_-]*token|secret)
            (\s*[:=]\s*|\s+)
            ([^\s""']+|""[^""]*""|'[^']*')",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
        static readonly Regex BearerValue = new Regex(
            @"(?i)\bbearer\s+\S+",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        internal static string Redact(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text ?? string.Empty;
            return SensitiveValue.Replace(
                BearerValue.Replace(text, "bearer <redacted>"),
                "$1$2<redacted>");
        }
    }

    internal static class ProcessRunner
    {
        const int OutputCharacterLimit = 64 * 1024;
        const int TerminationGraceMilliseconds = 2500;

        sealed class BoundedOutput
        {
            readonly Queue<string> lines = new Queue<string>();
            int characters;

            internal bool Truncated { get; private set; }

            internal void Add(string line)
            {
                string value = ProcessOutput.Redact(line ?? string.Empty) + Environment.NewLine;
                if (value.Length > OutputCharacterLimit)
                {
                    value = value.Substring(value.Length - OutputCharacterLimit);
                    lines.Clear();
                    characters = 0;
                    Truncated = true;
                }

                lines.Enqueue(value);
                characters += value.Length;
                while (characters > OutputCharacterLimit && lines.Count > 1)
                {
                    characters -= lines.Dequeue().Length;
                    Truncated = true;
                }
            }

            public override string ToString()
            {
                var builder = new StringBuilder(characters);
                foreach (string line in lines)
                    builder.Append(line);
                return builder.ToString();
            }
        }

        sealed class ProcessOperation : IPublishOperation
        {
            internal readonly object Gate = new object();
            internal readonly BoundedOutput Stdout = new BoundedOutput();
            internal readonly BoundedOutput Stderr = new BoundedOutput();
            internal readonly string FileName;
            internal readonly string Arguments;
            internal readonly string WorkingDirectory;
            internal readonly int TimeoutMs;
            internal readonly Action<string> OutputSink;
            internal readonly Action<ProcessRunResult> OnComplete;
            internal readonly string ToolKey;

            internal Process Process;
            internal DateTime StartedUtc;
            internal DateTime TerminationRequestedUtc;
            internal string Latest = string.Empty;
            internal string StartError;
            internal bool Started;
            internal bool Finished;
            internal bool CancelRequested;
            internal bool TimedOut;
            internal bool TreeTerminationRequested;
            internal bool TreeTerminationConfirmed;

            internal ProcessOperation(
                string fileName,
                string arguments,
                int timeoutMs,
                string workingDirectory,
                Action<string> outputSink,
                Action<ProcessRunResult> onComplete)
            {
                FileName = fileName ?? string.Empty;
                Arguments = arguments ?? string.Empty;
                TimeoutMs = timeoutMs < 1 ? 1 : timeoutMs;
                WorkingDirectory = workingDirectory ?? string.Empty;
                OutputSink = outputSink;
                OnComplete = onComplete;
                ToolKey = NormalizeToolKey(FileName);
            }

            public bool CanCancel
            {
                get
                {
                    lock (Gate)
                        return !Finished && !CancelRequested && !TimedOut;
                }
            }

            public string Status
            {
                get
                {
                    lock (Gate)
                    {
                        if (!Started)
                            return CancelRequested ? "Cancelling queued process." : "Waiting to start.";
                        if (CancelRequested || TimedOut)
                            return "Stopping external process.";
                        return Latest;
                    }
                }
            }

            public void Cancel()
            {
                lock (Gate)
                {
                    if (Finished || CancelRequested || TimedOut)
                        return;
                    CancelRequested = true;
                }

                RequestTermination(this);
            }
        }

        static readonly List<ProcessOperation> Operations = new List<ProcessOperation>();
        static readonly HashSet<string> ActiveTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        static bool updateHooked;

        internal static IPublishOperation RunAsync(
            string fileName,
            string arguments,
            int timeoutMs,
            string workingDirectory,
            Action<ProcessRunResult> onComplete)
        {
            return RunAsync(
                fileName,
                arguments,
                timeoutMs,
                workingDirectory,
                null,
                onComplete);
        }

        internal static IPublishOperation RunAsync(
            string fileName,
            string arguments,
            int timeoutMs,
            string workingDirectory,
            Action<string> outputSink,
            Action<ProcessRunResult> onComplete)
        {
            if (onComplete == null)
                return CompletedPublishOperation.Instance;

            var operation = new ProcessOperation(
                fileName,
                arguments,
                timeoutMs,
                workingDirectory,
                outputSink,
                onComplete);
            lock (Operations)
                Operations.Add(operation);
            EnsureUpdateHook();
            StartPendingOperations();
            return operation;
        }

        static void EnsureUpdateHook()
        {
            if (updateHooked)
                return;
            updateHooked = true;
            EditorApplication.update += Poll;
            AssemblyReloadEvents.beforeAssemblyReload += CancelForReload;
        }

        static void RemoveUpdateHookIfIdle()
        {
            lock (Operations)
            {
                if (Operations.Count != 0 || !updateHooked)
                    return;
                updateHooked = false;
            }

            EditorApplication.update -= Poll;
            AssemblyReloadEvents.beforeAssemblyReload -= CancelForReload;
        }

        static void StartPendingOperations()
        {
            lock (Operations)
            {
                for (int i = 0; i < Operations.Count; i++)
                {
                    ProcessOperation operation = Operations[i];
                    lock (operation.Gate)
                    {
                        if (operation.Started || operation.Finished || operation.CancelRequested)
                            continue;
                        if (ActiveTools.Contains(operation.ToolKey))
                            continue;
                        ActiveTools.Add(operation.ToolKey);
                        Start(operation);
                    }
                }
            }
        }

        static void Start(ProcessOperation operation)
        {
            operation.Started = true;
            operation.StartedUtc = DateTime.UtcNow;
            operation.Process = new Process();
            try
            {
                Configure(operation);
                operation.Process.Start();
                if (operation.Process.StartInfo.RedirectStandardInput)
                    operation.Process.StandardInput.Close();
                operation.Process.BeginOutputReadLine();
                operation.Process.BeginErrorReadLine();
            }
            catch (Exception exception)
            {
                operation.StartError = ProcessOutput.Redact(exception.Message);
            }
        }

        static void Configure(ProcessOperation operation)
        {
            ProcessStartInfo start = operation.Process.StartInfo;
            start.FileName = operation.FileName;
            start.Arguments = operation.Arguments;
            start.UseShellExecute = false;
            start.CreateNoWindow = true;
            start.RedirectStandardInput = true;
            start.RedirectStandardOutput = true;
            start.RedirectStandardError = true;
            start.StandardOutputEncoding = Encoding.UTF8;
            start.StandardErrorEncoding = Encoding.UTF8;
            if (!string.IsNullOrEmpty(operation.WorkingDirectory))
                start.WorkingDirectory = operation.WorkingDirectory;

            operation.Process.OutputDataReceived += (sender, args) =>
                ReceiveLine(operation, operation.Stdout, args.Data);
            operation.Process.ErrorDataReceived += (sender, args) =>
                ReceiveLine(operation, operation.Stderr, args.Data);
        }

        static void ReceiveLine(
            ProcessOperation operation,
            BoundedOutput output,
            string line)
        {
            if (line == null)
                return;

            string sanitized = ProcessOutput.Redact(line);
            lock (operation.Gate)
            {
                output.Add(sanitized);
                operation.Latest = sanitized;
            }

            if (operation.OutputSink == null)
                return;
            try
            {
                operation.OutputSink(sanitized);
            }
            catch (Exception)
            {
                // Logging must never destabilize process supervision.
            }
        }

        static void Poll()
        {
            List<ProcessOperation> finished = null;
            lock (Operations)
            {
                for (int i = Operations.Count - 1; i >= 0; i--)
                {
                    ProcessOperation operation = Operations[i];
                    if (!ShouldComplete(operation))
                        continue;

                    Operations.RemoveAt(i);
                    ActiveTools.Remove(operation.ToolKey);
                    if (finished == null)
                        finished = new List<ProcessOperation>();
                    finished.Add(operation);
                }
            }

            if (finished != null)
            {
                for (int i = 0; i < finished.Count; i++)
                    Complete(finished[i], true);
            }

            StartPendingOperations();
            RemoveUpdateHookIfIdle();
        }

        static bool ShouldComplete(ProcessOperation operation)
        {
            bool stop = false;
            bool complete;
            lock (operation.Gate)
                complete = EvaluateCompletion(operation, out stop);

            if (stop)
                RequestTermination(operation);
            return complete;
        }

        static bool EvaluateCompletion(ProcessOperation operation, out bool stop)
        {
            stop = false;
            if (operation.Finished)
                return true;
            if (!operation.Started)
                return operation.CancelRequested;
            if (!string.IsNullOrEmpty(operation.StartError))
                return true;

            bool exited;
            try
            {
                exited = operation.Process.HasExited;
            }
            catch (Exception)
            {
                return true;
            }

            if (exited)
                return true;

            if (!operation.CancelRequested
                && !operation.TimedOut
                && (DateTime.UtcNow - operation.StartedUtc).TotalMilliseconds > operation.TimeoutMs)
            {
                operation.TimedOut = true;
                stop = operation.TerminationRequestedUtc == default(DateTime);
                return false;
            }

            return (operation.CancelRequested || operation.TimedOut)
                && operation.TerminationRequestedUtc != default(DateTime)
                && (DateTime.UtcNow - operation.TerminationRequestedUtc).TotalMilliseconds
                    > TerminationGraceMilliseconds;
        }

        static void RequestTermination(ProcessOperation operation)
        {
            Process process;
            lock (operation.Gate)
            {
                if (!operation.Started || operation.Process == null)
                    return;
                if (operation.TerminationRequestedUtc != default(DateTime))
                    return;
                operation.TerminationRequestedUtc = DateTime.UtcNow;
                operation.TreeTerminationRequested = true;
                process = operation.Process;
            }

            bool confirmed = TryKillTree(process);
            lock (operation.Gate)
                operation.TreeTerminationConfirmed = confirmed;
        }

        static bool TryKillTree(Process process)
        {
            try
            {
                if (HasExited(process))
                    return true;

                bool killedTree = false;
                if (IsWindows())
                {
                    try
                    {
                        var start = new ProcessStartInfo
                        {
                            FileName = "taskkill.exe",
                            Arguments = "/PID " + process.Id + " /T /F",
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };
                        using (Process killer = Process.Start(start))
                        {
                            killedTree = killer != null
                                && killer.WaitForExit(1500)
                                && killer.ExitCode == 0;
                        }
                    }
                    catch (Exception)
                    {
                        // Fall through to terminating the direct process.
                    }
                }

                if (!HasExited(process))
                    process.Kill();
                return killedTree && HasExited(process);
            }
            catch (Exception)
            {
                return false;
            }
        }

        static bool HasExited(Process process)
        {
            try
            {
                return process != null && process.HasExited;
            }
            catch (Exception)
            {
                return false;
            }
        }

        static void Complete(ProcessOperation operation, bool invokeCallback)
        {
            bool cancelled;
            bool timedOut;
            bool started;
            string startError;
            Process process;
            lock (operation.Gate)
            {
                cancelled = operation.CancelRequested;
                timedOut = operation.TimedOut;
                started = operation.Started;
                startError = operation.StartError;
                process = operation.Process;
            }

            if (process != null && started && string.IsNullOrEmpty(startError))
            {
                try
                {
                    if (!cancelled && !timedOut)
                    {
                        process.WaitForExit();
                    }
                    else
                    {
                        try
                        {
                            process.CancelOutputRead();
                        }
                        catch (Exception)
                        {
                            // Output redirection may not have started.
                        }

                        try
                        {
                            process.CancelErrorRead();
                        }
                        catch (Exception)
                        {
                            // Error redirection may not have started.
                        }

                        process.WaitForExit(500);
                    }
                }
                catch (Exception)
                {
                    // The handle may already be unavailable.
                }
            }

            ProcessRunResult result;
            lock (operation.Gate)
            {
                operation.Finished = true;
                result = new ProcessRunResult
                {
                    Cancelled = operation.CancelRequested,
                    TimedOut = operation.TimedOut,
                    LatestLine = operation.Latest,
                    StandardOutput = operation.Stdout.ToString(),
                    StandardError = string.IsNullOrEmpty(operation.StartError)
                        ? operation.Stderr.ToString()
                        : operation.StartError,
                    OutputTruncated = operation.Stdout.Truncated || operation.Stderr.Truncated,
                    DurationMilliseconds = operation.StartedUtc == default(DateTime)
                        ? 0L
                        : (long)Math.Max(0, (DateTime.UtcNow - operation.StartedUtc).TotalMilliseconds)
                };

                if (process != null && !result.Cancelled && !result.TimedOut && HasExited(process))
                {
                    try
                    {
                        result.ExitCode = process.ExitCode;
                    }
                    catch (Exception)
                    {
                        result.ExitCode = -1;
                    }
                }

                result.TerminationUnconfirmed =
                    (result.Cancelled || result.TimedOut)
                    && operation.Started
                    && operation.Process != null
                    && !operation.TreeTerminationConfirmed;
            }

            Dispose(operation.Process);
            if (!invokeCallback)
                return;
            try
            {
                operation.OnComplete(result);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        static void CancelForReload()
        {
            List<ProcessOperation> pending;
            lock (Operations)
            {
                pending = new List<ProcessOperation>(Operations);
                Operations.Clear();
                ActiveTools.Clear();
            }

            for (int i = 0; i < pending.Count; i++)
            {
                lock (pending[i].Gate)
                    pending[i].CancelRequested = true;
                RequestTermination(pending[i]);
                Complete(pending[i], false);
            }

            RemoveUpdateHookIfIdle();
        }

        static void Dispose(Process process)
        {
            if (process == null)
                return;
            try
            {
                process.Dispose();
            }
            catch (Exception)
            {
                // The operating system already released the handle.
            }
        }

        internal static string CombineOutput(ProcessRunResult result, string toolName)
        {
            var builder = new StringBuilder();
            if (result != null && !string.IsNullOrWhiteSpace(result.StandardError))
                builder.AppendLine(ProcessOutput.Redact(result.StandardError.Trim()));
            if (result != null && !string.IsNullOrWhiteSpace(result.StandardOutput))
                builder.AppendLine(ProcessOutput.Redact(result.StandardOutput.Trim()));
            if (builder.Length == 0)
            {
                builder.Append(toolName)
                    .Append(" exited with code ")
                    .Append(result == null ? -1 : result.ExitCode)
                    .Append('.');
            }

            if (result != null && result.OutputTruncated)
                builder.AppendLine().Append("Earlier process output was truncated.");
            if (result != null && result.TerminationUnconfirmed)
                builder.AppendLine().Append("Process-tree termination could not be confirmed.");
            return builder.ToString().Trim();
        }

        static string NormalizeToolKey(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
                return string.Empty;
            try
            {
                return Path.GetFullPath(fileName);
            }
            catch (Exception)
            {
                return fileName;
            }
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
