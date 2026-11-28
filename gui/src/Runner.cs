using System;
using System.Diagnostics;
using System.Text;

namespace WarpCord
{
    internal enum LogKind
    {
        Info,
        Dim,
        Ok,
        Warn,
        Error,
        Step,
        Header
    }

    internal sealed class LogLine
    {
        public LogLine(LogKind kind, string text)
        {
            Kind = kind;
            Text = text;
        }

        public LogKind Kind { get; private set; }
        public string Text { get; private set; }
    }

    /// <summary>
    /// Runs one of the PowerShell entry points with UTF-8 output and streams it
    /// line by line to the console pane.
    /// </summary>
    internal sealed class Runner
    {
        private Process _process;
        private bool _exited;
        private DateTime _started;

        public event Action<LogLine> Line;
        public event Action<string> StepChanged;
        public event Action<int, TimeSpan> Finished;

        public bool IsRunning
        {
            get { return _process != null && !_exited; }
        }

        public bool Start(string scriptPath, string switches, string workingDirectory)
        {
            if (IsRunning) return false;
            if (string.IsNullOrEmpty(scriptPath) || !System.IO.File.Exists(scriptPath))
            {
                Emit(new LogLine(LogKind.Error, "Betik bulunamadı: " + scriptPath));
                return false;
            }

            var info = new ProcessStartInfo(Paths.PowerShell, BuildArguments(scriptPath, switches ?? ""));
            info.WorkingDirectory = workingDirectory;
            info.UseShellExecute = false;
            info.CreateNoWindow = true;
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;
            info.StandardOutputEncoding = new UTF8Encoding(false);
            info.StandardErrorEncoding = new UTF8Encoding(false);

            var process = new Process();
            process.StartInfo = info;
            process.EnableRaisingEvents = true;
            process.OutputDataReceived += delegate(object s, DataReceivedEventArgs e)
            {
                if (e.Data != null) Emit(new LogLine(Classify(e.Data), e.Data));
            };
            process.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e)
            {
                if (e.Data == null) return;
                string text = e.Data.Trim();
                if (text.Length == 0) return;
                Emit(new LogLine(LogKind.Error, text));
            };
            process.Exited += OnExited;

            _exited = false;
            _started = DateTime.Now;
            try
            {
                process.Start();
            }
            catch (Exception ex)
            {
                Emit(new LogLine(LogKind.Error, "Süreç başlatılamadı: " + ex.Message));
                return false;
            }

            _process = process;
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            return true;
        }

        public void Cancel()
        {
            Process process = _process;
            if (process == null) return;
            try
            {
                if (!process.HasExited)
                {
                    // kill the whole tree: install.ps1 starts sing-box as a child
                    var killer = new ProcessStartInfo("taskkill", "/PID " + process.Id + " /T /F");
                    killer.UseShellExecute = false;
                    killer.CreateNoWindow = true;
                    killer.RedirectStandardOutput = true;
                    killer.RedirectStandardError = true;
                    Process.Start(killer);
                    Emit(new LogLine(LogKind.Warn, "İptal istendi; süreç ağacı durduruluyor…"));
                }
            }
            catch (Exception ex)
            {
                Emit(new LogLine(LogKind.Error, "Durdurulamadı: " + ex.Message));
            }
        }

        private void OnExited(object sender, EventArgs e)
        {
            var process = (Process)sender;
            try { process.WaitForExit(); }
            catch { }
            int code = -1;
            try { code = process.ExitCode; }
            catch { }
            _exited = true;
            TimeSpan elapsed = DateTime.Now - _started;
            var handler = Finished;
            if (handler != null) handler(code, elapsed);
        }

        private void Emit(LogLine line)
        {
            if (line.Kind == LogKind.Step)
            {
                string step = line.Text.Trim();
                if (step.StartsWith("===>")) step = step.Substring(4).Trim();
                var stepHandler = StepChanged;
                if (stepHandler != null) stepHandler(step);
            }
            var handler = Line;
            if (handler != null) handler(line);
        }

        private static string BuildArguments(string scriptPath, string switches)
        {
            var inner = new StringBuilder();
            inner.Append("& { ");
            inner.Append("[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding $false; ");
            inner.Append("$OutputEncoding = [Console]::OutputEncoding; ");
            inner.Append("$ProgressPreference = 'SilentlyContinue'; ");
            inner.Append("& '").Append(scriptPath).Append('\'');
            if (!string.IsNullOrEmpty(switches)) inner.Append(' ').Append(switches);
            inner.Append("; exit $LASTEXITCODE }");
            return "-NoProfile -ExecutionPolicy Bypass -NoLogo -NonInteractive -Command \"" +
                   inner + "\"";
        }

        public static LogKind Classify(string text)
        {
            string trimmed = (text ?? "").Trim();
            if (trimmed.Length == 0) return LogKind.Dim;
            if (trimmed.StartsWith("===>")) return LogKind.Step;
            if (trimmed.StartsWith("---") || trimmed.StartsWith("===")) return LogKind.Header;

            if (trimmed.IndexOf("[ OK ]", StringComparison.OrdinalIgnoreCase) >= 0) return LogKind.Ok;
            if (trimmed.IndexOf("[ -- ]", StringComparison.OrdinalIgnoreCase) >= 0) return LogKind.Dim;
            if (trimmed.IndexOf("[!!]", StringComparison.OrdinalIgnoreCase) >= 0) return LogKind.Error;
            if (Contains(trimmed, "HATA") || Contains(trimmed, "BASARISIZ") ||
                Contains(trimmed, "FAIL") || Contains(trimmed, "Exception") ||
                Contains(trimmed, "could not") || Contains(trimmed, "erişilemiyor"))
                return LogKind.Error;
            if (Contains(trimmed, "UYARI") || Contains(trimmed, "WARN") || Contains(trimmed, "dikkat"))
                return LogKind.Warn;
            if (trimmed.StartsWith("$ ") || trimmed.StartsWith("PS>")) return LogKind.Step;
            return LogKind.Info;
        }

        private static bool Contains(string haystack, string needle)
        {
            return haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
