using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Media;

namespace WarpCord
{
    /// <summary>User preferences, persisted to %APPDATA%\WarpCord\ui.json.</summary>
    internal sealed class Settings
    {
        public string Mode = "auto";
        public bool Force;
        public bool KeepDns;
        public string Endpoint = "";
        public string ExtraApps = "";
        public string InstallMethod = "app";
        public bool AskedInstallMethod;
        public string Language = "tr";
        public bool AutoHeal = true;
        public bool TrayEnabled = true;
        public bool Notifications = true;
        public bool MinimizeToTray;
        public bool StartWithWindows;
        public string LastVersionCheck = "";
        public string LatestVersion = "";
        public bool Advanced;
        public bool Autoscroll = true;
        public double Width = 1000;
        public double Height = 760;
        public double Left;
        public double Top;
        public bool HasBounds;
        public bool Maximized;

        public static Settings Load()
        {
            var settings = new Settings();
            object node = Json.Node(Paths.SettingsPath);
            if (node == null) return settings;

            string mode = Json.Str(node, "mode");
            if (mode == "auto" || mode == "warp" || mode == "dpi") settings.Mode = mode;
            settings.Force = Json.Bool(node, false, "force");
            settings.KeepDns = Json.Bool(node, false, "keepDns");
            settings.Endpoint = Json.Str(node, "endpoint") ?? "";
            settings.ExtraApps = Json.Str(node, "extraApps") ?? "";
            string method = Json.Str(node, "installMethod");
            if (method == "app" || method == "manual") settings.InstallMethod = method;
            settings.AskedInstallMethod = Json.Bool(node, false, "askedInstallMethod");
            string language = Json.Str(node, "language");
            if (language == "tr" || language == "en") settings.Language = language;
            settings.AutoHeal = Json.Bool(node, true, "autoHeal");
            settings.TrayEnabled = Json.Bool(node, true, "trayEnabled");
            settings.Notifications = Json.Bool(node, true, "notifications");
            settings.MinimizeToTray = Json.Bool(node, false, "minimizeToTray");
            settings.StartWithWindows = Json.Bool(node, false, "startWithWindows");
            settings.LastVersionCheck = Json.Str(node, "lastVersionCheck") ?? "";
            settings.LatestVersion = Json.Str(node, "latestVersion") ?? "";
            settings.Advanced = Json.Bool(node, false, "advanced");
            settings.Autoscroll = Json.Bool(node, true, "autoscroll");
            settings.Width = Json.Int(node, 1000, "width");
            settings.Height = Json.Int(node, 760, "height");
            settings.Left = Json.Int(node, 0, "left");
            settings.Top = Json.Int(node, 0, "top");
            settings.HasBounds = Json.Bool(node, false, "hasBounds");
            settings.Maximized = Json.Bool(node, false, "maximized");
            return settings;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Paths.AppDataDir);
                var map = new Dictionary<string, object>
                {
                    { "mode", Mode },
                    { "force", Force },
                    { "keepDns", KeepDns },
                    { "endpoint", Endpoint ?? "" },
                    { "extraApps", ExtraApps ?? "" },
                    { "installMethod", InstallMethod ?? "app" },
                    { "askedInstallMethod", AskedInstallMethod },
                    { "language", Language ?? "tr" },
                    { "autoHeal", AutoHeal },
                    { "trayEnabled", TrayEnabled },
                    { "notifications", Notifications },
                    { "minimizeToTray", MinimizeToTray },
                    { "startWithWindows", StartWithWindows },
                    { "lastVersionCheck", LastVersionCheck ?? "" },
                    { "latestVersion", LatestVersion ?? "" },
                    { "advanced", Advanced },
                    { "autoscroll", Autoscroll },
                    { "width", (int)Math.Round(Width) },
                    { "height", (int)Math.Round(Height) },
                    { "left", (int)Math.Round(Left) },
                    { "top", (int)Math.Round(Top) },
                    { "hasBounds", HasBounds },
                    { "maximized", Maximized }
                };
                File.WriteAllText(Paths.SettingsPath, Json.Serialize(map));
            }
            catch
            {
                // preferences are a nicety; never break the app over them
            }
        }
    }

    /// <summary>One line of the live status list.</summary>
    internal sealed class StatRow : System.ComponentModel.INotifyPropertyChanged
    {
        private string _value;
        private Brush _dot;

        public StatRow(string label)
        {
            Label = label;
            _value = "—";
            _dot = Palette.Faint;
        }

        public string Label { get; private set; }

        public string Value
        {
            get { return _value; }
            set
            {
                if (_value == value) return;
                _value = value;
                Raise("Value");
            }
        }

        public Brush Dot
        {
            get { return _dot; }
            set
            {
                if (ReferenceEquals(_dot, value)) return;
                _dot = value;
                Raise("Dot");
            }
        }

        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;

        private void Raise(string name)
        {
            var handler = PropertyChanged;
            if (handler != null) handler(this, new System.ComponentModel.PropertyChangedEventArgs(name));
        }
    }
}
