using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Ellipse = System.Windows.Shapes.Ellipse;

namespace WarpCord
{
    internal enum ActionKind
    {
        Install,
        Heal,
        Status,
        Diagnose,
        Uninstall
    }

    internal sealed class MainWindow : Window
    {
        private const string GuiVersion = "1.2.0";

        private readonly Settings _settings;
        private readonly CliOptions _cli;
        private readonly Runner _runner = new Runner();
        private readonly ObservableCollection<StatRow> _rows = new ObservableCollection<StatRow>();
        private readonly Dictionary<string, StatRow> _index = new Dictionary<string, StatRow>();
        private readonly DispatcherTimer _timer = new DispatcherTimer();
        private readonly DispatcherTimer _netTimer = new DispatcherTimer();
        private Storyboard _spin;

        private Grid _root;
        private Border _rootBorder;
        private Border _statusPill;
        private Border _runPill;
        private Border _adminBanner;
        private Border _sacBanner;
        private TextBlock _sacBannerText;
        private Button _sacHelpBtn;
        private Grid _titleBar;
        private Grid _runStepHost;
        private Grid _spinner;
        private StackPanel _advancedPanel;
        private Button _primary;
        private Button _statusBtn;
        private Button _healBtn;
        private Button _diagBtn;
        private Button _uninstallBtn;
        private Button _refreshBtn;
        private Button _cancelBtn;
        private Button _minBtn;
        private Button _maxBtn;
        private Button _closeBtn;
        private Button _adminFixBtn;
        private Button _advancedToggle;
        private Button _copyBtn;
        private Button _clearBtn;
        private Button _folderBtn;
        private Button _tunnelLogBtn;
        private Button _healLogBtn;
        private Image _titleLogo;
        private Image _heroLogo;
        private TextBlock _statusText;
        private TextBlock _statusDetail;
        private TextBlock _updatedText;
        private TextBlock _runPillText;
        private TextBlock _runStep;
        private TextBlock _adminText;
        private TextBlock _adminBannerText;
        private TextBlock _repoText;
        private TextBlock _versionText;
        private TextBlock _advancedLabel;
        private TextBlock _consoleEmpty;
        private Ellipse _statusDot;
        private Ellipse _runPillDot;
        private Ellipse _adminDot;
        private System.Windows.Shapes.Path _maxIcon;
        private System.Windows.Shapes.Path _advancedChevron;
        private RotateTransform _spinnerRotate;
        private RotateTransform _chevronRotate;
        private RichTextBox _consoleBox;
        private ItemsControl _statsList;
        private CheckBox _autoscroll;
        private CheckBox _force;
        private CheckBox _keepDns;
        private RadioButton _modeAuto;
        private RadioButton _modeWarp;
        private RadioButton _modeDpi;
        private RadioButton _methodApp;
        private RadioButton _methodManual;
        private TextBlock _methodNote;
        private Button _manualInstallBtn;
        private TextBox _endpoint;

        private ScrollViewer _leftScroll;
        private ConsoleWriter _console;
        private Snapshot _snap;
        private bool _busy;
        private bool _probing;
        private bool _initializing = true;
        private bool _renderMode;

        public MainWindow(Settings settings, CliOptions cli)
        {
            _settings = settings;
            _cli = cli;
            _renderMode = cli.Render != null;

            BuildWindow();
            BindElements();
            _console = new ConsoleWriter(_consoleBox);
            InitRows();
            LoadSettingsIntoUi();
            AttachEvents();

            _spin = CreateSpinner();
            _timer.Interval = TimeSpan.FromSeconds(3);
            _timer.Tick += OnTimerTick;
            _netTimer.Interval = TimeSpan.FromSeconds(20);
            _netTimer.Tick += OnNetTimerTick;

            Loaded += OnLoaded;
            Closing += OnClosing;
            StateChanged += OnStateChanged;
            _initializing = false;
        }

        // ============================================================ construction
        private void BuildWindow()
        {
            _root = Xaml.Load<Grid>("WarpCord.MainWindow.xaml");
            Content = _root;

            Title = "WarpCord";
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.CanResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = Res<Brush>("BgBrush", Brushes.Black);
            Foreground = Res<Brush>("TextBrush", Brushes.White);
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
            MinWidth = 900;
            MinHeight = 660;
            Icon = Assets.Logo256;
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
            TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);

            WindowFx.Apply(this);
        }

        private static T Res<T>(string key, T fallback) where T : class
        {
            if (Application.Current == null) return fallback;
            return Application.Current.Resources[key] as T ?? fallback;
        }

        private T N<T>(string name) where T : class
        {
            var element = _root.FindName(name) as T;
            if (element == null) throw new InvalidOperationException("Arayüz öğesi bulunamadı: " + name);
            return element;
        }

