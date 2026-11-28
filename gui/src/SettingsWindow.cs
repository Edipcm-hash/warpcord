using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Ellipse = System.Windows.Shapes.Ellipse;

namespace WarpCord
{
    internal sealed class EndpointRow
    {
        public string Text { get; set; }
        public string Latency { get; set; }
        public Brush Dot { get; set; }
        public string Address { get; set; }
        public int Port { get; set; }
        public bool Ok { get; set; }
        public int Ms { get; set; }
    }

    /// <summary>Settings, readiness, endpoint comparison and version check, in one window.</summary>
    internal sealed class SettingsWindow : Window
    {
        private const string RepoUrl = "https://github.com/Edipcm-hash/warpcord";
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        private readonly Settings _settings;
        private readonly Action _onChanged;
        private readonly Snapshot _snapshot;
        private Grid _root;

        private TextBlock _subtitle, _readinessStatus, _readinessHint, _endpointsStatus, _endpointNote;
        private TextBlock _aboutUi, _aboutTunnel, _aboutRepo, _aboutSource, _aboutUpdate, _aboutStatus, _aboutLink;
        private TextBlock _portableNote;
        private TextBlock _settingsPathNote;
        private TextBlock _engineNote;
        private StackPanel _panelGeneral, _panelReadiness, _panelEndpoints, _panelAbout, _readinessList;
        private RadioButton _tabGeneral, _tabReadiness, _tabEndpoints, _tabAbout;
        private RadioButton _languageTr, _languageEn;
        private CheckBox _switchTray, _switchNotify, _switchMinimize, _switchAutoHeal;
        private ListBox _endpointsList;
        private Button _settingsClose, _readinessBtn, _endpointsBtn, _useBestBtn, _useSelectedBtn, _updateBtn, _bundleBtn;
        private List<EndpointRow> _endpointRows = new List<EndpointRow>();
        private bool _busy;

        public SettingsWindow(Settings settings, Snapshot snapshot, Action onChanged)
        {
            _settings = settings;
            _snapshot = snapshot;
            _onChanged = onChanged;

            BuildWindow();
            BindElements();
            LoadValues();
            AttachEvents();
            L10n.Apply(_root);
        }

        private void BuildWindow()
        {
            _root = Xaml.Load<Grid>("WarpCord.SettingsWindow.xaml");
            Content = _root;
            Title = L10n.T("Ayarlar");
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.Height;
            Width = 762;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            Background = WindowFx.BrushFromResource("BgBrush", Brushes.Black);
            Foreground = WindowFx.BrushFromResource("TextBrush", Brushes.White);
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
            WindowFx.Apply(this);
        }

        private T N<T>(string name) where T : class
        {
            var element = _root.FindName(name) as T;
            if (element == null) throw new InvalidOperationException("Ayarlar öğesi bulunamadı: " + name);
            return element;
        }

        private void BindElements()
        {
            _subtitle = N<TextBlock>("SettingsSubtitle");
            _settingsClose = N<Button>("SettingsClose");
            _tabGeneral = N<RadioButton>("TabGeneral");
            _tabReadiness = N<RadioButton>("TabReadiness");
            _tabEndpoints = N<RadioButton>("TabEndpoints");
            _tabAbout = N<RadioButton>("TabAbout");
            _panelGeneral = N<StackPanel>("PanelGeneral");
            _panelReadiness = N<StackPanel>("PanelReadiness");
            _panelEndpoints = N<StackPanel>("PanelEndpoints");
            _panelAbout = N<StackPanel>("PanelAbout");
            _languageTr = N<RadioButton>("LanguageTr");
            _languageEn = N<RadioButton>("LanguageEn");
            _switchTray = N<CheckBox>("SwitchTray");
            _switchNotify = N<CheckBox>("SwitchNotify");
            _switchMinimize = N<CheckBox>("SwitchMinimize");
            _switchAutoHeal = N<CheckBox>("SwitchAutoHeal");
            _portableNote = N<TextBlock>("PortableNote");
            _settingsPathNote = N<TextBlock>("SettingsPathNote");
            _engineNote = N<TextBlock>("EngineNote");
            _readinessBtn = N<Button>("ReadinessBtn");
            _readinessStatus = N<TextBlock>("ReadinessStatus");
            _readinessList = N<StackPanel>("ReadinessList");
            _readinessHint = N<TextBlock>("ReadinessHint");
            _endpointsBtn = N<Button>("EndpointsBtn");
            _endpointsStatus = N<TextBlock>("EndpointsStatus");
            _endpointsList = N<ListBox>("EndpointsList");
            _useBestBtn = N<Button>("UseBestBtn");
            _useSelectedBtn = N<Button>("UseSelectedBtn");
            _endpointNote = N<TextBlock>("EndpointNote");
            _aboutUi = N<TextBlock>("AboutUi");
            _aboutTunnel = N<TextBlock>("AboutTunnel");
            _aboutRepo = N<TextBlock>("AboutRepo");
            _aboutSource = N<TextBlock>("AboutSource");
            _aboutUpdate = N<TextBlock>("AboutUpdate");
            _aboutStatus = N<TextBlock>("AboutStatus");
            _aboutLink = N<TextBlock>("AboutLink");
            _updateBtn = N<Button>("UpdateBtn");
            _bundleBtn = N<Button>("BundleBtn");
        }

