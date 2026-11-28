using System;

namespace WarpCord
{
    /// <summary>Script, switches and title for one action; shared by UI and CLI.</summary>
    internal sealed class ActionSpec
    {
        public ActionSpec(string script, string switches, string title, string missingRelative)
        {
            Script = script;
            Switches = switches;
            Title = title;
            MissingRelative = missingRelative;
        }

        public string Script { get; private set; }
        public string Switches { get; private set; }
        public string Title { get; private set; }
        public string MissingRelative { get; private set; }
    }

    internal static class Actions
    {
        public static ActionSpec Build(ActionKind kind, Settings settings, string endpoint)
        {
            string repoArg = Paths.RepoRoot != null ? " -RepoRoot '" + Paths.RepoRoot + "'" : "";
            endpoint = endpoint ?? "";
            settings = settings ?? new Settings();

            switch (kind)
            {
                case ActionKind.Install:
                    return new ActionSpec(
                        Paths.InstallScript,
                        "-Mode " + settings.Mode +
                        (settings.Force ? " -Force" : "") +
                        (settings.KeepDns ? " -KeepDns" : "") +
                        (endpoint.Length > 0 ? " -Endpoint " + endpoint : "") +
                        " -ExtraApps '" + Sanitize(settings.ExtraApps) + "'" +
                        " -NoElevate" + repoArg,
                        "Kurulum",
                        @"app\install.ps1");

                case ActionKind.Heal:
                    return new ActionSpec(
                        Paths.HealScript,
                        "-InstallDir '" + Paths.InstallDir + "' -NoElevate -WaitSeconds 20",
                        "Onarım",
                        @"tools\warpheal.ps1");

                case ActionKind.Status:
                    return new ActionSpec(
                        Paths.StatusScript,
                        "-NoElevate" + repoArg,
                        "Durum kontrolü",
                        @"app\status.ps1");

                case ActionKind.Diagnose:
                    return new ActionSpec(
                        Paths.DiagnoseScript,
                        "-NoElevate",
                        "Tanı",
                        @"tools\tani.ps1");

                default:
                    return new ActionSpec(
                        Paths.UninstallScript,
                        (settings.KeepDns ? "-KeepDns " : "") + "-NoElevate" + repoArg,
                        "Kaldırma",
                        @"app\uninstall.ps1");
            }
        }

        public static ActionKind Parse(string name, ActionKind fallback)
        {
            switch ((name ?? "").ToLowerInvariant())
            {
                case "install": return ActionKind.Install;
                case "heal": return ActionKind.Heal;
                case "status": return ActionKind.Status;
                case "diagnose": return ActionKind.Diagnose;
                case "uninstall": return ActionKind.Uninstall;
                default: return fallback;
            }
        }

        public static string Name(ActionKind kind)
        {
            switch (kind)
            {
                case ActionKind.Install: return "install";
                case ActionKind.Heal: return "heal";
                case ActionKind.Status: return "status";
                case ActionKind.Diagnose: return "diagnose";
                default: return "uninstall";
            }
        }

/// <summary>Only process names and separators may reach the installer's argument list.</summary>
        private static string Sanitize(string list)
        {
            if (string.IsNullOrEmpty(list)) return "";
            var builder = new System.Text.StringBuilder();
            foreach (char c in list)
            {
                if (char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '_' || c == ',' || c == ';' || c == ' ')
                    builder.Append(c);
            }
            return builder.ToString();
        }
        public static bool NeedsAdmin(ActionKind kind)
        {
            return kind != ActionKind.Status;
        }
    }
}