        private void BindElements()
        {
            _rootBorder = N<Border>("RootBorder");
            _leftScroll = N<ScrollViewer>("LeftScroll");
            _titleBar = N<Grid>("TitleBar");
            _titleLogo = N<Image>("TitleLogo");
            _heroLogo = N<Image>("HeroLogo");
            _minBtn = N<Button>("MinBtn");
            _maxBtn = N<Button>("MaxBtn");
            _closeBtn = N<Button>("CloseBtn");
            _maxIcon = N<System.Windows.Shapes.Path>("MaxIcon");
            _adminBanner = N<Border>("AdminBanner");
            _adminBannerText = N<TextBlock>("AdminBannerText");
            _adminFixBtn = N<Button>("AdminFixBtn");
            _sacBanner = N<Border>("SacBanner");
            _sacBannerText = N<TextBlock>("SacBannerText");
            _sacHelpBtn = N<Button>("SacHelpBtn");
            _statusPill = N<Border>("StatusPill");
            _statusDot = N<Ellipse>("StatusDot");
            _statusText = N<TextBlock>("StatusText");
            _statusDetail = N<TextBlock>("StatusDetail");
            _primary = N<Button>("PrimaryButton");
            _statusBtn = N<Button>("StatusBtn");
            _healBtn = N<Button>("HealBtn");
            _diagBtn = N<Button>("DiagBtn");
            _advancedToggle = N<Button>("AdvancedToggle");
            _advancedLabel = N<TextBlock>("AdvancedLabel");
            _advancedChevron = N<System.Windows.Shapes.Path>("AdvancedChevron");
            _chevronRotate = N<RotateTransform>("ChevronRotate");
            _advancedPanel = N<StackPanel>("AdvancedPanel");
            _modeAuto = N<RadioButton>("ModeAuto");
            _modeWarp = N<RadioButton>("ModeWarp");
            _modeDpi = N<RadioButton>("ModeDpi");
            _methodApp = N<RadioButton>("MethodApp");
            _methodManual = N<RadioButton>("MethodManual");
            _methodNote = N<TextBlock>("MethodNote");
            _manualInstallBtn = N<Button>("ManualInstallBtn");
            _force = N<CheckBox>("ForceCheck");
            _keepDns = N<CheckBox>("KeepDnsCheck");
            _endpoint = N<TextBox>("EndpointBox");
            _uninstallBtn = N<Button>("UninstallBtn");
            _statsList = N<ItemsControl>("StatsList");
            _updatedText = N<TextBlock>("UpdatedText");
            _refreshBtn = N<Button>("RefreshBtn");
            _runPill = N<Border>("RunPill");
            _runPillDot = N<Ellipse>("RunPillDot");
            _runPillText = N<TextBlock>("RunPillText");
            _copyBtn = N<Button>("CopyBtn");
            _clearBtn = N<Button>("ClearBtn");
            _autoscroll = N<CheckBox>("AutoscrollCheck");
            _runStepHost = N<Grid>("RunStepHost");
            _spinner = N<Grid>("Spinner");
            _spinnerRotate = N<RotateTransform>("SpinnerRotate");
            _runStep = N<TextBlock>("RunStep");
            _cancelBtn = N<Button>("CancelBtn");
            _consoleBox = N<RichTextBox>("ConsoleBox");
            _consoleEmpty = N<TextBlock>("ConsoleEmpty");
            _adminDot = N<Ellipse>("AdminDot");
            _adminText = N<TextBlock>("AdminText");
            _repoText = N<TextBlock>("RepoText");
            _versionText = N<TextBlock>("VersionText");
            _folderBtn = N<Button>("FolderBtn");
            _tunnelLogBtn = N<Button>("TunnelLogBtn");
            _healLogBtn = N<Button>("HealLogBtn");

            _titleLogo.Source = Assets.Logo32;
            _heroLogo.Source = Assets.Logo256;
        }

        private void InitRows()
        {
            AddRow("mode", "Mod");
            AddRow("endpoint", "Uç");
            AddRow("tun", "TUN");
            AddRow("singbox", "sing-box");
            AddRow("proxy", "Proxy");
            AddRow("heal", "Onarım");
            AddRow("dns", "DNS");
            AddRow("api", "Discord API");
            AddRow("ip", "Dış IP");
            _statsList.ItemsSource = _rows;
        }

        private void AddRow(string key, string label)
        {
            var row = new StatRow(label);
            _index[key] = row;
            _rows.Add(row);
        }

        private void LoadSettingsIntoUi()
        {
            Width = Math.Max(MinWidth, _settings.Width);
            Height = Math.Max(MinHeight, _settings.Height);

            if (!_renderMode && _settings.HasBounds && IsOnScreen(_settings.Left, _settings.Top))
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = _settings.Left;
                Top = _settings.Top;
            }

            _modeAuto.IsChecked = _settings.Mode == "auto";
            _modeWarp.IsChecked = _settings.Mode == "warp";
            _modeDpi.IsChecked = _settings.Mode == "dpi";
            _methodApp.IsChecked = _settings.InstallMethod != "manual";
            _methodManual.IsChecked = _settings.InstallMethod == "manual";
            SetMethodUi();
            _force.IsChecked = _settings.Force;
            _keepDns.IsChecked = _settings.KeepDns;
            _endpoint.Text = _settings.Endpoint ?? "";
            _autoscroll.IsChecked = _settings.Autoscroll;
            _console.Autoscroll = _settings.Autoscroll;

            SetAdvanced(_settings.Advanced);

            _repoText.Text = Paths.RepoRoot ?? (Paths.Installed ? Paths.InstallDir : "WarpCord klasörü bulunamadı");
            _versionText.Text = "arayüz " + GuiVersion;
            UpdateAdminUi();
            UpdateSacUi();

