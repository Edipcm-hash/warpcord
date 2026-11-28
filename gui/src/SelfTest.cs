using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace WarpCord
{
    /// <summary>
    /// Pure-logic checks that need no window, no tunnel and no admin rights, so
    /// CI can run them on a bare Windows runner. Exit code 0 means everything passed.
    /// </summary>
    internal static class SelfTest
    {
        private static int _failures;
        private static int _checks;

        public static int Run()
        {
            ConsoleIo.UseUtf8();

            Console.WriteLine("WarpCord " + AppInfo.Version + " · selftest");
            Console.WriteLine();

            JsonChecks();
            VersionChecks();
            LanguageChecks();
            EndpointChecks();
            EngineChecks();
            ActionChecks();
            CliChecks();
            LogChecks();
            EngineSourceChecks();

            Console.WriteLine();
            Console.WriteLine(_failures == 0
                ? "SONUC: " + _checks + " kontrol gecti."
                : "SONUC: " + _failures + "/" + _checks + " kontrol basarisiz.");
            return _failures == 0 ? 0 : 1;
        }

        private static void JsonChecks()
        {
            Console.WriteLine("json");
            object node = Json.Parse("{\"a\":{\"b\":\"x\"},\"n\":3,\"f\":true,\"arr\":[1,2],\"e\":\"\"}");
            Check("nested string", Json.Str(node, "a", "b") == "x");
            Check("int", Json.Int(node, 0, "n") == 3);
            Check("bool", Json.Bool(node, false, "f"));
            Check("missing key is null", Json.Str(node, "a", "nope") == null);
            Check("missing int uses fallback", Json.Int(node, 42, "nope") == 42);
            var list = Json.Get(node, "arr") as IList;
            Check("array length", list != null && list.Count == 2);
            Check("empty string stays empty", Json.Str(node, "e") == "");
            Check("broken json is null", Json.Parse("{oops") == null);
            Check("json with prefix noise", Json.Str(Json.Parse("x{\"k\":1}"), "k") == "1");
        }

        private static void VersionChecks()
        {
            Console.WriteLine("surum");
            Check("equal", VersionUtil.Compare("4.0.0", "4.0.0") == 0);
            Check("minor newer", VersionUtil.Compare("4.1.0", "4.0.9") > 0);
            Check("old is older", VersionUtil.Compare("3.9.9", "4.0.0") < 0);
            Check("leading v ignored", VersionUtil.Compare("v5.0.0", "5.0.0") == 0);
            Check("short form", VersionUtil.Compare("4", "4.0.0") == 0);
            Check("suffix ignored", VersionUtil.Compare("4.0.0-beta", "4.0.0") == 0);
        }

        private static void LanguageChecks()
        {
            Console.WriteLine("dil");
            string previous = L10n.Lang;
            L10n.Lang = "tr";
            Check("turkish passthrough", L10n.T("Onar") == "Onar");
            L10n.Lang = "en";
            Check("english lookup", L10n.T("Onar") == "Repair");
            Check("unknown falls back", L10n.T("bilinmeyen metin") == "bilinmeyen metin");
            Check("phrase replace", L10n.P("WARP tüneli · uç 1.2.3.4") == "WARP tunnel · endpoint 1.2.3.4");
            Check("phrase keeps english", L10n.P("running") == "running");
            L10n.Lang = previous;
        }

        private static void EndpointChecks()
        {
            Console.WriteLine("uclar");
            var rows = new List<EndpointRow>
            {
                new EndpointRow { Address = "b", Port = 2, Ok = false, Ms = 10 },
                new EndpointRow { Address = "a", Port = 1, Ok = true, Ms = 90 },
                new EndpointRow { Address = "c", Port = 3, Ok = true, Ms = 40 }
            };
            List<EndpointRow> ranked = SettingsWindow.Rank(rows);
            Check("best first", ranked[0].Address == "c");
            Check("second best", ranked[1].Address == "a");
            Check("dead last", ranked[2].Address == "b");
            Check("rank keeps everything", ranked.Count == 3);
        }

        private static void EngineChecks()
        {
            Console.WriteLine("motor");
            string[] expected = Engine.ExpectedResources().ToArray();
            Check("expects ten engine files", expected.Length == 10);
            var embedded = new HashSet<string>(typeof(Engine).Assembly.GetManifestResourceNames(), StringComparer.Ordinal);
            int missing = expected.Count(name => !embedded.Contains(name));
            Check("all engine files embedded", missing == 0);
            if (missing > 0) Console.WriteLine("    eksik: " + string.Join(", ", expected.Where(n => !embedded.Contains(n)).ToArray()));
        }

        private static void ActionChecks()
        {
            Console.WriteLine("islemler");
            var settings = new Settings();
            foreach (ActionKind kind in Enum.GetValues(typeof(ActionKind)))
            {
                ActionSpec spec = Actions.Build(kind, settings, "1.2.3.4:2408");
                Check(kind + " has title", !string.IsNullOrEmpty(spec.Title));
                Check(kind + " has switches", spec.Switches != null);
                Check(kind + " name round trip", Actions.Parse(Actions.Name(kind), ActionKind.Install) == kind);
            }
            Check("install carries endpoint", Actions.Build(ActionKind.Install, settings, "1.2.3.4:2408").Switches.Contains("1.2.3.4:2408"));
            Check("status needs no admin", !Actions.NeedsAdmin(ActionKind.Status));
            Check("install needs admin", Actions.NeedsAdmin(ActionKind.Install));
        }

        private static void CliChecks()
        {
            Console.WriteLine("komut satiri");
            CliOptions parsed = CliOptions.Parse(new[] { "--render", "out.png", "--size", "1000x760", "--demo", "--state", "broken", "--engine" });
            Check("render path", parsed.Render == "out.png");
            Check("size width", Math.Abs(parsed.RenderWidth - 1000) < 0.5);
            Check("size height", Math.Abs(parsed.RenderHeight - 760) < 0.5);
            Check("demo flag", parsed.Demo);
            Check("state flag", parsed.PreviewState == "broken");
            Check("engine flag", parsed.UseEmbeddedEngine);
            bool threw = false;
            try { CliOptions.Parse(new[] { "--render" }); }
            catch (ArgumentException) { threw = true; }
            Check("missing value throws", threw);
        }

        private static void LogChecks()
        {
            Console.WriteLine("gunluk");
            Check("ok line", Runner.Classify("03:00 [ OK ] bir sey") == LogKind.Ok);
            Check("error line", Runner.Classify("[!] HATA oldu") == LogKind.Error);
            Check("warn line", Runner.Classify("UYARI: dikkat") == LogKind.Warn);
            Check("header line", Runner.Classify("--- Dogrulama ---") == LogKind.Header);
            Check("step line", Runner.Classify("===> uclar deneniyor") == LogKind.Step);
            Check("plain line", Runner.Classify("outbound/direct[direct] connection") == LogKind.Info);
        }

        private static void EngineSourceChecks()
        {
            Console.WriteLine("kaynak");
            string root = Paths.RepoRoot;
            Check("engine or repo found", root != null);
            Check("install script", Paths.InstallScript != null);
            Check("heal script", Paths.HealScript != null);
            Check("status script", Paths.StatusScript != null);
            Check("readiness script", ScriptHost.ToolPath("warpreadiness.ps1") != null);
            Check("endpoints script", ScriptHost.ToolPath("warpendpoints.ps1") != null);
            Check("powershell present", System.IO.File.Exists(Paths.PowerShell));
            Check("settings path is absolute", System.IO.Path.IsPathRooted(Paths.SettingsPath));
        }

        private static void Check(string name, bool ok)
        {
            _checks++;
            if (ok)
            {
                Console.WriteLine("  [ok]   " + name);
                return;
            }
            _failures++;
            Console.WriteLine("  [FAIL] " + name);
        }
    }
}
