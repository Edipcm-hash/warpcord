using System;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace WarpCord
{
    /// <summary>Tray icon, network watch, Discord helper, settings window and auto repair.</summary>
    internal sealed partial class MainWindow
    {
        private TrayIcon _tray;
        private TrayState _lastTrayState = (TrayState)(-1);
        private DateTime _lastAutoHeal = DateTime.MinValue;
        private DispatcherTimer _networkDebounce;
        private bool _exiting;
        private bool _trayHintShown;

        private void InitLanguage()
        {
            L10n.Lang = _settings.Language;
            L10n.Apply(_root);
        }

        private void InitShell()
        {
            if (_settings.TrayEnabled) CreateTray();

            _networkDebounce = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _networkDebounce.Tick += delegate
            {
                _networkDebounce.Stop();
                OnNetworkChanged();
            };

            try
            {
                NetworkChange.NetworkAddressChanged += delegate
                {
                    if (_renderMode) return;
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        _networkDebounce.Stop();
                        _networkDebounce.Start();
                    }));
                };
            }
            catch
            {
                // NIC events are a nicety
            }
        }

        // ------------------------------------------------------------------ tray
        private void CreateTray()
        {
            if (_tray != null) return;
            try
            {
                _tray = new TrayIcon(_settings.Notifications);
                _tray.OpenRequested += OnTrayOpen;
                _tray.RepairRequested += OnTrayRepair;
                _tray.ExitRequested += OnTrayExit;
                _tray.Show();
            }
            catch
            {
                _tray = null;
            }
        }

        private void DestroyTray()
        {
            if (_tray == null) return;
            try
            {
                _tray.Dispose();
            }
            catch { }
            _tray = null;
        }

        private void OnTrayOpen()
        {
            Dispatcher.BeginInvoke(new Action(delegate
            {
                Show();
                WindowState = WindowState.Normal;
                Activate();
            }));
        }

        private void OnTrayRepair()
        {
            Dispatcher.BeginInvoke(new Action(delegate
            {
                Show();
                WindowState = WindowState.Normal;
                Activate();
                StartAction(ActionKind.Heal);
            }));
        }

        private void OnTrayExit()
        {
            Dispatcher.BeginInvoke(new Action(delegate
            {
                _exiting = true;
                Close();
            }));
        }

        private void UpdateTray(Snapshot snapshot)
        {
            if (_tray == null || snapshot == null) return;

            TrayState state;
            string tooltip;
            if (_busy)
            {
                state = TrayState.Busy;
                tooltip = "WarpCord · " + L10n.T("çalışıyor");
            }
            else if (!snapshot.Installed)
            {
                state = TrayState.Unknown;
                tooltip = "WarpCord · " + L10n.T("Kurulu değil");
            }
            else if (snapshot.Healthy)
            {
                state = TrayState.Healthy;
                tooltip = "WarpCord · " + L10n.T("Tünel sağlıklı");
            }
            else
            {
                state = TrayState.Problem;
                tooltip = "WarpCord · " + L10n.T("Tünelde sorun var");
            }

            _tray.SetState(state, tooltip);

            if (_lastTrayState != (TrayState)(-1) && _lastTrayState != state && snapshot.Installed && !_busy)
            {
                if (state == TrayState.Problem)
                    _tray.Notify("WarpCord", L10n.T("Tünelde sorun var") + " — " + L10n.T("Onar"), true);
                else if (state == TrayState.Healthy && _lastTrayState == TrayState.Problem)
                    _tray.Notify("WarpCord", L10n.T("Tünel sağlıklı"), false);
            }
            _lastTrayState = state;
        }

        // --------------------------------------------------------------- network
        private void OnNetworkChanged()
        {
            if (_renderMode || _snap == null || !_snap.Installed) return;
            if (_networkBanner == null) return;
            _networkBanner.Visibility = Visibility.Visible;
            Task ignore = ProbeAsync(true, true);
        }

        private void NetworkFix_Click(object sender, RoutedEventArgs e)
        {
            if (_networkBanner != null) _networkBanner.Visibility = Visibility.Collapsed;
            StartAction(ActionKind.Install);
        }

        // --------------------------------------------------------------- discord
        private void DiscordRestart_Click(object sender, RoutedEventArgs e)
        {
            if (_busy) return;
            _log.Separator("Discord · " + DateTime.Now.ToString("HH:mm:ss"));

            Task.Run(delegate
            {
                string error;
                bool ok = DiscordClient.Restart(out error);
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (ok)
                    {
                        _log.Write("  " + L10n.T("Discord yeniden başlatıldı."), Palette.Green, false);
                    }
                    else
                    {
                        _log.Write("  " + (error ?? L10n.T("Discord kurulu değil ya da yol bulunamadı.")), Palette.Red, false);
                        Dialogs.Info(this, L10n.T("Discord'u yeniden başlat"),
                            L10n.T("Discord kurulu değil ya da yol bulunamadı.") + "\n\n" + (error ?? ""));
                    }
                    SyncEmpty();
                }));
            });
        }

        // -------------------------------------------------------------- settings
        private void Settings_Click(object sender, RoutedEventArgs e)
        {
            var window = new SettingsWindow(_settings, _snap, ApplySettingsChanged);
            window.Owner = this;
            window.ShowDialog();
            ApplySettingsChanged();
        }

        /// <summary>Re-applies everything that depends on settings (language, tray, endpoint).</summary>
        private void ApplySettingsChanged()
        {
            L10n.Lang = _settings.Language;
            L10n.Apply(_root);
            if (_tray != null) _tray.RefreshLanguage();

            if (_settings.TrayEnabled) CreateTray();
            else DestroyTray();

            _repoText.Text = DescribeEngineSource();
            _rows.Clear();
            _index.Clear();
            InitRows();
            if (_snap != null) ApplySnapshot(_snap);
            else UpdatePrimaryLabel();

            SetMethodUi();
            UpdateAdminUi();
            UpdateSacUi();
            SetLogTabHint();
        }

        // ------------------------------------------------------------------ logs
        private void SetLogTab(LogTab tab)
        {
            if (_log == null) return;
            _log.SetTab(tab);
            if (tab != LogTab.Action) _log.RefreshFromFiles(Paths.TunnelLogPath, Paths.HealLogPath);
            SetLogTabHint();
            SyncEmpty();
        }

        private void SetLogTabHint()
        {
            if (_consoleEmpty == null) return;
            string hint;
            if (_log == null || _log.Tab == LogTab.Action)
                hint = L10n.T("Kurulum, onarım ve teşhis çıktısı burada akar. Başlamak için soldaki düğmeyi kullan.");
            else if (_log.Tab == LogTab.Tunnel)
                hint = L10n.T("Tünel günlüğü boş. Kurulumdan sonra sing-box akışları burada görünür.");
            else
                hint = L10n.T("Onarım günlüğü boş. Koruyucu çalıştığında buraya yazar.");
            _consoleEmpty.Text = hint;
        }

        // ------------------------------------------------------------ auto repair
        private void MaybeAutoHeal(Snapshot snapshot)
        {
            if (!_settings.AutoHeal || _busy || _renderMode) return;
            if (snapshot == null || !snapshot.Installed || snapshot.Healthy) return;
            if ((DateTime.Now - _lastAutoHeal).TotalMinutes < 3) return;

            _lastAutoHeal = DateTime.Now;
            _log.Separator("Otomatik onarım · " + DateTime.Now.ToString("HH:mm:ss"));
            _log.Write("  " + L10n.T("Sorun görüldüğünde onarımı kendisi çalıştırır."), Palette.Faint, false);
            SyncEmpty();
            StartAction(ActionKind.Heal);
        }
    }
}