            if (!_renderMode && _settings.Maximized) WindowState = WindowState.Maximized;
        }

        private static bool IsOnScreen(double left, double top)
        {
            return left > SystemParameters.VirtualScreenLeft - 40 &&
                   top > SystemParameters.VirtualScreenTop - 40 &&
                   left < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 120 &&
                   top < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 120;
        }

        private void AttachEvents()
        {
            _primary.Click += Primary_Click;
            _statusBtn.Click += Status_Click;
            _healBtn.Click += Heal_Click;
            _diagBtn.Click += Diag_Click;
            _uninstallBtn.Click += Uninstall_Click;
            _advancedToggle.Click += Advanced_Click;
            _refreshBtn.Click += Refresh_Click;
            _copyBtn.Click += Copy_Click;
            _clearBtn.Click += Clear_Click;
            _cancelBtn.Click += Cancel_Click;
            _folderBtn.Click += Folder_Click;
            _tunnelLogBtn.Click += TunnelLog_Click;
            _healLogBtn.Click += HealLog_Click;
            _adminFixBtn.Click += AdminFix_Click;
            _sacHelpBtn.Click += SacHelp_Click;
            _minBtn.Click += Min_Click;
            _maxBtn.Click += Max_Click;
            _closeBtn.Click += Close_Click;
            _titleBar.MouseLeftButtonDown += TitleBar_MouseLeftButtonDown;

            _modeAuto.Checked += Mode_Checked;
            _modeWarp.Checked += Mode_Checked;
            _modeDpi.Checked += Mode_Checked;

            _methodApp.Checked += Method_Checked;
            _methodManual.Checked += Method_Checked;
            _manualInstallBtn.Click += ManualInstall_Click;

            _force.Checked += Option_Changed;
            _force.Unchecked += Option_Changed;
            _keepDns.Checked += Option_Changed;
            _keepDns.Unchecked += Option_Changed;
            _autoscroll.Checked += Autoscroll_Changed;
            _autoscroll.Unchecked += Autoscroll_Changed;
            _endpoint.TextChanged += Endpoint_Changed;

            _runner.Line += delegate(LogLine line)
            {
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    _console.Write(line);
                    SyncEmpty();
                }));
            };
            _runner.StepChanged += delegate(string step)
            {
                Dispatcher.BeginInvoke(new Action(delegate { _runStep.Text = step; }));
            };
            _runner.Finished += delegate(int code, TimeSpan elapsed)
            {
                Dispatcher.BeginInvoke(new Action(delegate { OnRunFinished(code, elapsed); }));
            };
        }

        private Storyboard CreateSpinner()
        {
            try
            {
                var animation = new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1.1))
                {
                    RepeatBehavior = RepeatBehavior.Forever
                };
                Storyboard.SetTarget(animation, _spinnerRotate);
                Storyboard.SetTargetProperty(animation, new PropertyPath(RotateTransform.AngleProperty));
                var storyboard = new Storyboard();
                storyboard.Children.Add(animation);
                return storyboard;
            }
            catch
            {
                return null;
            }
        }

        // ================================================================= startup
        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (_renderMode)
            {
                BeginRender();
                return;
            }

            _timer.Start();
            _netTimer.Start();

            if (!string.IsNullOrEmpty(_cli.RunAction)) StartActionByName(_cli.RunAction);
            else FirstRunCheckAsync();
        }

        /// <summary>
        /// On a machine without WarpCord, ask once whether the user wants the app
        /// to install it or to run the classic WarpCord.bat by hand.
        /// </summary>
        private async void FirstRunCheckAsync()
        {
            try
            {
                Snapshot snapshot = await ProbeAsync(true, true);
                if (snapshot == null || snapshot.Installed || _settings.AskedInstallMethod) return;
                AskInstallMethod();
            }
            catch (Exception ex)
            {
                CrashLog.Write(ex);
            }
        }

        private void AskInstallMethod()
        {
            InstallMethodChoice choice = InstallPrompt.Show(this);
            if (choice == InstallMethodChoice.Cancel) return;

            _settings.InstallMethod = choice == InstallMethodChoice.Manual ? "manual" : "app";
            _settings.AskedInstallMethod = true;
            if (choice == InstallMethodChoice.Manual) _methodManual.IsChecked = true;
            else _methodApp.IsChecked = true;
            SetMethodUi();
            _settings.Save();

            if (choice == InstallMethodChoice.Manual) LaunchManualInstall();
        }

        private void OnTimerTick(object sender, EventArgs e)
        {
            if (_busy || _probing) return;
            Task ignore = ProbeAsync(false, false);
        }

        private void OnNetTimerTick(object sender, EventArgs e)
        {
            if (_busy || _probing) return;
            Task ignore = ProbeAsync(true, false);
        }

        private async void BeginRender()
        {
            try
            {
                ShowInTaskbar = false;
                if (_cli.RenderWidth > 0) Width = _cli.RenderWidth;
                if (_cli.RenderHeight > 0) Height = _cli.RenderHeight;
                if (_cli.Demo) SeedDemoConsole();
                if (_cli.AdvancedPreview) SetAdvanced(true);

                await ProbeAsync(true, true);
                if (!string.IsNullOrEmpty(_cli.PreviewState) && _snap != null)
                    ApplySnapshot(PreviewState(_snap, _cli.PreviewState));
                await Task.Delay(80);
                UpdateLayout();
                SaveRender(_cli.Render, _cli.RenderScale <= 0 ? 2 : _cli.RenderScale);
            }
            catch (Exception ex)
            {
                CrashLog.Write(ex);
                Console.Error.WriteLine(ex);
            }
            finally
            {
                Application.Current.Shutdown();
            }
        }

        private void SeedDemoConsole()
        {
            string[] lines = Prober.Tail(Paths.InstallLogPath, 44);
            if (lines.Length == 0)
            {
                _console.Write("Örnek günlük bulunamadı (install.log yok).", Palette.Faint, false);
                SyncEmpty();
                return;
            }
            foreach (string line in lines)
            {
                // raw HTTP response headers add noise to a screenshot
                string trimmed = line.Trim();
                if (trimmed.StartsWith("Date:") || trimmed.StartsWith("Content-Type:") ||
                    trimmed.StartsWith("Content-Length") || trimmed.StartsWith("Transfer-Encoding:") ||
                    trimmed.StartsWith("Keep-Alive:") || trimmed.StartsWith("Connection:") ||
                    trimmed.StartsWith("Set-Cookie:") || trimmed.StartsWith("Via:") ||
                    trimmed.StartsWith("CF-") || trimmed.StartsWith("Server:"))
                    continue;
                _console.Write(new LogLine(Runner.Classify(line), line));
            }
            SyncEmpty();
        }

        private static Snapshot PreviewState(Snapshot s, string state)
        {
            switch ((state ?? "").ToLowerInvariant())
            {
                case "notinstalled":
                    s.Installed = false;
                    s.Version = null;
                    s.InstalledAt = null;
                    s.Mode = "";
                    s.SingBoxRunning = false;
                    s.FragRunning = false;
                    s.TunUp = false;
                    s.HealStatus = null;
                    s.Healthy = false;
                    break;

                case "broken":
                    s.SingBoxRunning = false;
                    s.SingBoxPid = 0;
                    s.TunUp = false;
                    s.DnsOk = false;
                    s.DnsPoisoned = true;
                    s.DnsText = "zehirlenmiş: 195.175.254.2";
                    s.DnsChecked = true;
                    s.ApiOk = false;
                    s.ApiText = "erişilemiyor · zaman aşımı";
                    s.ApiChecked = true;
                    s.HealStatus = "failed";
                    s.Healthy = false;
                    break;

                case "dpi":
                    s.Mode = "dpi";
                    s.FragPort = 1081;
                    s.FragRunning = true;
                    s.FragPid = 9124;
                    s.EndpointAddress = null;
                    s.Healthy = true;
                    break;
            }
            return s;
        }

        private void SaveRender(string path, double scale)
        {
            FrameworkElement element = _rootBorder;
            element.UpdateLayout();

            double width = element.ActualWidth;
            double height = element.ActualHeight;
            if (width < 10 || height < 10)
            {
                width = Width;
                height = Height;
                element.Measure(new Size(width, height));
                element.Arrange(new Rect(0, 0, width, height));
                element.UpdateLayout();
            }

            var bitmap = new RenderTargetBitmap(
                (int)Math.Round(width * scale), (int)Math.Round(height * scale),
                96 * scale, 96 * scale, PixelFormats.Pbgra32);
            bitmap.Render(element);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));

            string full = System.IO.Path.GetFullPath(path);
            string dir = System.IO.Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            using (var stream = File.Create(full)) encoder.Save(stream);

            if (_leftScroll != null)
            {
                Console.Error.WriteLine("layout: viewport=" + _leftScroll.ViewportHeight.ToString("0") +
                                        " extent=" + _leftScroll.ExtentHeight.ToString("0") +
                                        " scrollable=" + _leftScroll.ScrollableHeight.ToString("0"));
            }
        }

        // ================================================================ probing
        internal async Task<Snapshot> ProbeAsync(bool network, bool publicIp)
        {
            if (_probing) return _snap;
            _probing = true;
            Snapshot snapshot = null;
            try
            {
                snapshot = await Prober.CollectAsync(network, publicIp);
                ApplySnapshot(snapshot);
            }
            catch (Exception ex)
            {
                if (_snap == null) _snap = new Snapshot();
                _snap.Error = ex.Message;
                _console.Write("Durum okunamadı: " + ex.Message, Palette.Red, false);
                SyncEmpty();
            }
            finally
            {
                _probing = false;
            }
            return snapshot ?? _snap;
        }

        private void ApplySnapshot(Snapshot s)
        {
            _snap = s;

            if (!s.Installed) StateNotInstalled(s);
            else if (s.Healthy) StateHealthy(s);
            else StateProblem(s);

            UpdateRows(s);

            _updatedText.Text = "son " + DateTime.Now.ToString("HH:mm:ss");
            _versionText.Text = string.IsNullOrEmpty(s.Version)
                ? "arayüz " + GuiVersion
                : "WarpCord " + s.Version + " · arayüz " + GuiVersion;

            _statusBtn.IsEnabled = !_busy && Paths.StatusScript != null;
            _diagBtn.IsEnabled = !_busy && Paths.DiagnoseScript != null;
            _healBtn.IsEnabled = !_busy && s.Installed && Paths.HealScript != null;
            _uninstallBtn.IsEnabled = !_busy && s.Installed && Paths.UninstallScript != null;

            if (!_busy) UpdatePrimaryLabel();
        }

        private void StateNotInstalled(Snapshot s)
        {
            SetPill("KURULU DEĞİL", Color.FromRgb(0x66, 0x70, 0x8C));
            _statusDetail.Text =
                "WarpCord bu bilgisayarda kurulu değil. Kurulum: ücretsiz WARP cihazı kaydedilir, uçlar denenir, " +
                "çalışan motor seçilir ve tünel açılır.\nİlk kurulum 1–2 dakika sürer.";
        }

        private void StateHealthy(Snapshot s)
        {
            SetPill("SAĞLIKLI", Color.FromRgb(0x34, 0xD3, 0x99));
            string endpoint = s.DpiMode
                ? " · proxy portu " + s.FragPort
                : (string.IsNullOrEmpty(s.EndpointAddress) ? "" : " · uç " + s.EndpointAddress + ":" + s.EndpointPort);
            _statusDetail.Text = s.ModeName + endpoint + "\nDiscord API " + s.ApiMs + " ms · DNS temiz · " +
                                 (s.TunUp ? "TUN ayakta" : "TUN görünmüyor");
        }

        private void StateProblem(Snapshot s)
        {
            SetPill("SORUN VAR", Color.FromRgb(0xF8, 0x71, 0x71));
            var reasons = new List<string>();
            if (!s.SingBoxRunning) reasons.Add("sing-box çalışmıyor");
            if (s.DpiMode && !s.FragRunning) reasons.Add("DPI proxy çalışmıyor");
            if (s.DnsPoisoned) reasons.Add("DNS zehirli");
            else if (s.DnsChecked && !s.DnsOk) reasons.Add("DNS çözümlenemiyor");
            if (s.ApiChecked && !s.ApiOk) reasons.Add("Discord API yanıt vermiyor");
            if (!s.TunUp) reasons.Add("TUN adaptörü ayakta değil");
            if (reasons.Count == 0) reasons.Add("tünel doğrulanamadı");
            if (reasons.Count > 3) reasons.RemoveRange(3, reasons.Count - 3);

            _statusDetail.Text = "Sorun: " + string.Join(", ", reasons.ToArray()) +
                                 ".\nOnar düğmesi bozulan parçayı kendisi düzeltir.";
        }

        private void SetPill(string text, Color color)
        {
            var solid = new SolidColorBrush(color);
            solid.Freeze();
            var background = new SolidColorBrush(Color.FromArgb(0x16, color.R, color.G, color.B));
            background.Freeze();
            var border = new SolidColorBrush(Color.FromArgb(0x38, color.R, color.G, color.B));
            border.Freeze();

            _statusText.Text = text;
            _statusText.Foreground = solid;
            _statusDot.Fill = solid;
            _statusPill.Background = background;
            _statusPill.BorderBrush = border;
        }

        private void UpdateRows(Snapshot s)
        {
            if (!s.Installed)
            {
                Row("mode", "—", Palette.Faint);
                Row("endpoint", "—", Palette.Faint);
                Row("tun", "yok", Palette.Faint);
                Row("singbox", "kurulu değil", Palette.Faint);
                Row("proxy", "kurulu değil", Palette.Faint);
                Row("heal", "kurulu değil", Palette.Faint);
                Row("dns", s.DnsChecked ? s.DnsText : "—", s.DnsOk ? Palette.Green : Palette.Faint);
                Row("api", s.ApiChecked ? s.ApiText : "—", s.ApiOk ? Palette.Green : Palette.Faint);
                Row("ip", string.IsNullOrEmpty(s.PublicIp) ? "—" : s.PublicIp, Palette.Faint);
                return;
            }

            Row("mode", s.ModeName, s.DpiMode ? Palette.Cyan : Palette.Violet);

            string endpoint;
            if (s.DpiMode) endpoint = "127.0.0.1:" + s.FragPort + " (yerel proxy)";
            else if (!string.IsNullOrEmpty(s.EndpointAddress)) endpoint = s.EndpointAddress + ":" + s.EndpointPort;
            else endpoint = "—";
            Row("endpoint", endpoint,
                endpoint == "—" ? Palette.Faint : (s.DpiMode ? Palette.Cyan : Palette.Dim));

            Row("tun", s.TunUp ? "WarpCord · Up" : (s.TunName != null ? s.TunName + " · kapalı" : "yok"),
                s.TunUp ? Palette.Green : Palette.Amber);

            Row("singbox", s.SingBoxRunning ? "çalışıyor · PID " + s.SingBoxPid : "ÇALIŞMIYOR",
                s.SingBoxRunning ? Palette.Green : Palette.Red);

            if (s.DpiMode)
                Row("proxy", s.FragRunning ? "çalışıyor · PID " + s.FragPid : "ÇALIŞMIYOR",
                    s.FragRunning ? Palette.Green : Palette.Red);
            else
                Row("proxy", "gerekmiyor (WARP modu)", Palette.Faint);

            string heal;
            Brush healDot;
            if (string.IsNullOrEmpty(s.HealStatus))
            {
                heal = "henüz çalışmadı";
                healDot = Palette.Faint;
            }
            else
            {
                string when = FormatHealTime(s.HealLastRun);
                string label;
                if (string.Equals(s.HealStatus, "healthy", StringComparison.OrdinalIgnoreCase))
                {
                    label = "sağlıklı";
                    healDot = Palette.Green;
                }
                else if (string.Equals(s.HealStatus, "repaired", StringComparison.OrdinalIgnoreCase))
                {
                    label = "onarıldı";
                    healDot = Palette.Amber;
                }
                else if (string.Equals(s.HealStatus, "failed", StringComparison.OrdinalIgnoreCase))
                {
                    label = "sorun sürüyor";
                    healDot = Palette.Red;
                }
                else
                {
                    label = s.HealStatus;
                    healDot = Palette.Red;
                }
                heal = label + " · " + when;
            }
            Row("heal", heal, healDot);

            Row("dns", s.DnsChecked ? s.DnsText : "—", s.DnsOk ? Palette.Green : Palette.Red);
            Row("api", s.ApiChecked ? s.ApiText : "—", s.ApiOk ? Palette.Green : Palette.Red);
            Row("ip", string.IsNullOrEmpty(s.PublicIp) ? "—" : s.PublicIp,
                string.IsNullOrEmpty(s.PublicIp) ? Palette.Faint : Palette.Dim);
        }

        private void Row(string key, string value, Brush dot)
        {
            StatRow row;
            if (!_index.TryGetValue(key, out row)) return;
            row.Value = value;
            row.Dot = dot ?? Palette.Faint;
        }

        private static string FormatHealTime(string text)
        {
            DateTime parsed;
            if (!string.IsNullOrEmpty(text) &&
                DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
                return parsed.ToString("HH:mm");
            return text ?? "—";
        }

        private void UpdateAdminUi()
        {
            bool admin = Elevation.IsAdmin;
            _adminDot.Fill = admin ? Palette.Green : Palette.Amber;
            _adminText.Text = admin ? "yönetici" : "yönetici değil";
            _adminDot.ToolTip = admin ? "Yönetici olarak çalışıyor" : "Yönetici hakları yok";
            _adminBanner.Visibility = admin ? Visibility.Collapsed : Visibility.Visible;
            if (!admin)
                _adminBannerText.Text =
                    "Kurulum, onarım ve kaldırma yönetici hakları ister. Durum bilgisi yönetici olmadan da okunur.";
        }

        // ================================================================ actions
        private void UpdatePrimaryLabel()
        {
            if (_busy) return;
            string label;
            if (_snap == null) label = "Kontrol ediliyor…";
            else if (!_snap.Installed) label = ManualInstallSelected ? "Elle Kur (WarpCord.bat)" : "Kur ve Bağlan";
            else if (!_snap.Healthy) label = "Onar";
            else label = "Yeniden Bağlan";

            _primary.Content = label;
            if (label == "Onar")
                _primary.ToolTip = "Tüneli denetle ve bozulan parçayı onar";
            else if (label.StartsWith("Elle"))
                _primary.ToolTip = "Klasik WarpCord.bat kurulumunu ayrı bir pencerede başlat";
            else if (label == "Kur ve Bağlan")
                _primary.ToolTip = "Kur, çalışan motoru seç ve tüneli aç";
            else
                _primary.ToolTip = "Tüneli yeniden kur ve doğrula";
        }

        private bool ManualInstallSelected
        {
            get { return _settings.InstallMethod == "manual"; }
        }

        private void SetMethodUi()
        {
            bool manual = ManualInstallSelected;
            if (_methodNote != null)
            {
                _methodNote.Text = manual
                    ? "Kurulum, proje klasöründeki WarpCord.bat ile ayrı bir komut penceresinde çalışır. Bu pencere açık kalır ve durumu izler."
                    : "Kurulum bu pencerede çalışır; adımları canlı görürsün ve çıktı kaydın içinde kalır.";
            }
            if (_manualInstallBtn != null)
                _manualInstallBtn.Visibility = manual ? Visibility.Visible : Visibility.Collapsed;
            if (!_busy) UpdatePrimaryLabel();
        }

        private void Method_Checked(object sender, RoutedEventArgs e)
        {
            if (_initializing) return;
            _settings.InstallMethod = ReferenceEquals(sender, _methodManual) ? "manual" : "app";
            SetMethodUi();
        }

        private void ManualInstall_Click(object sender, RoutedEventArgs e)
        {
            if (_busy) return;
            LaunchManualInstall();
        }

        /// <summary>Runs the classic WarpCord.bat in its own elevated console window.</summary>
        private void LaunchManualInstall()
        {
            string bat = Paths.ManualInstallScript;
            _console.Separator("Elle kurulum · " + DateTime.Now.ToString("HH:mm:ss"));
            if (bat == null)
            {
                _console.Write("  WarpCord.bat bulunamadı. WarpCord.exe dosyasını proje klasörünün içinden çalıştırın.",
                    Palette.Red, false);
                _console.Write("  Aranan klasör: " +
                               (Paths.RepoRoot ?? "(bulunamadı, --repo ile belirtebilirsiniz)"), Palette.Faint, false);
                SyncEmpty();
                return;
            }

            try
            {
                var info = new ProcessStartInfo(bat);
                info.Verb = "runas";
                info.UseShellExecute = true;
                info.WorkingDirectory = Paths.RepoRoot;
                Process.Start(info);

                _console.Write("  WarpCord.bat ayrı bir pencerede başlatıldı:", Palette.Cyan, false);
                _console.Write("  " + bat, Palette.Faint, false);
                _console.Write("  Kurulum bitince durum kendiliğinden yenilenir; istersen Durum düğmesine bas.",
                    Palette.Faint, false);
                SyncEmpty();

                _runPill.Visibility = Visibility.Visible;
                _runPillText.Text = "elle";
                _runPillText.Foreground = Palette.Amber;
                _runPillDot.Fill = Palette.Amber;
                _runPill.Background = new SolidColorBrush(Color.FromArgb(0x16, 0xFB, 0xBF, 0x24));
                _runPill.BorderBrush = new SolidColorBrush(Color.FromArgb(0x38, 0xFB, 0xBF, 0x24));

                Later(20000, delegate { Task ignore = ProbeAsync(true, true); });
                Later(60000, delegate { Task ignore = ProbeAsync(true, true); });
            }
            catch (Win32Exception ex)
            {
                _console.Write(ex.NativeErrorCode == 1223
                        ? "  Yönetici izni verilmedi; elle kurulum başlatılmadı."
                        : "  WarpCord.bat başlatılamadı: " + ex.Message,
                    Palette.Amber, false);
                SyncEmpty();
            }
        }

        private void Primary_Click(object sender, RoutedEventArgs e)
        {
            if (_busy) return;
            bool installed = _snap != null && _snap.Installed;
            if (installed && !_snap.Healthy)
            {
                StartAction(ActionKind.Heal);
                return;
            }
            if (!installed && ManualInstallSelected)
            {
                LaunchManualInstall();
                return;
            }
            StartAction(ActionKind.Install);
        }

        private void Status_Click(object sender, RoutedEventArgs e)
        {
            StartAction(ActionKind.Status);
        }

        private void Heal_Click(object sender, RoutedEventArgs e)
        {
            StartAction(ActionKind.Heal);
        }

        private void Diag_Click(object sender, RoutedEventArgs e)
        {
            StartAction(ActionKind.Diagnose);
        }

        private void Uninstall_Click(object sender, RoutedEventArgs e)
        {
            if (_busy) return;
            if (!Elevation.IsAdmin) { RequestElevation(ActionKind.Uninstall); return; }
            if (!Dialogs.Confirm(this, "Kurulumu kaldır",
                    "Görevler, güvenlik duvarı kuralları ve tünel dosyaları silinir; adaptör DNS'i geri alınır. " +
                    "Discord yeniden doğrudan bağlanmayı dener. Devam edilsin mi?", "Kaldır", true))
                return;
            StartAction(ActionKind.Uninstall);
        }

        private void StartActionByName(string name)
        {
            StartAction(Actions.Parse(name, ActionKind.Status));
        }

        private void StartAction(ActionKind kind)
        {
            if (_busy) return;
            if (!Elevation.IsAdmin)
            {
                RequestElevation(kind);
                return;
            }

            string endpoint = (_endpoint.Text ?? "").Trim();
            if (endpoint.Length > 0 && !IsValidEndpoint(endpoint))
            {
                _console.Separator("Uç adresi geçersiz");
                _console.Write("  '" + endpoint + "' bir uç değil. Beklenen biçim: 162.159.192.1:2408",
                    Palette.Red, false);
                SyncEmpty();
                _endpoint.Focus();
                return;
            }
            _settings.Endpoint = endpoint;

            string script;
            string switches;
            string title;
            if (!BuildAction(kind, endpoint, out script, out switches, out title)) return;

            _console.Separator(title + " · " + DateTime.Now.ToString("HH:mm:ss"));
            _console.Write("$ " + System.IO.Path.GetFileName(script) + " " + switches, Palette.Faint, false);
            SyncEmpty();

            if (!_runner.Start(script, switches, Paths.RepoRoot ?? Paths.InstallDir))
            {
                _console.Write("Başlatılamadı.", Palette.Red, false);
                SyncEmpty();
                return;
            }

            SetBusy(true, title);
        }

        private bool BuildAction(ActionKind kind, string endpoint, out string script, out string switches,
                                 out string title)
        {
            ActionSpec spec = Actions.Build(kind, _settings, endpoint);
            script = spec.Script;
            switches = spec.Switches;
            title = spec.Title;
            if (script == null)
            {
                ScriptMissing(spec.MissingRelative);
                return false;
            }
            return true;
        }

        private void ScriptMissing(string relative)
        {
            _console.Separator("Betik bulunamadı");
            _console.Write("  " + relative + " yok. WarpCord.exe dosyasını proje klasörünün içinden çalıştırın.",
                Palette.Red, false);
            _console.Write("  Aranan klasör: " +
                           (Paths.RepoRoot ?? "(bulunamadı, --repo ile belirtebilirsiniz)"), Palette.Faint, false);
            SyncEmpty();
        }

        private void RequestElevation(ActionKind kind)
        {
            string arguments = "--run " + Actions.Name(kind);
            if (Paths.RepoRoot != null) arguments += " --repo \"" + Paths.RepoRoot + "\"";

            SingleInstance.Release();
            string error;
            if (Elevation.Relaunch(arguments, out error))
            {
                Close();
                return;
            }
            _adminBanner.Visibility = Visibility.Visible;
            _adminBannerText.Text = error;
        }

        private static bool IsValidEndpoint(string text)
        {
            return Regex.IsMatch(text, @"^(?:(?:\d{1,3}\.){3}\d{1,3}:\d{1,5}|\[[0-9A-Fa-f:]+\]:\d{1,5})$");
        }

        private void SetBusy(bool busy, string title)
        {
            _busy = busy;

            _primary.IsEnabled = !busy;
            _statusBtn.IsEnabled = !busy && Paths.StatusScript != null;
            _healBtn.IsEnabled = !busy && Paths.HealScript != null;
            _diagBtn.IsEnabled = !busy && Paths.DiagnoseScript != null;
            _uninstallBtn.IsEnabled = !busy && Paths.UninstallScript != null && (_snap == null || _snap.Installed);
            _advancedToggle.IsEnabled = !busy;
            _modeAuto.IsEnabled = !busy;
            _modeWarp.IsEnabled = !busy;
            _modeDpi.IsEnabled = !busy;
            _methodApp.IsEnabled = !busy;
            _methodManual.IsEnabled = !busy;
            _manualInstallBtn.IsEnabled = !busy;
            _endpoint.IsEnabled = !busy;

            if (busy)
            {
                _primary.Content = "Çalışıyor…";
                _runPill.Visibility = Visibility.Visible;
                _runPillText.Text = "çalışıyor";
                _runPillText.Foreground = Palette.Cyan;
                _runPillDot.Fill = Palette.Cyan;
                _runPill.Background = new SolidColorBrush(Color.FromArgb(0x16, 0x22, 0xD3, 0xEE));
                _runPill.BorderBrush = new SolidColorBrush(Color.FromArgb(0x38, 0x22, 0xD3, 0xEE));
                _runStepHost.Visibility = Visibility.Visible;
                _runStep.Text = title + " başladı…";
                _cancelBtn.Visibility = Visibility.Visible;
                if (_spin != null) { try { _spin.Begin(this, true); } catch { } }
            }
            else
            {
                _cancelBtn.Visibility = Visibility.Collapsed;
                _runStepHost.Visibility = Visibility.Collapsed;
                if (_spin != null) { try { _spin.Stop(this); } catch { } }
                UpdatePrimaryLabel();
            }
        }

        private void OnRunFinished(int code, TimeSpan elapsed)
        {
            bool ok = code == 0;
            _console.Write("", null, false);
            _console.Write((ok ? "✓ " : "✗ ") + "İşlem " + (ok ? "tamamlandı" : "tamamlanamadı") +
                           " · kod " + code + " · " + FormatDuration(elapsed),
                ok ? Palette.Green : Palette.Red, true);
            SyncEmpty();

            SetBusy(false, "");

            _runPill.Visibility = Visibility.Visible;
            _runPillText.Text = ok ? "tamam" : "hata";
            _runPillText.Foreground = ok ? Palette.Green : Palette.Red;
            _runPillDot.Fill = ok ? Palette.Green : Palette.Red;
            byte alpha = 0x16;
            Color tone = ok ? Color.FromRgb(0x34, 0xD3, 0x99) : Color.FromRgb(0xF8, 0x71, 0x71);
            _runPill.Background = new SolidColorBrush(Color.FromArgb(alpha, tone.R, tone.G, tone.B));
            _runPill.BorderBrush = new SolidColorBrush(Color.FromArgb(0x38, tone.R, tone.G, tone.B));

            Later(900, delegate { Task ignore = ProbeAsync(true, true); });
            Later(9000, delegate { if (!_busy) _runPill.Visibility = Visibility.Collapsed; });
        }

        private static string FormatDuration(TimeSpan elapsed)
        {
            if (elapsed.TotalSeconds < 60) return Math.Round(elapsed.TotalSeconds) + " sn";
            return Math.Round(elapsed.TotalMinutes, 1).ToString(CultureInfo.InvariantCulture) + " dk";
        }

        private static void Later(int milliseconds, Action action)
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
            timer.Tick += delegate
            {
                timer.Stop();
                action();
            };
            timer.Start();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            if (!_busy) return;
            if (!Dialogs.Confirm(this, "İşlemi iptal et",
                    "Süreç ağacı zorla durdurulacak. Kurulum yarıda kalırsa tünel kapalı kalabilir; " +
                    "ardından Onar çalıştırabilirsiniz.", "İptal et", true))
                return;
            _runner.Cancel();
        }

        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            Task ignore = ProbeAsync(true, true);
        }

        // ================================================================ options
        private void Advanced_Click(object sender, RoutedEventArgs e)
        {
            SetAdvanced(!IsAdvanced);
            _settings.Advanced = IsAdvanced;
        }

        private bool IsAdvanced
        {
            get { return _advancedPanel.Visibility == Visibility.Visible; }
        }

        private void SetAdvanced(bool open)
        {
            _advancedPanel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            if (_chevronRotate != null) _chevronRotate.Angle = open ? 180 : 0;
        }

        private void Mode_Checked(object sender, RoutedEventArgs e)
        {
            if (_initializing) return;
            if (ReferenceEquals(sender, _modeWarp)) _settings.Mode = "warp";
            else if (ReferenceEquals(sender, _modeDpi)) _settings.Mode = "dpi";
            else _settings.Mode = "auto";
        }

        private void Option_Changed(object sender, RoutedEventArgs e)
        {
            if (_initializing) return;
            _settings.Force = _force.IsChecked == true;
            _settings.KeepDns = _keepDns.IsChecked == true;
        }

        private void Endpoint_Changed(object sender, TextChangedEventArgs e)
        {
            if (_initializing) return;
            _settings.Endpoint = _endpoint.Text ?? "";
        }

        private void Autoscroll_Changed(object sender, RoutedEventArgs e)
        {
            if (_initializing) return;
            _settings.Autoscroll = _autoscroll.IsChecked == true;
            _console.Autoscroll = _settings.Autoscroll;
            if (_settings.Autoscroll) _consoleBox.ScrollToEnd();
        }

        // ================================================================ console
        private void SyncEmpty()
        {
            _consoleEmpty.Visibility = _console.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
        }

        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(_console.Dump());
                _runPill.Visibility = Visibility.Visible;
                _runPillText.Text = "kopyalandı";
                _runPillText.Foreground = Palette.Dim;
                _runPillDot.Fill = Palette.Dim;
                Later(2500, delegate { if (!_busy) _runPill.Visibility = Visibility.Collapsed; });
            }
            catch (Exception ex)
            {
                _console.Write("Panoya kopyalanamadı: " + ex.Message, Palette.Red, false);
            }
        }

        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            _console.Clear();
            SyncEmpty();
        }

        private void Folder_Click(object sender, RoutedEventArgs e)
        {
            string target = Directory.Exists(Paths.InstallDir) ? Paths.InstallDir : Paths.RepoRoot;
            if (string.IsNullOrEmpty(target)) return;
            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", "\"" + target + "\"") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                _console.Write("Klasör açılamadı: " + ex.Message, Palette.Red, false);
            }
        }

        private void TunnelLog_Click(object sender, RoutedEventArgs e)
        {
            OpenFile(Paths.TunnelLogPath);
        }

        private void HealLog_Click(object sender, RoutedEventArgs e)
        {
            OpenFile(Paths.HealLogPath);
        }

        private void OpenFile(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                _console.Separator("Günlük bulunamadı");
                _console.Write("  " + (path ?? "(yol yok)"), Palette.Faint, false);
                SyncEmpty();
                return;
            }
            try
            {
                Process.Start(new ProcessStartInfo("notepad.exe", "\"" + path + "\"") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                _console.Write("Günlük açılamadı: " + ex.Message, Palette.Red, false);
                SyncEmpty();
            }
        }

        private void SacHelp_Click(object sender, RoutedEventArgs e)
        {
            Dialogs.Info(this, "Smart App Control",
                "Smart App Control, imzasız ve itibarı bilinmeyen uygulamaları engeller; tek tek uygulama " +
                "izin listesi yoktur. WarpCord.exe imzasız olduğu için bu ayar açıkken hiç başlamaz, " +
                "sing-box.exe de çalışmaz ve kurulum yarıda kalır.\n\n" +
                "Kapatmak için:\n" +
                "1. Ayarlar → Gizlilik ve güvenlik → Windows Güvenliği\n" +
                "2. Uygulama ve tarayıcı denetimi → Akıllı Uygulama Denetimi\n" +
                "3. Kapalı\n\n" +
                "Not: Kapatmak geri alınamaz; Windows'u sıfırlamadan yeniden açılamaz.\n\n" +
                "Kalıcı çözüm: exe'yi bir kod imzalama sertifikasıyla imzalamak — " +
                "gui\\build.ps1 -Certificate <parmak izi veya .pfx>");
        }

        private void UpdateSacUi()
        {
            SacState state = SmartAppControl.Read();
            bool blocks = SmartAppControl.Blocks(state);
            _sacBanner.Visibility = blocks ? Visibility.Visible : Visibility.Collapsed;
            if (!blocks) return;

            _sacBannerText.Text = state == SacState.On
                ? "Smart App Control açık: imzasız WarpCord.exe ve sing-box.exe bu makinede engellenir."
                : "Smart App Control değerlendirme modunda: imzasız bileşenler engellenebilir.";
        }

        private void AdminFix_Click(object sender, RoutedEventArgs e)
        {
            string arguments = Paths.RepoRoot != null ? "--repo \"" + Paths.RepoRoot + "\"" : "";
            SingleInstance.Release();
            string error;
            if (Elevation.Relaunch(arguments, out error)) Close();
            else _adminBannerText.Text = error;
        }
        // ================================================================ window
        private void Min_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void Max_Click(object sender, RoutedEventArgs e)
        {
            ToggleMaximize();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void ToggleMaximize()
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (IsInsideButton(e.OriginalSource as DependencyObject)) return;
            if (e.ClickCount == 2)
            {
                ToggleMaximize();
                return;
            }
            try { DragMove(); }
            catch (InvalidOperationException) { }
        }

        private static bool IsInsideButton(DependencyObject source)
        {
            while (source != null)
            {
                if (source is ButtonBase) return true;
                source = VisualTreeHelper.GetParent(source) ?? LogicalTreeHelper.GetParent(source);
            }
            return false;
        }

        private void OnStateChanged(object sender, EventArgs e)
        {
            bool maximized = WindowState == WindowState.Maximized;
            if (_rootBorder != null)
            {
                _rootBorder.BorderThickness = maximized ? new Thickness(0) : new Thickness(1);
                _rootBorder.Margin = maximized ? new Thickness(7) : new Thickness(0);
            }
            if (_maxIcon != null)
            {
                _maxIcon.Data = Geometry.Parse(maximized
                    ? "M 0,2.5 L 8,2.5 L 8,10.5 L 0,10.5 Z M 2.5,2.5 L 2.5,0.5 L 10.5,0.5 L 10.5,8.5 L 8,8.5"
                    : "M 0.5,0.5 L 10.5,0.5 L 10.5,10.5 L 0.5,10.5 Z");
            }
        }

        private void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_renderMode)
            {
                SingleInstance.Release();
                return;
            }

            if (_busy)
            {
                if (!Dialogs.Confirm(this, "İşlem sürüyor",
                        "Devam eden işlem iptal edilecek. Uygulama kapatılsın mı?", "Kapat", true))
                {
                    e.Cancel = true;
                    return;
                }
                _runner.Cancel();
            }

            if (WindowState == WindowState.Normal)
            {
                _settings.Width = ActualWidth;
                _settings.Height = ActualHeight;
                _settings.Left = Left;
                _settings.Top = Top;
                _settings.HasBounds = true;
            }
            _settings.Maximized = WindowState == WindowState.Maximized;
            _settings.Save();
            SingleInstance.Release();
        }
    }
}
