using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.ProcessBuilder;
using CustomLauncher.Core;
using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace CustomLauncher
{
    public partial class MainWindow : Window
    {
        private AppSettings _settings = null!;
        private MinecraftPath _minecraftPath = null!;
        private MinecraftLauncher _launcher = null!;
        private Process? _gameProcess;
        private bool _isBusy;
        private bool _needsModpackUpdate;
        private double _scrollTarget = -1;
        private bool _scrolling;

        private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(10) };

        private const string VER = "3.0";
        private const string MC = "1.20.1";
        private const string FORGE = "47.4.11";
        private const string FULL_ID = MC + "-forge-" + FORGE;
        private const string DefPrimary = "#0D0D1E";
        private const string DefAccent = "#BB86FC";
        private const string MODPACK_VER_URL = "https://raw.githubusercontent.com/pers1k1/vrsns/main/modpack_version.txt";
        private const string LAUNCHER_VER_URL = "https://raw.githubusercontent.com/pers1k1/vrsns/main/launcher_version.txt";
        private const string MODPACK_URL = "https://github.com/pers1k1/modpack/releases/download/main/release.zip";
        private const string LAUNCHER_EXE_URL = "https://github.com/pers1k1/BattleCraft-Remake/releases/download/main/CustomLauncher.exe";
        private static readonly string FORGE_JAR_URL = $"https://maven.minecraftforge.net/net/minecraftforge/forge/{MC}-{FORGE}/forge-{MC}-{FORGE}-installer.jar";
        private string _onlineModpackVer = "0.0";

        private readonly string[] _jvmArgs = {
            "-XX:+UseG1GC","-XX:+ParallelRefProcEnabled","-XX:MaxGCPauseMillis=200",
            "-XX:+UnlockExperimentalVMOptions","-XX:+DisableExplicitGC","-XX:+AlwaysPreTouch",
            "-XX:G1NewSizePercent=30","-XX:G1MaxNewSizePercent=40","-XX:G1HeapRegionSize=8M",
            "-XX:G1ReservePercent=20","-XX:G1HeapWastePercent=5","-XX:G1MixedGCCountTarget=4",
            "-XX:InitiatingHeapOccupancyPercent=15","-XX:G1MixedGCLiveThresholdPercent=90",
            "-XX:G1RSetUpdatingPauseTimePercent=5","-XX:SurvivorRatio=32",
            "-XX:+PerfDisableSharedMem","-XX:MaxTenuringThreshold=1"};

        private TaskCompletionSource<bool>? _dialogTcs;

        private Task<bool> ShowCustomDialog(string message, string title = "Внимание", bool isYesNo = false)
        {
            CustomDialogTitle.Text = title;
            CustomDialogMessage.Text = message;
            CustomDialogBtnCancel.Visibility = isYesNo ? Visibility.Visible : Visibility.Collapsed;
            CustomDialogBtnOk.Content = isYesNo ? "Да" : "OK";

            CustomDialogOverlay.Visibility = Visibility.Visible;
            CustomDialogOverlay.Opacity = 0;
            CustomDialogOverlay.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)));

            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            CustomDialogScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.95, 1, TimeSpan.FromMilliseconds(250)) { EasingFunction = ease });
            CustomDialogScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.95, 1, TimeSpan.FromMilliseconds(250)) { EasingFunction = ease });
            CustomDialogTranslate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(15, 0, TimeSpan.FromMilliseconds(250)) { EasingFunction = ease });

            _dialogTcs = new TaskCompletionSource<bool>();
            return _dialogTcs.Task;
        }

        private void CustomDialogOk_Click(object s, RoutedEventArgs e)
        {
            CloseCustomDialog();
            _dialogTcs?.TrySetResult(true);
        }

        private void CustomDialogCancel_Click(object s, RoutedEventArgs e)
        {
            CloseCustomDialog();
            _dialogTcs?.TrySetResult(false);
        }

        private void CloseCustomDialog()
        {
            var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(150));
            fade.Completed += (s2, e2) => { CustomDialogOverlay.Visibility = Visibility.Hidden; CustomDialogOverlay.BeginAnimation(OpacityProperty, null); CustomDialogOverlay.Opacity = 1; };
            CustomDialogOverlay.BeginAnimation(OpacityProperty, fade);
        }

        public MainWindow()
        {
            InitializeComponent();
            MouseMove += OnWindowMouseMove;
            MouseLeave += (s, e) => ParticleBg?.ClearMouse();
            MouseLeftButtonDown += OnWindowClick;
            Activated += (s, e) => { if (_settings.ParticlesEnabled) ParticleBg?.Resume(); };
            Deactivated += (s, e) => ParticleBg?.Pause();
            StateChanged += (s, e) => { if (WindowState == WindowState.Minimized) ParticleBg?.Pause(); else if (_settings.ParticlesEnabled) ParticleBg?.Resume(); };
            InitializeLauncherCore();
        }

        private void OnWindowClick(object sender, MouseButtonEventArgs e)
        {
            ParticleBg?.Burst(e.GetPosition(ParticleBg));
        }

        private void OnWindowMouseMove(object sender, MouseEventArgs e)
        {
            ParticleBg?.SetMouse(e.GetPosition(ParticleBg));

            var titlePos = e.GetPosition(TitleText);
            double tw = TitleText.ActualWidth, th = TitleText.ActualHeight;
            if (tw > 0 && th > 0)
            {
                double cx = tw / 2, cy = th / 2;
                double nd = Math.Sqrt(Math.Pow((titlePos.X - cx) / (tw / 2 + 120), 2) + Math.Pow((titlePos.Y - cy) / 60, 2));
                if (nd < 1.0)
                {
                    double p = 1.0 - nd;
                    TitleTranslate.BeginAnimation(TranslateTransform.XProperty, null);
                    TitleTranslate.BeginAnimation(TranslateTransform.YProperty, null);
                    TitleTranslate.X += ((cx - titlePos.X) / cx * 6 * p - TitleTranslate.X) * 0.15;
                    TitleTranslate.Y += ((cy - titlePos.Y) / cy * 4 * p - TitleTranslate.Y) * 0.15;
                }
                else
                {
                    TitleTranslate.X *= 0.92;
                    TitleTranslate.Y *= 0.92;
                }
            }
        }

        private void InitializeLauncherCore()
        {
            _settings = AppSettings.Load();
            FillColorPresets();
            ApplyThemeFromSettings();
            ApplyCustomTheme();
            if (_settings.IsFirstRun) ShowSetupPanel();
            else
            {
                UsernameBox.Text = _settings.Username;
                RamSlider.Value = _settings.RamMb > 0 ? _settings.RamMb : 4096;
                PathBox.Text = _settings.GamePath;
                if (!Version.TryParse(_settings.ModpackVersion, out _)) _settings.ModpackVersion = "0.0";
                SwitchToMain();
            }
        }

        private void ShowSetupPanel()
        {
            SetupPanel.Visibility = Visibility.Visible;
            LoginPanel.Visibility = Visibility.Hidden;
            MainPanel.Visibility = Visibility.Hidden;
            TopButtons.Visibility = Visibility.Collapsed;
            TitleContainer.Visibility = Visibility.Collapsed;
            SetupPathBox.Text = _settings.GamePath;
        }

        private void BtnSetupSelectFolder_Click(object s, RoutedEventArgs e)
        { var d = new OpenFolderDialog(); if (d.ShowDialog() == true) SetupPathBox.Text = d.FolderName; }

        private async void BtnSetupComplete_Click(object s, RoutedEventArgs e)
        {
            string nick = SetupUsernameBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(nick)) { await ShowCustomDialog("Введите никнейм!"); return; }
            string path = SetupPathBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(path)) { await ShowCustomDialog("Выберите папку для игры!"); return; }
            _settings.Username = nick; _settings.GamePath = path; _settings.RamMb = 4096;
            AppSettings.Save(_settings);
            SetupPanel.Visibility = Visibility.Hidden;
            TopButtons.Visibility = Visibility.Visible;
            UsernameBox.Text = nick; RamSlider.Value = 4096; PathBox.Text = path;
            SwitchToMain();
        }

        private void FillColorPresets()
        {
            if (ColorPresetCombo == null) return;
            ColorPresetCombo.Items.Clear();
            ColorPresetCombo.Items.Add(new ComboBoxItem { Content = "Космический", Tag = "#0D0D1E|#BB86FC" });
            ColorPresetCombo.Items.Add(new ComboBoxItem { Content = "Темно-синий / Красный", Tag = "#1A1A2E|#E94560" });
            ColorPresetCombo.Items.Add(new ComboBoxItem { Content = "Океан / Бирюзовый", Tag = "#0A192F|#64FFDA" });
            ColorPresetCombo.Items.Add(new ComboBoxItem { Content = "Лесной / Зеленый", Tag = "#1B2631|#2ECC71" });
            ColorPresetCombo.Items.Add(new ComboBoxItem { Content = "Бордовый / Золотой", Tag = "#2C1810|#FFD700" });
            ColorPresetCombo.Items.Add(new ComboBoxItem { Content = "Шоколад / Оранжевый", Tag = "#1A1512|#FF9F43" });
            ColorPresetCombo.Items.Add(new ComboBoxItem { Content = "Мята / Лайм", Tag = "#0F2027|#00F260" });
        }

        private void ApplyThemeFromSettings()
        {
            string p = string.IsNullOrWhiteSpace(_settings.PrimaryColor) ? DefPrimary : _settings.PrimaryColor;
            string a = string.IsNullOrWhiteSpace(_settings.AccentColor) ? DefAccent : _settings.AccentColor;
            ApplyPrimaryColor(p, false); ApplyAccentColor(a, false);
            ApplyBloom(_settings.BloomEnabled ?? true, _settings.BloomStrength ?? 60, false);
            if (PrimaryColorBox != null) PrimaryColorBox.Text = p.ToUpper();
            if (AccentColorBox != null) AccentColorBox.Text = a.ToUpper();
            if (BloomEnabledCheck != null) BloomEnabledCheck.IsChecked = _settings.BloomEnabled ?? true;
            if (BloomStrengthSlider != null) BloomStrengthSlider.Value = _settings.BloomStrength ?? 60;

            if (ColorPresetCombo != null)
            {
                string tagToFind = $"{p}|{a}".ToUpper();
                ColorPresetCombo.SelectionChanged -= ColorPreset_Changed;
                foreach (ComboBoxItem item in ColorPresetCombo.Items)
                {
                    if (item.Tag is string tg && tg.ToUpper() == tagToFind)
                    {
                        ColorPresetCombo.SelectedItem = item;
                        break;
                    }
                }
                ColorPresetCombo.SelectionChanged += ColorPreset_Changed;
            }
        }

        private void ApplyPrimaryColor(string hex, bool save = true)
        {
            try { var c = (Color)ColorConverter.ConvertFromString(hex);
                this.Resources["PrimaryColor"] = c; this.Resources["PrimaryBrush"] = new SolidColorBrush(c);
                if (save) { _settings.PrimaryColor = hex; AppSettings.Save(_settings); }
            } catch { }
        }

        private void ApplyAccentColor(string hex, bool save = true)
        {
            try { var c = (Color)ColorConverter.ConvertFromString(hex);
                this.Resources["AccentColor"] = c; this.Resources["AccentBrush"] = new SolidColorBrush(c);
                if (save) { _settings.AccentColor = hex; AppSettings.Save(_settings); }
                try { ParticleBg?.UpdateAccent(c); } catch { }
            } catch { }
        }

        private void ApplyBloom(bool on, double str, bool save = true)
        {
            double k = str / 100.0;
            this.Resources["BloomBlurRadius"] = 5 + 25 * k;
            this.Resources["BloomOpacity"] = on ? 0.2 + 0.8 * k : 0.0;

            this.Resources["TitleBloomBlurRadius"] = 20 + 40 * k;
            this.Resources["TitleBloomOpacity"] = on ? 0.2 + 0.3 * k : 0.0;

            if (save) { _settings.BloomEnabled = on; _settings.BloomStrength = str; AppSettings.Save(_settings); }
            if (BloomStrengthSlider != null)
            {
                BloomStrengthSlider.IsEnabled = on;
                BloomStrengthSlider.Opacity = on ? 1.0 : 0.35;
            }
        }

        private void BtnApplyPrimaryColor_Click(object s, RoutedEventArgs e) => ApplyPrimaryColor(PrimaryColorBox.Text);
        private void BtnApplyAccentColor_Click(object s, RoutedEventArgs e) => ApplyAccentColor(AccentColorBox.Text);

        private void ColorPreset_Changed(object s, SelectionChangedEventArgs e)
        {
            if (!IsLoaded) return;
            if (ColorPresetCombo.SelectedItem is ComboBoxItem item && item.Tag is string td)
            { try { var p = td.Split('|'); ApplyPrimaryColor(p[0]); ApplyAccentColor(p[1]); PrimaryColorBox.Text = p[0]; AccentColorBox.Text = p[1]; } catch { } }
        }

        private async void BtnResetAllSettings_Click(object s, RoutedEventArgs e)
        {
            if (await ShowCustomDialog("Сбросить все?", "Сброс", true) != true) return;
            _settings = new AppSettings(); AppSettings.Save(_settings);
            try { string d = GetThemeDir(); if (Directory.Exists(d)) Directory.Delete(d, true); } catch { }
            MainWnd.Background = null; MainWnd.SetResourceReference(Control.BackgroundProperty, "PrimaryBrush"); Icon = null;
            try { ParticleBg?.Start(); } catch { }
            InitializeLauncherCore();
        }

        private void InitializeLauncher()
        {
            _minecraftPath = new MinecraftPath(_settings.GamePath);
            _launcher = new MinecraftLauncher(MinecraftLauncherParameters.CreateDefault(_minecraftPath));
            _launcher.FileProgressChanged += (s, e) => Dispatcher.BeginInvoke(() =>
            { StatusText.Text = e.Name; if (e.TotalTasks > 0) SetProgress((double)e.ProgressedTasks / e.TotalTasks * 100); });
        }

        private async void BtnPlay_Click(object sender, RoutedEventArgs e)
        {
            if (_gameProcess != null) { try { _gameProcess.Kill(); } catch { } _gameProcess = null; Show(); SetPlayState("idle"); StatusText.Text = "Готов"; SetProgress(0); return; }
            if (!_settings.HasGamePath) { await ShowCustomDialog("Выберите папку для игры в настройках!"); return; }

            BtnPlay.IsEnabled = false; SetBusy(true);
            bool didInstall = false;
            try
            {
                if (!Directory.Exists(_settings.GamePath)) Directory.CreateDirectory(_settings.GamePath);
                InitializeLauncher();

                string vDir = Path.Combine(_settings.GamePath, "versions");
                bool hasForge = Directory.Exists(vDir) && Directory.GetDirectories(vDir).Any(d => d.Contains(MC) && d.ToLower().Contains("forge"));
                if (!hasForge) { didInstall = true; await InstallForgeSilent(); }
                if (!_settings.IsModpackInstalled) { didInstall = true; await InstallModpack(true); }
                else if (_needsModpackUpdate) { didInstall = true; await InstallModpack(true); _needsModpackUpdate = false; }

                if (didInstall) { StatusText.Text = "Установка завершена! Нажмите ИГРАТЬ."; SetProgress(0); SetPlayState("idle"); BtnPlay.IsEnabled = true; SetBusy(false); return; }

                StatusText.Text = "Запуск..."; SetProgress(100);
                var vers = await _launcher.GetAllVersionsAsync();
                var ver = vers.FirstOrDefault(v => v.Name == FULL_ID) ?? vers.FirstOrDefault(v => v.Name.Contains(MC) && v.Name.ToLower().Contains("forge"));
                if (ver == null) { await ShowCustomDialog("Forge не найден!"); return; }

                var opt = new MLaunchOption { MaximumRamMb = _settings.RamMb, Session = MSession.CreateOfflineSession(_settings.Username), JavaPath = FindJava() };
                _gameProcess = await _launcher.CreateProcessAsync(ver.Name, opt);
                InjectJvmArgs(_gameProcess);

                _gameProcess.StartInfo.CreateNoWindow = !_settings.DebugConsole;
                _gameProcess.StartInfo.UseShellExecute = false;

                _gameProcess.Start();
                SetPlayState("running"); BtnPlay.IsEnabled = true; SetBusy(false); Hide();
                await _gameProcess.WaitForExitAsync();
                _gameProcess = null; Show(); SetPlayState("idle"); StatusText.Text = "Готов";
            }
            catch (Exception ex) { await ShowCustomDialog($"Ошибка: {ex.Message}"); }
            finally { SetProgress(0); BtnPlay.IsEnabled = true; SetBusy(false); }
        }

        private void SetBusy(bool busy)
        {
            _isBusy = busy;
            BtnReinstall.IsEnabled = !busy;
            BtnSettingsOpen.IsEnabled = !busy;
        }

        private void SetPlayState(string st)
        {
            if (st == "running") { BtnPlay.Content = "ОТМЕНА"; BtnPlay.Background = new SolidColorBrush(Color.FromRgb(180, 60, 60)); }
            else { BtnPlay.Content = "\u25B6  ИГРАТЬ"; BtnPlay.SetResourceReference(Control.BackgroundProperty, "AccentBrush"); }
        }

        private void InjectJvmArgs(Process p)
        {
            if (string.IsNullOrEmpty(p.StartInfo.Arguments)) return;
            string jvm = string.Join(" ", _jvmArgs); string a = p.StartInfo.Arguments;
            int i = a.IndexOf(" -cp "); if (i < 0) i = a.IndexOf(" -classpath ");
            p.StartInfo.Arguments = i > 0 ? a.Insert(i, " " + jvm) : jvm + " " + a;
        }

        private string FindJava()
        {
            string old = Path.Combine(_settings.GamePath, "runtime", "java-runtime-gamma", "windows-x64", "java-runtime-gamma", "bin", "java.exe");
            if (File.Exists(old)) return old;
            string rt = Path.Combine(_settings.GamePath, "runtime");
            if (Directory.Exists(rt)) { var f = Directory.GetFiles(rt, "java.exe", SearchOption.AllDirectories).FirstOrDefault(j => j.Contains("bin") && !j.Contains("javaw")); if (f != null) return f; }
            return "java";
        }

        private void EnsureProfiles() { string p = Path.Combine(_settings.GamePath, "launcher_profiles.json"); if (!File.Exists(p)) File.WriteAllText(p, "{\"profiles\":{}}"); }

        private async Task InstallForgeSilent()
        {
            StatusText.Text = "Установка Forge..."; GameProgressBar.IsIndeterminate = true;
            try
            {
                await _launcher.InstallAsync(MC); EnsureProfiles();
                string jar = Path.Combine(Path.GetTempPath(), "forge_installer.jar");
                if (File.Exists(jar)) File.Delete(jar);
                await new FileDownloader().DownloadFileAsync(FORGE_JAR_URL, jar);
                var psi = new ProcessStartInfo { FileName = FindJava(), Arguments = $"-jar \"{jar}\" --installClient \"{_settings.GamePath}\"", CreateNoWindow = true, UseShellExecute = false };
                var proc = Process.Start(psi); if (proc != null) await proc.WaitForExitAsync();
                await _launcher.GetAllVersionsAsync();
                try { File.Delete(jar); } catch { }
                CleanForgeLog();
            }
            catch (Exception ex) { await ShowCustomDialog($"Ошибка Forge: {ex.Message}"); }
            finally { GameProgressBar.IsIndeterminate = false; }
        }

        private void CleanForgeLog()
        {
            try { foreach (var dir in new[] { Path.GetTempPath(), _settings.GamePath, AppDomain.CurrentDomain.BaseDirectory })
                foreach (var f in Directory.GetFiles(dir, "*.jar.log")) try { File.Delete(f); } catch { }
            } catch { }
        }

        private static readonly string[] ModpackDirs = { "mods", "config", "scripts", "kubejs", "resourcepacks", "shaderpacks", "defaultconfigs", "tacz", "tacz_backup" };

        private async Task InstallModpack(bool clean)
        {
            if (clean)
            {
                StatusText.Text = "Очистка старых файлов...";
                foreach (var dir in ModpackDirs)
                {
                    string p = Path.Combine(_settings.GamePath, dir);
                    try { if (Directory.Exists(p)) Directory.Delete(p, true); } catch { }
                }
            }
            string zip = Path.Combine(Path.GetTempPath(), "modpack_download.zip");
            var dl = new FileDownloader();
            dl.ProgressChanged += v => Dispatcher.BeginInvoke(() => { SetProgress(v); StatusText.Text = $"Скачивание {v:F0}%"; });
            await dl.DownloadFileAsync(MODPACK_URL, zip);
            StatusText.Text = "Распаковка...";
            await Task.Run(() => { ZipFile.ExtractToDirectory(zip, _settings.GamePath, true); try { File.Delete(zip); } catch { } });
            _settings.IsModpackInstalled = true;
            _settings.ModpackVersion = _onlineModpackVer != "0.0" ? _onlineModpackVer : "1.0";
            AppSettings.Save(_settings);
        }

        private async void SwitchToMain()
        {
            SetupPanel.Visibility = Visibility.Hidden; LoginPanel.Visibility = Visibility.Hidden;
            MainPanel.Visibility = Visibility.Visible; TopButtons.Visibility = Visibility.Visible;
            TitleContainer.Visibility = Visibility.Visible;
            WelcomeText.Text = $"Игрок: {_settings.Username}"; VersionText.Text = $"v{VER}";
            DebugCheck.IsChecked = _settings.DebugConsole;
            ParticlesCheck.IsChecked = _settings.ParticlesEnabled;
            if (!_settings.ParticlesEnabled) ParticleBg?.Stop(); else ParticleBg?.FadeIn();
            var ease = new QuarticEase { EasingMode = EasingMode.EaseOut };
            TitleContainer.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(1000)) { EasingFunction = ease });
            TitleEntranceTranslate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(30, 0, TimeSpan.FromMilliseconds(1000)) { EasingFunction = ease });

            InitializeLauncher(); await CheckUpdates();
        }

        private void LoginGridState() { MainPanel.Visibility = Visibility.Hidden; LoginPanel.Visibility = Visibility.Visible; UsernameBox.Clear(); }

        private void BtnLogin_Click(object s, RoutedEventArgs e)
        { var n = UsernameBox.Text.Trim(); if (string.IsNullOrWhiteSpace(n)) return; _settings.Username = n; AppSettings.Save(_settings); SwitchToMain(); }

        private void BtnClose_Click(object s, RoutedEventArgs e) => Application.Current.Shutdown();
        private void BtnMinimize_Click(object s, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void SetProgress(double v)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(() => SetProgress(v)); return; }
            GameProgressBar.BeginAnimation(ProgressBar.ValueProperty, new DoubleAnimation { To = v, Duration = TimeSpan.FromMilliseconds(250), EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
        }

        private string GetThemeDir() { string p = Path.Combine(_settings.GamePath, "launcher_theme"); if (_settings.HasGamePath && !Directory.Exists(p)) try { Directory.CreateDirectory(p); } catch { } return p; }

        private void ApplyCustomTheme()
        {
            MainWnd.SetResourceReference(Control.BackgroundProperty, "PrimaryBrush"); if (_settings.ParticlesEnabled) ParticleBg?.Start();
            if (!_settings.HasGamePath) return;
            try
            {
                string dir = GetThemeDir(); string ico = Path.Combine(dir, "icon.ico");
                if (File.Exists(ico)) { var bmp = new BitmapImage(); using (var st = File.OpenRead(ico)) { bmp.BeginInit(); bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.StreamSource = st; bmp.EndInit(); } bmp.Freeze(); Icon = bmp; }
            } catch { }
        }

        private void BtnSettings_Click(object s, RoutedEventArgs e)
        {
            SettingsPanel.Visibility = Visibility.Visible; _scrollTarget = -1;

            try 
            { 
                int maxRam = (int)(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1048576L); 
                RamSlider.Maximum = Math.Max(2048, maxRam); 
            } 
            catch { RamSlider.Maximum = 8192; }

            RamSlider.Value = _settings.RamMb > 0 ? _settings.RamMb : 4096;
            PathBox.Text = _settings.GamePath;
            SettingsPanel.Opacity = 0;
            SettingsPanel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)));
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            SettingsScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.93, 1, TimeSpan.FromMilliseconds(250)) { EasingFunction = ease });
            SettingsScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.93, 1, TimeSpan.FromMilliseconds(250)) { EasingFunction = ease });
            SettingsTranslate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(25, 0, TimeSpan.FromMilliseconds(250)) { EasingFunction = ease });

            var titleEase = new QuarticEase { EasingMode = EasingMode.EaseOut };
            SettingsTitle.Opacity = 0;
            SettingsTitleTranslate.Y = 20;
            SettingsTitle.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(800)) { EasingFunction = titleEase, BeginTime = TimeSpan.FromMilliseconds(150) });
            SettingsTitleTranslate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(20, 0, TimeSpan.FromMilliseconds(800)) { EasingFunction = titleEase, BeginTime = TimeSpan.FromMilliseconds(150) });
        }

        private void BtnCloseSettings_Click(object s, RoutedEventArgs e)
        {
            if (int.TryParse(RamBox.Text, out int ram)) _settings.RamMb = ram;
            string np = PathBox.Text.Trim(); if (!string.IsNullOrWhiteSpace(np)) _settings.GamePath = np;
            AppSettings.Save(_settings); if (_settings.HasGamePath) InitializeLauncher();
            var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(150));
            fade.Completed += (s2, e2) => { SettingsPanel.Visibility = Visibility.Hidden; SettingsPanel.BeginAnimation(OpacityProperty, null); SettingsPanel.Opacity = 1; };
            SettingsPanel.BeginAnimation(OpacityProperty, fade);
        }

        private void BtnSaveSettings_Click(object s, RoutedEventArgs e) => BtnCloseSettings_Click(s, e);
        private void DebugCheck_Changed(object s, RoutedEventArgs e) { if (IsLoaded) { _settings.DebugConsole = DebugCheck.IsChecked == true; AppSettings.Save(_settings); } }
        private void ParticlesCheck_Changed(object s, RoutedEventArgs e) { if (!IsLoaded) return; bool on = ParticlesCheck.IsChecked == true; _settings.ParticlesEnabled = on; AppSettings.Save(_settings); ParticlesCheck.IsEnabled = false; if (on) ParticleBg?.FadeIn(() => Dispatcher.Invoke(() => ParticlesCheck.IsEnabled = true)); else ParticleBg?.FadeOut(() => Dispatcher.Invoke(() => ParticlesCheck.IsEnabled = true)); }
        private void BtnSelectFolder_Click(object s, RoutedEventArgs e) { var d = new OpenFolderDialog(); if (d.ShowDialog() == true) PathBox.Text = d.FolderName; }

        private void RamSlider_ValueChanged(object s, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!IsLoaded || RamSlider == null) return;
            double[] snapPoints = { 2048, 4096, 6144, 8192, 10240, 12288, 14336, 16384, 24576, 32768, 49152, 65536 };
            double val = e.NewValue;
            foreach (var sp in snapPoints)
            {
                if (Math.Abs(val - sp) <= 50)
                {
                    if (Math.Abs(RamSlider.Value - sp) > 0.1) RamSlider.Value = sp;
                    break;
                }
            }
        }

        private async Task CheckUpdates()
        {
            try
            {
                string ts = "?t=" + DateTime.Now.Ticks;
                var t1 = _httpClient.GetStringAsync(MODPACK_VER_URL + ts);
                var t2 = _httpClient.GetStringAsync(LAUNCHER_VER_URL + ts);
                await Task.WhenAll(t1, t2);
                if (Version.TryParse(t1.Result.Trim(), out var onV) && Version.TryParse(_settings.ModpackVersion, out var loV))
                {
                    _onlineModpackVer = t1.Result.Trim();
                    if (!_settings.IsModpackInstalled) { BtnPlay.Content = "УСТАНОВИТЬ"; BtnPlay.Background = new SolidColorBrush(Color.FromRgb(220, 150, 30)); }
                    else if (onV > loV) { _needsModpackUpdate = true; BtnPlay.Content = "ОБНОВИТЬ"; BtnPlay.Background = new SolidColorBrush(Color.FromRgb(220, 150, 30)); }
                    else { _needsModpackUpdate = false; SetPlayState("idle"); }
                }
                StatusText.Text = $"Модпак v{_settings.ModpackVersion}";
                string lv = t2.Result.Trim();
                if (lv != VER && await ShowCustomDialog($"Обновить лаунчер до {lv}?", "Обновление", true)) await UpdateLauncher();
            }
            catch { StatusText.Text = "Ошибка сети"; }
        }

        private async Task UpdateLauncher()
        {
            StatusText.Text = "Обновление...";
            try
            {
                string dir = AppDomain.CurrentDomain.BaseDirectory, cur = Process.GetCurrentProcess().MainModule!.FileName;
                string tmp = Path.Combine(dir, "upd.exe"), bat = Path.Combine(dir, "update.bat");
                await new FileDownloader().DownloadFileAsync(LAUNCHER_EXE_URL, tmp);
                File.WriteAllText(bat, $@"timeout 2 & del ""{cur}"" & move ""{tmp}"" ""{cur}"" & start """" ""{cur}"" & del %0");
                Process.Start(new ProcessStartInfo(bat) { CreateNoWindow = true, UseShellExecute = true }); Application.Current.Shutdown();
            }
            catch { StatusText.Text = "Ошибка обновления"; }
        }

        private async void BtnReinstall_Click(object s, RoutedEventArgs e)
        {
            if (_isBusy) return;
            if (!_settings.HasGamePath) { await ShowCustomDialog("Сначала выберите папку!"); return; }
            if (await ShowCustomDialog("Перекачать моды?", "Подтверждение", true))
            { SetBusy(true); await InstallModpack(true); StatusText.Text = "Моды переустановлены"; SetPlayState("idle"); SetBusy(false); }
        }

        private void BtnChangeIcon_Click(object s, RoutedEventArgs e)
        { var d = new OpenFileDialog { Filter = "Icon|*.ico" }; if (d.ShowDialog() == true) { try { File.Copy(d.FileName, Path.Combine(GetThemeDir(), "icon.ico"), true); ApplyCustomTheme(); } catch { } } }

        private void BloomEnabledCheck_Changed(object s, RoutedEventArgs e) { if (IsLoaded) ApplyBloom(BloomEnabledCheck.IsChecked == true, BloomStrengthSlider.Value); }
        private void BloomStrengthSlider_ValueChanged(object s, RoutedPropertyChangedEventArgs<double> e) { if (IsLoaded) ApplyBloom(BloomEnabledCheck.IsChecked == true, e.NewValue); }

        private void SettingsScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            e.Handled = true;
            var sv = SettingsScrollViewer;
            if (_scrollTarget < 0) _scrollTarget = sv.VerticalOffset;
            _scrollTarget = Math.Clamp(_scrollTarget - e.Delta * 0.5, 0, sv.ScrollableHeight);
            if (!_scrolling) { _scrolling = true; CompositionTarget.Rendering += ScrollTick; }
        }

        private void ScrollTick(object? s, EventArgs e)
        {
            var sv = SettingsScrollViewer;
            double cur = sv.VerticalOffset, diff = _scrollTarget - cur;
            if (Math.Abs(diff) < 0.5) { sv.ScrollToVerticalOffset(_scrollTarget); _scrolling = false; CompositionTarget.Rendering -= ScrollTick; return; }
            sv.ScrollToVerticalOffset(cur + diff * 0.15);
        }

        private void SettingsScroll_ScrollChanged(object s, ScrollChangedEventArgs e)
        { if (ColorPresetCombo != null && ColorPresetCombo.IsDropDownOpen) ColorPresetCombo.IsDropDownOpen = false; }

        public void Btn_MouseTrack(object sender, MouseEventArgs e)
        {
            var btn = (Button)sender;
            if (btn.Template.FindName("BtnTr", btn) is TranslateTransform t)
            {
                t.BeginAnimation(TranslateTransform.XProperty, null);
                t.BeginAnimation(TranslateTransform.YProperty, null);
                var p = e.GetPosition(btn);
                double cx = btn.ActualWidth / 2, cy = btn.ActualHeight / 2;
                double tx = (cx - p.X) / cx * 5;
                double ty = (cy - p.Y) / cy * 3;
                t.X += (tx - t.X) * 0.3;
                t.Y += (ty - t.Y) * 0.3;
            }
        }

        public void Btn_MouseReset(object sender, MouseEventArgs e)
        {
            var btn = (Button)sender;
            if (btn.Template.FindName("BtnTr", btn) is TranslateTransform t)
            {
                var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                t.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(300)) { EasingFunction = ease });
                t.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(300)) { EasingFunction = ease });
            }
        }
    }
}