        private void LoadValues()
        {
            _languageTr.IsChecked = _settings.Language != "en";
            _languageEn.IsChecked = _settings.Language == "en";
            _switchTray.IsChecked = _settings.TrayEnabled;
            _switchNotify.IsChecked = _settings.Notifications;
            _switchMinimize.IsChecked = _settings.MinimizeToTray;
            _switchAutoHeal.IsChecked = _settings.AutoHeal;
            _switchNotify.IsEnabled = _settings.TrayEnabled;

            _subtitle.Text = "WarpCord · " + AppInfo.Version;
            _portableNote.Text = Paths.IsPortable
                ? L10n.T("Açık: ayarlar ve motor exe'nin yanında tutulur.") + "  (" + Paths.AppDir + ")"
                : L10n.T("Kapalı: ayarlar %APPDATA%\\WarpCord altında tutulur.") + "  (" + Paths.SettingsPath + ")";

            _settingsPathNote.Text = L10n.T("Ayarlar dosyası") + ": " + Paths.SettingsPath;
            _engineNote.Text = L10n.T("Motor") + ": " + (Paths.RepoRoot ?? "—");
            _aboutUi.Text = AppInfo.Version;
            _aboutTunnel.Text = _snapshot != null && !string.IsNullOrEmpty(_snapshot.Version)
                ? _snapshot.Version + " · " + _snapshot.ModeName
                : "—";
            _aboutRepo.Text = RepoUrl;
            _aboutSource.Text = (Paths.IsEmbedded ? L10n.T("gömülü motor") : L10n.T("klasör")) + " · " + (Paths.RepoRoot ?? "—");
            _aboutUpdate.Text = string.IsNullOrEmpty(_settings.LatestVersion)
                ? "—"
                : _settings.LatestVersion;

            _endpointsStatus.Text = "";
            _readinessStatus.Text = "";
            _readinessHint.Text = "";
            _endpointNote.Text = "";
        }

        private void AttachEvents()
        {
            _settingsClose.Click += delegate { SaveAndClose(); };
            KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Escape) SaveAndClose();
            };

            _tabGeneral.Checked += delegate { ShowPanel(0); };
            _tabReadiness.Checked += delegate { ShowPanel(1); };
            _tabEndpoints.Checked += delegate { ShowPanel(2); };
            _tabAbout.Checked += delegate { ShowPanel(3); };

            _languageTr.Checked += delegate { SetLanguage("tr"); };
            _languageEn.Checked += delegate { SetLanguage("en"); };

            _switchTray.Checked += ToggleChanged;
            _switchTray.Unchecked += ToggleChanged;
            _switchNotify.Checked += ToggleChanged;
            _switchNotify.Unchecked += ToggleChanged;
            _switchMinimize.Checked += ToggleChanged;
            _switchMinimize.Unchecked += ToggleChanged;
            _switchAutoHeal.Checked += ToggleChanged;
            _switchAutoHeal.Unchecked += ToggleChanged;

