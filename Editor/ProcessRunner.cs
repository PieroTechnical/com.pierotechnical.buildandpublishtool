using System;
using System.Diagnostics;
using System.Text;
using UnityEditor;
using Debug = UnityEngine.Debug;

namespace Pierotechnical.BuildAndUploadTool.Editor
{
    public sealed class ProcessRunResult
    {
        public int ExitCode;
        public string StandardOutput = string.Empty;
        public string StandardError = string.Empty;
        public bool TimedOut;
        public bool Cancelled;
    }

    public static class ProcessRunner
    {
        public static ProcessRunResult Run(string fileName, string arguments, int timeoutMs, string progressTitle)
        {
            return Run(fileName, arguments, timeoutMs, progressTitle, null);
        }

        public static ProcessRunResult Run(string fileName, string arguments, int timeoutMs, string progressTitle, string workingDirectory)
        {
            var result = new ProcessRunResult();
            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            var gate = new object();
            var process = new Process();

            try
            {
                process.StartInfo.FileName = fileName;
                process.StartInfo.Arguments = arguments ?? string.Empty;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.CreateNoWindow = true;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;
                process.StartInfo.StandardOutputEncoding = Encoding.UTF8;
                process.StartInfo.StandardErrorEncoding = Encoding.UTF8;
                if (!string.IsNullOrEmpty(workingDirectory))
                {
                    process.StartInfo.WorkingDirectory = workingDirectory;
                    process.StartInfo.RedirectStandardInput = true;
                }

                process.OutputDataReceived += (sender, args) =>
                {
                    if (args.Data == null)
                        return;
                    lock (gate)
                        stdout.AppendLine(args.Data);
                };
                process.ErrorDataReceived += (sender, args) =>
                {
                    if (args.Data == null)
                        return;
                    lock (gate)
                        stderr.AppendLine(args.Data);
                };

                EditorUtility.ClearProgressBar();
                process.Start();
                if (process.StartInfo.RedirectStandardInput)
                    process.StandardInput.Close();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                DateTime started = DateTime.UtcNow;
                while (!process.WaitForExit(200))
                {
                    string latest;
                    lock (gate)
                        latest = LastLine(stderr.Length > 0 ? stderr : stdout);

                    float pulse = (float)((DateTime.UtcNow - started).TotalSeconds % 10.0) / 10f;
                    string info = string.IsNullOrEmpty(latest) ? "Working..." : TrimProgress(latest);
                    if (EditorUtility.DisplayCancelableProgressBar(progressTitle, info, pulse))
                    {
                        TryKill(process);
                        result.Cancelled = true;
                        break;
                    }

                    if ((DateTime.UtcNow - started).TotalMilliseconds > timeoutMs)
                    {
                        TryKill(process);
                        result.TimedOut = true;
                        break;
                    }
                }

                if (result.Cancelled || result.TimedOut)
                {
                    try
                    {
                        process.WaitForExit(2000);
                    }
                    catch (Exception)
                    {
                        // The process handle is already gone.
                    }
                }
                else
                {
                    process.WaitForExit();
                    result.ExitCode = process.ExitCode;
                }
            }
            catch (Exception exception)
            {
                TryKill(process);
                result.ExitCode = -1;
                result.StandardError = exception.Message;
                Debug.LogError("Could not run process: " + exception.Message);
            }
            finally
            {
                lock (gate)
                {
                    if (string.IsNullOrEmpty(result.StandardOutput))
                        result.StandardOutput = stdout.ToString();
                    if (string.IsNullOrEmpty(result.StandardError))
                        result.StandardError = stderr.ToString();
                }

                process.Dispose();
                EditorUtility.ClearProgressBar();
            }

            return result;
        }

        static string LastLine(StringBuilder builder)
        {
            if (builder == null || builder.Length == 0)
                return string.Empty;

            string text = builder.ToString().TrimEnd('\r', '\n');
            int index = text.LastIndexOf('\n');
            if (index < 0 || index >= text.Length - 1)
                return text.Trim();

            return text.Substring(index + 1).Trim();
        }

        static string TrimProgress(string value)
        {
            const int limit = 120;
            if (value.Length <= limit)
                return value;
            return value.Substring(value.Length - limit);
        }

        static void TryKill(Process process)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill();
            }
            catch (Exception)
            {
                // The process already exited.
            }
        }
    }
}
