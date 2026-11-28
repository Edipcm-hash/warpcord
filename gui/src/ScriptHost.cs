using System;
using System.Diagnostics;
using System.Text;

namespace WarpCord
{
    /// <summary>Runs an engine script and captures its stdout (JSON helpers).</summary>
    internal static class ScriptHost
    {
        public static string Run(string scriptPath, string arguments, int timeoutMs)
        {
            if (string.IsNullOrEmpty(scriptPath) || !System.IO.File.Exists(scriptPath))
                throw new InvalidOperationException("Betik bulunamadı: " + scriptPath);

            string inner = "& { [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding $false; " +
                           "& '" + scriptPath + "'" + (string.IsNullOrEmpty(arguments) ? "" : " " + arguments) + " }";

            var info = new ProcessStartInfo(Paths.PowerShell,
                "-NoProfile -ExecutionPolicy Bypass -NoLogo -NonInteractive -Command \"" + inner + "\"");
            info.UseShellExecute = false;
            info.CreateNoWindow = true;
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;
            info.StandardOutputEncoding = new UTF8Encoding(false);
            info.StandardErrorEncoding = new UTF8Encoding(false);

            using (var process = new Process())
            {
                process.StartInfo = info;
                var output = new StringBuilder();
                process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e)
                {
                    if (e.Data != null) output.AppendLine(e.Data);
                };
                process.ErrorDataReceived += delegate { };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                if (!process.WaitForExit(timeoutMs))
                {
                    try { process.Kill(); }
                    catch { }
                    throw new TimeoutException("Betik zaman aşımına uğradı: " + System.IO.Path.GetFileName(scriptPath));
                }

                process.WaitForExit();
                return output.ToString().Trim();
            }
        }

        /// <summary>Runs an inline PowerShell command and returns its stdout.</summary>
        public static string RunCommand(string command, int timeoutMs)
        {
            var info = new ProcessStartInfo(Paths.PowerShell,
                "-NoProfile -ExecutionPolicy Bypass -NoLogo -NonInteractive -Command \"" + command + "\"");
            info.UseShellExecute = false;
            info.CreateNoWindow = true;
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;
            info.StandardOutputEncoding = new UTF8Encoding(false);
            info.StandardErrorEncoding = new UTF8Encoding(false);

            using (var process = new Process())
            {
                process.StartInfo = info;
                process.Start();
                string output = process.StandardOutput.ReadToEnd();
                process.StandardError.ReadToEnd();
                if (!process.WaitForExit(timeoutMs))
                {
                    try { process.Kill(); } catch { }
                    return "";
                }
                return output;
            }
        }
        public static string ToolPath(string fileName)
        {
            string root = Paths.RepoRoot;
            if (root == null) return null;
            string path = System.IO.Path.Combine(root, @"tools\" + fileName);
            return System.IO.File.Exists(path) ? path : null;
        }
    }
}