            _readinessBtn.Click += delegate { RunReadiness(); };
            _endpointsBtn.Click += delegate { RunEndpoints(); };
            _useBestBtn.Click += delegate { UseBestEndpoint(); };
            _useSelectedBtn.Click += delegate { UseSelectedEndpoint(); };
            _updateBtn.Click += delegate { CheckVersion(); };
            _bundleBtn.Click += delegate { MakeBundle(); };
            _aboutLink.MouseLeftButtonUp += delegate { OpenUrl(RepoUrl); };
        }

        private void ShowPanel(int index)
        {
            _panelGeneral.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed;
            _panelReadiness.Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed;
            _panelEndpoints.Visibility = index == 2 ? Visibility.Visible : Visibility.Collapsed;
            _panelAbout.Visibility = index == 3 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void SetLanguage(string language)
        {
            if (_settings.Language == language) return;
            _settings.Language = language;
            L10n.Lang = language;
            L10n.Apply(_root);
            Title = L10n.T("Ayarlar");
            LoadValues();
            if (_onChanged != null) _onChanged();
        }

        private void ToggleChanged(object sender, RoutedEventArgs e)
        {
            _settings.TrayEnabled = _switchTray.IsChecked == true;
            _settings.Notifications = _switchNotify.IsChecked == true;
            _settings.MinimizeToTray = _switchMinimize.IsChecked == true;
            _settings.AutoHeal = _switchAutoHeal.IsChecked == true;
            _switchNotify.IsEnabled = _settings.TrayEnabled;
            if (_onChanged != null) _onChanged();
        }

        // ------------------------------------------------------------- readiness
        private void RunReadiness()
        {
            if (_busy) return;
            string script = ScriptHost.ToolPath("warpreadiness.ps1");
            if (script == null)
            {
                _readinessStatus.Text = L10n.T("Betik bulunamadı: warpreadiness.ps1");
                return;
            }

            _busy = true;
            _readinessBtn.IsEnabled = false;
            _readinessStatus.Text = L10n.T("Denetleniyor…");
            _readinessList.Children.Clear();

            Task.Run(delegate
            {
                string output = null;
                string error = null;
                try { output = ScriptHost.Run(script, "", 60000); }
                catch (Exception ex) { error = ex.Message; }

                Dispatcher.BeginInvoke(new Action(delegate
                {
                    _busy = false;
                    _readinessBtn.IsEnabled = true;
                    if (error != null)
                    {
                        _readinessStatus.Text = L10n.T("Denetim başarısız") + ": " + error;
                        return;
                    }
                    RenderReadiness(Json.Parse(output));
                }));
            });
        }

        private void RenderReadiness(object node)
        {
            _readinessList.Children.Clear();
            if (node == null)
            {
                _readinessStatus.Text = L10n.T("Denetim başarısız");
                return;
            }

            bool admin = Json.Bool(node, false, "admin");
            string language = Json.Str(node, "language") ?? "?";
            string policy = Json.Str(node, "policy") ?? "?";
            bool gpo = Json.Bool(node, false, "gpo");
            bool defender = Json.Bool(node, false, "defender");
            bool realtime = Json.Bool(node, false, "realtime");
            int asr = Json.Int(node, 0, "asr");
            int cfa = Json.Int(node, -1, "cfa");
            string sac = (Json.Str(node, "sac") ?? "unknown").ToLowerInvariant();
            bool wdac = Json.Bool(node, false, "wdac");
            int motw = Json.Int(node, 0, "motw");

            var hints = new List<string>();

            AddReadinessRow(L10n.T("Yönetici"), admin ? L10n.T("var") : L10n.T("yok"), admin ? Palette.Green : Palette.Amber);
            if (!admin) hints.Add("Kurulum ve onarım için UAC ile yönetici izni gerekir.");

            AddReadinessRow(L10n.T("Dil modu"), language,
                language.Equals("FullLanguage", StringComparison.OrdinalIgnoreCase) ? Palette.Green : Palette.Red);
            if (!language.Equals("FullLanguage", StringComparison.OrdinalIgnoreCase))
                hints.Add("PowerShell kısıtlı dil modunda; betikler çalışmaz.");

            AddReadinessRow(L10n.T("Yürütme politikası"), policy, Palette.Dim);
            AddReadinessRow(L10n.T("GPO kısıtı"), gpo ? L10n.T("var") : L10n.T("yok"), gpo ? Palette.Amber : Palette.Green);

            AddReadinessRow(L10n.T("Defender"), defender ? L10n.T("var") : L10n.T("yok"), Palette.Dim);
            AddReadinessRow(L10n.T("Gerçek zamanlı koruma"), realtime ? L10n.T("açık") : L10n.T("kapalı"),
                realtime ? Palette.Amber : Palette.Green);
            AddReadinessRow(L10n.T("ASR kuralı"), asr.ToString(), asr > 0 ? Palette.Amber : Palette.Green);
            if (asr > 0) hints.Add("ASR kuralı indirilen içeriği engelleyebilir; kurulum istisna ekler.");

            AddReadinessRow(L10n.T("Korumalı klasörler"), cfa == 1 ? L10n.T("açık") : L10n.T("kapalı"),
                cfa == 1 ? Palette.Amber : Palette.Green);

            Brush sacDot = sac == "off" ? Palette.Green : (sac == "on" ? Palette.Red : Palette.Amber);
            AddReadinessRow(L10n.T("Smart App Control"), sac == "off" ? L10n.T("kapalı (iyi)") : sac, sacDot);
            if (sac == "on")
                hints.Add("Smart App Control açık: imzasız WarpCord.exe ve sing-box.exe çalışmaz. Kapatmadan kurulum olmaz.");
            else if (sac == "evaluation")
                hints.Add("Smart App Control değerlendirme modunda; imzasız bileşenler engellenebilir.");

            AddReadinessRow(L10n.T("WDAC/UMCI"), wdac ? L10n.T("var") : L10n.T("yok"), wdac ? Palette.Red : Palette.Green);
            if (wdac) hints.Add("WDAC/UMCI imzasız betik ve ikiliyi engelliyor; politika kaldırılmalı.");

            AddReadinessRow(L10n.T("Mark-of-the-web"), motw.ToString(), motw > 0 ? Palette.Amber : Palette.Green);

            _readinessStatus.Text = hints.Count == 0
                ? L10n.T("Bu makinede kurulumu engelleyen bir şey görünmüyor.")
                : L10n.F("{0} uyarı var.", hints.Count);
            _readinessHint.Text = string.Join("\n", hints.ToArray());
        }

        private void AddReadinessRow(string label, string value, Brush dot)
        {
            var grid = new Grid { Height = 26, Margin = new Thickness(0, 0, 0, 1) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var name = new TextBlock
            {
                Text = label,
                FontSize = 11.5,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Palette.Faint
            };
            var text = new TextBlock
            {
                Text = value,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = Palette.Text
            };
            Grid.SetColumn(text, 1);
            var point = new Ellipse { Width = 6, Height = 6, Fill = dot, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 2, 0) };
            Grid.SetColumn(point, 2);

            grid.Children.Add(name);
            grid.Children.Add(text);
            grid.Children.Add(point);
            _readinessList.Children.Add(grid);
        }

        // ------------------------------------------------------------- endpoints
        private void RunEndpoints()
        {
            if (_busy) return;
            string script = ScriptHost.ToolPath("warpendpoints.ps1");
            if (script == null)
            {
                _endpointsStatus.Text = L10n.T("Betik bulunamadı: warpendpoints.ps1");
                return;
            }

            _busy = true;
            _endpointsBtn.IsEnabled = false;
            _endpointsStatus.Text = L10n.T("Uçlar deneniyor… (birkaç saniye)");
            _endpointRows = new List<EndpointRow>();
            _endpointsList.ItemsSource = null;

            Task.Run(delegate
            {
                string output = null;
                string error = null;
                try { output = ScriptHost.Run(script, "-MaxCount 8 -TimeoutSec 4", 120000); }
                catch (Exception ex) { error = ex.Message; }

                Dispatcher.BeginInvoke(new Action(delegate
                {
                    _busy = false;
                    _endpointsBtn.IsEnabled = true;
                    if (error != null)
                    {
                        _endpointsStatus.Text = L10n.T("Denetim başarısız") + ": " + error;
                        return;
                    }
                    RenderEndpoints(output);
                }));
            });
        }

        private void RenderEndpoints(string json)
        {
            _endpointRows = new List<EndpointRow>();
            var list = Json.Parse(json) as System.Collections.IList;
            if (list != null)
            {
                foreach (object item in list)
                {
                    bool ok = Json.Bool(item, false, "ok");
                    int ms = Json.Int(item, -1, "ms");
                    string address = Json.Str(item, "address") ?? "?";
                    int port = Json.Int(item, 0, "port");
                    _endpointRows.Add(new EndpointRow
                    {
                        Address = address,
                        Port = port,
                        Ok = ok,
                        Ms = ms,
                        Text = address + ":" + port,
                        Latency = ok ? ms + " ms" : "—",
                        Dot = ok ? Palette.Green : Palette.Red
                    });
                }
            }

            _endpointRows = Rank(_endpointRows);

            _endpointsList.ItemsSource = _endpointRows;
            if (_endpointRows.Count > 0) _endpointsList.SelectedIndex = 0;

            int working = _endpointRows.Count(r => r.Ok);
            _endpointsStatus.Text = string.Format("{0}/{1} {2}", working, _endpointRows.Count, L10n.T("uç yanıt verdi"));
            _endpointNote.Text = working == 0
                ? L10n.T("Hiçbiri yanıt vermedi; DPI motoru daha uygun olabilir.")
                : "";
        }

        private void UseBestEndpoint()
        {
            var best = _endpointRows.Where(r => r.Ok).OrderBy(r => r.Ms).FirstOrDefault();
            if (best == null) { _endpointNote.Text = L10n.T("Çalışan uç yok."); return; }
            ApplyEndpoint(best);
        }

        private void UseSelectedEndpoint()
        {
            var row = _endpointsList.SelectedItem as EndpointRow;
            if (row == null) { _endpointNote.Text = L10n.T("Önce listeden bir uç seç."); return; }
            ApplyEndpoint(row);
        }

        private void ApplyEndpoint(EndpointRow row)
        {
            _settings.Endpoint = row.Address + ":" + row.Port;
            _endpointNote.Text = L10n.F("{0} seçildi · Yeniden Bağlan ile uygulanır.", row.Text);
            if (_onChanged != null) _onChanged();
        }

        // ----------------------------------------------------------------- about
        private async void CheckVersion()
        {
            _busy = true;
            _updateBtn.IsEnabled = false;
            _aboutStatus.Text = L10n.T("Denetleniyor…");
            try
            {
                string url = RepoUrl.Replace("github.com", "api.github.com/repos") + "/releases/latest";
                using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    request.Headers.TryAddWithoutValidation("User-Agent", "WarpCord/" + AppInfo.Version);
                    request.Headers.TryAddWithoutValidation("Accept", "application/vnd.github+json");
                    using (HttpResponseMessage response = await Http.SendAsync(request))
                    {
                        string body = await response.Content.ReadAsStringAsync();
                        if (!response.IsSuccessStatusCode)
                        {
                            _aboutStatus.Text = L10n.T("Denetim başarısız") + " · HTTP " + (int)response.StatusCode;
                            return;
                        }
                        object node = Json.Parse(body);
                        string tag = Json.Str(node, "tag_name") ?? "";
                        string name = Json.Str(node, "name") ?? tag;
                        string latest = (tag ?? "").TrimStart('v', 'V');
                        _settings.LastVersionCheck = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
                        _settings.LatestVersion = string.IsNullOrEmpty(latest) ? tag : "v" + latest;
                        _aboutUpdate.Text = _settings.LatestVersion;
                        _aboutLink.Visibility = Visibility.Visible;

                        int comparison = VersionUtil.Compare(latest, AppInfo.Version);
                        _aboutStatus.Text = comparison > 0
                            ? L10n.F("{0} yayında: {1}", L10n.T("Yeni sürüm var"), name)
                            : L10n.T("Güncel") + " · " + (string.IsNullOrEmpty(name) ? tag : name);
                    }
                }
            }
            catch (Exception ex)
            {
                _aboutStatus.Text = L10n.T("Denetim başarısız") + ": " + ex.Message;
            }
            finally
            {
                _busy = false;
                _updateBtn.IsEnabled = true;
            }
        }


        private async void MakeBundle()
        {
            _busy = true;
            _bundleBtn.IsEnabled = false;
            _aboutStatus.Text = L10n.T("Tanı paketi hazırlanıyor…");
            string script = Paths.DiagnoseScript;
            if (script == null)
            {
                _aboutStatus.Text = L10n.T("Betik bulunamadı: tani.ps1");
                _busy = false;
                _bundleBtn.IsEnabled = true;
                return;
            }

            string folder = Path.GetTempPath();
            string result = null;
            string error = null;
            await Task.Run(delegate
            {
                try { ScriptHost.Run(script, "-Bundle -NoElevate", 300000); }
                catch (Exception ex) { error = ex.Message; }
            });

            if (error != null)
            {
                _aboutStatus.Text = L10n.T("Denetim başarısız") + ": " + error;
            }
            else
            {
                try
                {
                    var newest = new DirectoryInfo(folder).GetFiles("WarpCord-tani-*.zip")
                        .OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault();
                    if (newest != null)
                    {
                        result = newest.FullName;
                        Process.Start("explorer.exe", "/select,\"" + result + "\"");
                        _aboutStatus.Text = L10n.T("Tanı paketi hazır") + ": " + result;
                    }
                    else
                    {
                        _aboutStatus.Text = L10n.T("Paket bulunamadı; tani.bat -Bundle çıktısına bak.");
                    }
                }
                catch (Exception ex)
                {
                    _aboutStatus.Text = ex.Message;
                }
            }

            _busy = false;
            _bundleBtn.IsEnabled = true;
        }

        private static void OpenUrl(string url)
        {
            try
            {
                var info = new ProcessStartInfo(url) { UseShellExecute = true };
                Process.Start(info);
            }
            catch { }
        }

        private void SaveAndClose()
        {
            _settings.Save();
            if (_onChanged != null) _onChanged();
            Close();
        }

        /// <summary>Draws the settings window to a PNG (docs and visual checks).</summary>
        public static void RenderTo(string path, double scale)
        {
            Grid root = Xaml.Load<Grid>("WarpCord.SettingsWindow.xaml");
            var card = new Border
            {
                Background = WindowFx.BrushFromResource("PanelBrush", Brushes.DimGray),
                BorderBrush = WindowFx.BrushFromResource("StrokeBrush", Brushes.Gray),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Child = root
            };

            PopulatePreview(root);

            card.Measure(new Size(762, double.PositiveInfinity));
            card.Arrange(new Rect(0, 0, 762, card.DesiredSize.Height));
            card.UpdateLayout();

            double height = card.ActualHeight > 0 ? card.ActualHeight : card.DesiredSize.Height;
            if (scale <= 0) scale = 1;

            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
                (int)Math.Round(762 * scale), (int)Math.Round(height * scale),
                96 * scale, 96 * scale, PixelFormats.Pbgra32);
            bitmap.Render(card);

            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            string full = Path.GetFullPath(path);
            string dir = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            using (var stream = File.Create(full)) encoder.Save(stream);
        }
        /// <summary>Fills the preview render with the saved settings instead of empty controls.</summary>
        private static void PopulatePreview(Grid root)
        {
            var settings = Settings.Load();
            L10n.Lang = settings.Language;

            var subtitle = root.FindName("SettingsSubtitle") as TextBlock;
            if (subtitle != null) subtitle.Text = "WarpCord · " + AppInfo.Version;

            SetChecked(root, "LanguageTr", settings.Language != "en");
            SetChecked(root, "LanguageEn", settings.Language == "en");
            SetChecked(root, "SwitchTray", settings.TrayEnabled);
            SetChecked(root, "SwitchNotify", settings.Notifications);
            SetChecked(root, "SwitchMinimize", settings.MinimizeToTray);
            SetChecked(root, "SwitchAutoHeal", settings.AutoHeal);

            var portable = root.FindName("PortableNote") as TextBlock;
            if (portable != null)
            {
                portable.Text = Paths.IsPortable
                    ? L10n.T("Açık: ayarlar ve motor exe'nin yanında tutulur.")
                    : L10n.T("Kapalı: ayarlar %APPDATA%\\WarpCord altında tutulur.");
            }

            var path = root.FindName("SettingsPathNote") as TextBlock;
            if (path != null) path.Text = L10n.T("Ayarlar dosyası") + ": " + Paths.SettingsPath;
            var engine = root.FindName("EngineNote") as TextBlock;
            if (engine != null) engine.Text = L10n.T("Motor") + ": " + (Paths.RepoRoot ?? "—");

            L10n.Apply(root);
        }

/// <summary>Working endpoints first, then by latency; dead ones last.</summary>
        internal static List<EndpointRow> Rank(IEnumerable<EndpointRow> rows)
        {
            return rows
                .OrderByDescending(r => r.Ok)
                .ThenBy(r => r.Ms <= 0 ? int.MaxValue : r.Ms)
                .ToList();
        }
        private static void SetChecked(Grid root, string name, bool value)
        {
            var toggle = root.FindName(name) as System.Windows.Controls.Primitives.ToggleButton;
            if (toggle != null) toggle.IsChecked = value;
        }
    }
}