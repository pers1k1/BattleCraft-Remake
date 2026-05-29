using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.ProcessBuilder;
using CustomLauncher.Core;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
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

        private System.Windows.Threading.DispatcherTimer? _sysMonTimer;
        private System.Collections.Generic.List<string> _logLines = new();

        private ServerManager? _serverManager;
        private ServerConfig? _activeServerConfig;
        private bool _isServerTab;
        private bool _isServerBusy;
        private const int MAX_CONSOLE_CHARS = 100000;

        private class StatusTextDummy
        {
            private MainWindow _w;
            public StatusTextDummy(MainWindow w) { _w = w; }
            public string Text { set { _w.Log(value); } get { return ""; } }
        }
        private StatusTextDummy StatusText => new StatusTextDummy(this);

        private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(10) };

        private const string VER = "6.0";
        private const string MC = "1.20.1";
        private const string FORGE = "47.4.20";
        private const string FULL_ID = MC + "-forge-" + FORGE;
        private const string DefPrimary = "#0D0D1E";
        private const string DefAccent = "#BB86FC";
        private const string MODPACK_VER_URL = "https://raw.githubusercontent.com/pers1k1/vrsns/main/modpack_version.txt";
        private const string LAUNCHER_VER_URL = "https://raw.githubusercontent.com/pers1k1/vrsns/main/launcher_version.txt";
        private const string SERVER_MODPACK_VER_URL = "https://raw.githubusercontent.com/pers1k1/vrsns/main/server_modpack_version.txt";
        private const string MODPACK_URL = "https://github.com/pers1k1/modpack/releases/download/main/release.zip";
        private const string LAUNCHER_EXE_URL = "https://github.com/pers1k1/BattleCraft-Remake/releases/download/main/BCR.exe";
        private static readonly string FORGE_JAR_URL = $"https://maven.minecraftforge.net/net/minecraftforge/forge/{MC}-{FORGE}/forge-{MC}-{FORGE}-installer.jar";
        private string _onlineModpackVer = "0.0";
        private string _onlineServerModpackVer = "0.0";
        private bool _needsServerModpackUpdate = false;

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
            fade.Completed += (s2, e2) =>
            {
                CustomDialogOverlay.Visibility = Visibility.Hidden;
                CustomDialogOverlay.BeginAnimation(OpacityProperty, null);
                CustomDialogOverlay.Opacity = 1;
                CustomDialogInputBox.Visibility = Visibility.Collapsed;
            };
            CustomDialogOverlay.BeginAnimation(OpacityProperty, fade);
        }

        private async Task<string?> ShowInputDialogAsync(string title, string defaultValue = "")
        {
            CustomDialogTitle.Text = title;
            CustomDialogMessage.Text = "";
            CustomDialogMessage.Visibility = Visibility.Collapsed;
            CustomDialogInputBox.Visibility = Visibility.Visible;
            CustomDialogInputBox.Text = defaultValue;
            CustomDialogBtnCancel.Visibility = Visibility.Visible;
            CustomDialogBtnOk.Content = "OK";

            CustomDialogOverlay.Visibility = Visibility.Visible;
            CustomDialogOverlay.Opacity = 0;
            CustomDialogOverlay.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)));

            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            CustomDialogScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.95, 1, TimeSpan.FromMilliseconds(250)) { EasingFunction = ease });
            CustomDialogScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.95, 1, TimeSpan.FromMilliseconds(250)) { EasingFunction = ease });
            CustomDialogTranslate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(15, 0, TimeSpan.FromMilliseconds(250)) { EasingFunction = ease });

            _dialogTcs = new TaskCompletionSource<bool>();
            bool confirmed = await _dialogTcs.Task;

            CustomDialogMessage.Visibility = Visibility.Visible;

            if (!confirmed)
                return null;

            string trimmedInput = CustomDialogInputBox.Text.Trim();
            return string.IsNullOrWhiteSpace(trimmedInput) ? null : trimmedInput;
        }

        public MainWindow()
        {
            InitializeComponent();
            MouseMove += OnWindowMouseMove;
            InitializeLauncherCore();
        }

        private void Log(string message)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(() => Log(message)); return; }
            string prefix = message.Contains("Ошибка") ? "[ERR]" : "[SYS]";
            _logLines.Add($"{prefix} {message}");
            if (_logLines.Count > 6) _logLines.RemoveAt(0);
            LogTerminalText.Text = string.Join("\n", _logLines);
        }

        private void StartTimers()
        {
            try { _sysMonTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(5) }; _sysMonTimer.Tick += (s, e) => UpdateSysMonitor(); _sysMonTimer.Start(); UpdateSysMonitor(); } catch { }
        }

        private void UpdateSysMonitor()
        {
            try
            {
                var memInfo = GC.GetGCMemoryInfo();
                double totalMem = memInfo.TotalAvailableMemoryBytes / 1073741824.0;
                double usedMem = memInfo.MemoryLoadBytes / 1073741824.0;
                int memBars = (int)((usedMem / totalMem) * 10);
                double appMem = Process.GetCurrentProcess().WorkingSet64 / 1048576.0;

                var accentColor = (Color)FindResource("AccentColor");
                var dimAccent = Color.FromArgb(140, accentColor.R, accentColor.G, accentColor.B);
                var accentBrush = new SolidColorBrush(dimAccent);
                var dimBrush = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255));
                var textBrush = new SolidColorBrush(Color.FromArgb(102, 255, 255, 255));

                var doc = SysMonitorRich.Document;
                doc.Blocks.Clear();
                var p1 = new Paragraph();
                p1.Inlines.Add(new Run("SYS RAM: [") { Foreground = textBrush });
                for (int i = 0; i < 10; i++)
                    p1.Inlines.Add(new Run("|") { Foreground = i < memBars ? accentBrush : dimBrush });
                p1.Inlines.Add(new Run($"] {usedMem:F1}/{totalMem:F1} GB") { Foreground = textBrush });
                doc.Blocks.Add(p1);
                var p2 = new Paragraph();
                p2.Inlines.Add(new Run($"APP RAM: {appMem:F0} MB") { Foreground = textBrush });
                doc.Blocks.Add(p2);
            }
            catch { }
        }


        private void OnWindowMouseMove(object sender, MouseEventArgs e)
        {
        }

        private void InitializeLauncherCore()
        {
            _settings = AppSettings.Load();
            FillColorPresets();
            ApplyThemeFromSettings();
            ApplyCustomTheme();
            StartTimers();
            if (_settings.IsFirstRun) { _ = AnimateTerminalText(TopLeftTitleText, "BattleCraft Remake Launcher"); ShowSetupPanel(); }
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

            SetupPathBox.Text = _settings.GamePath;

            SetupPanel.Opacity = 0;
            SetupPanel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(800)) { EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }, BeginTime = TimeSpan.FromMilliseconds(200) });
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
            _ = AnimateTerminalText(TopLeftTitleText, "BattleCraft Remake Launcher");
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
                bool hasForge = Directory.Exists(vDir) && Directory.Exists(Path.Combine(vDir, FULL_ID));
                
                if (!hasForge) 
                { 
                    if (Directory.Exists(vDir))
                    {
                        foreach (var d in Directory.GetDirectories(vDir))
                        {
                            string dName = Path.GetFileName(d);
                            if (dName.Contains(MC) && dName.ToLower().Contains("forge") && dName != FULL_ID)
                            {
                                try { Directory.Delete(d, true); } catch { }
                            }
                        }
                    }
                    didInstall = true; 
                    await InstallForgeSilent(); 
                }
                if (!_settings.IsModpackInstalled) { didInstall = true; await InstallModpack(true); }
                else if (_needsModpackUpdate) { didInstall = true; await InstallModpack(true); _needsModpackUpdate = false; }

                if (didInstall) { Log("Готово!"); StatusText.Text = "Установка завершена! Нажмите ИГРАТЬ."; SetProgress(0); SetPlayState("idle"); BtnPlay.IsEnabled = true; SetBusy(false); return; }

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
                _logLines.Clear(); LogTerminalText.Text = "";
                SetPlayState("running"); BtnPlay.IsEnabled = true; SetBusy(false); 
                
                bool isServerRunning = _serverManager != null && _serverManager.CurrentState != ServerState.Stopped;
                if (!isServerRunning) Hide();
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
            Log("Распаковка завершена!");
            _settings.IsModpackInstalled = true;
            _settings.ModpackVersion = _onlineModpackVer != "0.0" ? _onlineModpackVer : "1.0";
            AppSettings.Save(_settings);
        }

        private async Task AnimateTerminalText(TextBlock tb, string targetText)
        {
            tb.Text = "";
            string chars = "$?#!*%@^&~";
            var rnd = new Random();
            for (int i = 0; i < targetText.Length; i++)
            {
                tb.Text = targetText.Substring(0, i) + chars[rnd.Next(chars.Length)] + "_";
                await Task.Delay(25);
                tb.Text = targetText.Substring(0, i + 1) + "_";
                await Task.Delay(25);
            }
            tb.Text = targetText;
        }

        private string GetRandomGreeting(string username)
        {
            int hour = DateTime.Now.Hour;
            var rnd = new Random();
            
            if (hour >= 4 && hour < 12)
            {
                string[] p = {
                    $"Доброе утро, {username}!", $"Прекрасное утро, не так ли, {username}?", $"Просыпайся и пой, {username}!",
                    $"С новым днем, {username}!", $"Утро добрым бывает, {username}!", $"Время свершений, {username}!",
                    $"Утренний кофе готов, {username}?", $"Солнце встало, {username}!", $"Бодрого утра, {username}!",
                    $"Ранняя пташка, {username}!", $"Начинаем день с улыбки, {username}!", $"Пусть утро будет легким, {username}!",
                    $"Светлого утра, {username}!", $"Впереди отличный день, {username}!", $"Завтрак съеден, {username}?",
                    $"Навстречу приключениям, {username}!", $"Утро магии, {username}!", $"Свежий старт, {username}!",
                    $"С первыми лучами солнца, {username}!", $"День начинается сейчас, {username}!", $"Бодрость духа, {username}!",
                    $"Доброе и теплое утро, {username}!", $"Заряжайся позитивом, {username}!", $"Мир просыпается, {username}!",
                    $"Ясное утро, {username}!", $"Утро для великих дел, {username}!", $"Солнечного утра, {username}!",
                    $"Готов свернуть горы, {username}?", $"Отличное начало дня, {username}!", $"Улыбнись новому дню, {username}!",
                    $"Энергичного утра, {username}!", $"Утро туманное, но доброе, {username}!", $"Пора действовать, {username}!",
                    $"Пробуждение системы, {username}!", $"Утро зовет к новым вершинам, {username}!", $"Свежий ветер, {username}!",
                    $"С добрым утром и хорошим днем, {username}!", $"Утренняя прохлада, {username}!", $"Встречай рассвет, {username}!",
                    $"Утренний заряд энергии получен, {username}?", $"На старт, внимание, утро, {username}!", $"Чистый лист, {username}!",
                    $"Утро – время планов, {username}!", $"Позитивного утра, {username}!", $"Пусть все задуманное сбудется, {username}!",
                    $"С первыми петухами, {username}!", $"Утро мудренее вечера, {username}!", $"Волшебного утра, {username}!",
                    $"Лучшее время дня, {username}!", $"Утро приносит надежду, {username}!", $"Удачного старта, {username}!"
                };
                return p[rnd.Next(p.Length)];
            }
            if (hour >= 12 && hour < 16)
            {
                string[] p = {
                    $"Добрый день, {username}!", $"Какой солнечный день, {username}!", $"Отличный день для игры, {username}!",
                    $"День в самом разгаре, {username}!", $"Продуктивного дня, {username}!", $"Экватор дня пройден, {username}?",
                    $"Хорошего дня, {username}!", $"Пусть день пройдет отлично, {username}!", $"Яркого дня, {username}!",
                    $"Как проходит день, {username}?", $"Время обеда, {username}!", $"День полон возможностей, {username}!",
                    $"Замечательный день, {username}!", $"Светлый день, {username}!", $"Середина пути, {username}!",
                    $"Силы еще есть, {username}?", $"Дневной перерыв, {username}!", $"Вдохновляющий день, {username}!",
                    $"Рабочий полдень, {username}!", $"День летит незаметно, {username}!", $"Прекрасный полдень, {username}!",
                    $"Позитивного дня, {username}!", $"Успешного продолжения дня, {username}!", $"Солнце в зените, {username}!",
                    $"Дневная суета, {username}!", $"Наслаждайся моментом, {username}!", $"Превосходный день, {username}!",
                    $"Бодрого дня, {username}!", $"Энергичный день, {username}!", $"Время не ждет, {username}!",
                    $"Твори и побеждай, {username}!", $"Солнечные лучи греют, {username}!", $"День для побед, {username}!",
                    $"Системы работают в штатном режиме, {username}!", $"Хорошего настроения, {username}!", $"Отличного самочувствия, {username}!",
                    $"Дневные задачи, {username}!", $"Время пить чай, {username}!", $"Полдень радует, {username}!",
                    $"Не сбавляй темп, {username}!", $"Всё идет по плану, {username}?", $"Радостного дня, {username}!",
                    $"Пусть день будет успешным, {username}!", $"Врываемся в игру, {username}!", $"Прекрасное время суток, {username}!",
                    $"День наполнен светом, {username}!", $"Отличный настрой, {username}!", $"Дневная доза гейминга, {username}!",
                    $"Сияющий день, {username}!", $"Продолжаем в том же духе, {username}!", $"Всё отлично, {username}!"
                };
                return p[rnd.Next(p.Length)];
            }
            if (hour >= 16 && hour < 21)
            {
                string[] p = {
                    $"Добрый вечер, {username}!", $"Красивый вечер, {username}!", $"Закат прекрасен, {username}?",
                    $"Уютного вечера, {username}!", $"Вечер обещает быть интересным, {username}!", $"Время расслабиться, {username}!",
                    $"Приятного вечера, {username}!", $"Замечательный вечер, {username}!", $"Вечерние игры - лучшее, {username}?",
                    $"Спокойного вечера, {username}!", $"Вечер за окном, {username}!", $"Пора отдыхать, {username}!",
                    $"Прекрасное завершение дня, {username}!", $"Темнеет, {username}!", $"Вечерняя прохлада, {username}!",
                    $"Вечерние сумерки, {username}!", $"День подходит к концу, {username}!", $"Светлого вечера, {username}!",
                    $"Тихий вечер, {username}!", $"Огни города зажигаются, {username}!", $"Чудесного вечера, {username}!",
                    $"Теплого вечера, {username}!", $"Вечерняя атмосфера, {username}!", $"Звездный вечер, {username}!",
                    $"Спокойствие и тишина, {username}!", $"Время итогов, {username}!", $"Отдыхай душой, {username}!",
                    $"Добрый и уютный вечер, {username}!", $"Вечерние огни, {username}!", $"Вечер в кругу друзей, {username}!",
                    $"Превосходный вечер, {username}!", $"Вечерний релакс, {username}!", $"Вечерняя медитация, {username}!",
                    $"Запуск вечерних протоколов, {username}!", $"Система готова к вечеру, {username}!", $"Волшебный вечер, {username}!",
                    $"Легкого вечера, {username}!", $"Снимаем напряжение, {username}!", $"Вечерний чай, {username}!",
                    $"Тишина вечера, {username}!", $"Хорошего окончания дня, {username}!", $"Вечерний покой, {username}!",
                    $"Радостного вечера, {username}!", $"Вечернее настроение, {username}!", $"Закат догорает, {username}!",
                    $"Свет ночных фонарей, {username}!", $"Отличный вечер для побед, {username}!", $"Вечерние разговоры, {username}!",
                    $"Наслаждайся вечером, {username}!", $"Вечерняя гармония, {username}!", $"Теплые мысли, {username}!"
                };
                return p[rnd.Next(p.Length)];
            }
            
            string[] n = {
                $"Доброй ночи, {username}!", $"Сверкающее небо, да, {username}?", $"Звезды ярко светят, {username}!",
                $"Ночной дозор на связи, {username}!", $"Город засыпает, просыпается {username}!", $"Тихой ночи, {username}!",
                $"Самое время для долгих сессий, {username}?", $"Ночь - время чудес, {username}!", $"Луна светит ярко, {username}!",
                $"Спокойной ночи, {username}!", $"Ночные приключения ждут, {username}!", $"Темная ночь, {username}!",
                $"Звездное небо зовет, {username}!", $"Не засиживайся допоздна, {username}!", $"Полуночный гейминг, {username}?",
                $"Ночная тишина, {username}!", $"Ночь глубока, {username}!", $"Мистика ночи, {username}!",
                $"Одинокая луна, {username}!", $"Ночные тени, {username}!", $"Сладких снов, {username}!",
                $"Ночной режим активирован, {username}!", $"Темнота друг молодежи, {username}!", $"Бессонница, {username}?",
                $"Таинственная ночь, {username}!", $"Тихая ночь, {username}!", $"Спокойствие ночи, {username}!",
                $"Ночные огни, {username}!", $"Ночная смена, {username}!", $"Глубокая ночь, {username}!",
                $"Звезды наблюдают, {username}!", $"Млечный путь, {username}!", $"Ночная атмосфера, {username}!",
                $"Переход в ночной режим, {username}...", $"Сон для слабаков, {username}?", $"Волшебной ночи, {username}!",
                $"Ночной бриз, {username}!", $"Середина ночи, {username}!", $"Ночь спокойна, {username}!",
                $"Безмятежной ночи, {username}!", $"Ночной пейзаж, {username}!", $"Уютной ночи, {username}!",
                $"Ночные раздумья, {username}!", $"Ночной эфир, {username}!", $"Свет во тьме, {username}!",
                $"Ночная мгла, {username}!", $"Лунный свет, {username}!", $"Спокойной и тихой ночи, {username}!",
                $"Ночь принадлежит нам, {username}!", $"Ночные тайны, {username}!", $"Звездная пыль, {username}!"
            };
            return n[rnd.Next(n.Length)];
        }

        private string GetRandomQuestion()
        {
            int hour = DateTime.Now.Hour;
            var rnd = new Random();
            var q = new System.Collections.Generic.List<string> {
                "Как дела?", "Что нового?", "Готов к игре?", "Какие планы на игру?",
                "Скилл наточен?", "Готов побеждать?", "Все системы в норме?",
                "Настроение боевое?", "Кликай на ИГРАТЬ!", "Время писать историю!",
                "Ждем только тебя!", "Проверка пинга...", "Инжект модов...", "Синхронизация..."
            };
            
            if (hour >= 16 || hour < 4)
            {
                q.Add("Как прошел день?");
                q.Add("Много фрагов сегодня?");
                q.Add("Устал за день?");
            }
            if (hour >= 4 && hour < 12)
            {
                q.Add("Как спалось?");
                q.Add("Готов к новому дню?");
                q.Add("Утренний раш?");
            }
            return q[rnd.Next(q.Count)];
        }

        private async void StartWelcomeTextLoop()
        {
            string chars = "$?#!*%@^&~";
            var rnd = new Random();
            bool showGreeting = true;
            
            while (true)
            {
                string phrase = showGreeting ? GetRandomGreeting(_settings.Username) : GetRandomQuestion();
                showGreeting = !showGreeting;
                
                await AnimateTerminalText(WelcomeText, phrase);
                
                for (int w = 0; w < 8; w++)
                {
                    WelcomeText.Text = phrase + "_";
                    await Task.Delay(500);
                    WelcomeText.Text = phrase + " ";
                    await Task.Delay(500);
                }
                
                for (int i = phrase.Length - 1; i >= 0; i--)
                {
                    WelcomeText.Text = phrase.Substring(0, i) + chars[rnd.Next(chars.Length)] + "_";
                    await Task.Delay(15);
                    WelcomeText.Text = phrase.Substring(0, i) + "_";
                    await Task.Delay(15);
                }
                WelcomeText.Text = "";
                await Task.Delay(500);
            }
        }

        private async void SwitchToMain()
        {
            SetupPanel.Visibility = Visibility.Hidden; LoginPanel.Visibility = Visibility.Hidden;
            MainPanel.Visibility = Visibility.Visible; TopButtons.Visibility = Visibility.Visible;

            WelcomeText.Text = ""; VersionText.Text = "";

            var ease = new QuarticEase { EasingMode = EasingMode.EaseOut };

            TopButtons.Opacity = 0;
            TopButtons.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(600)) { EasingFunction = ease, BeginTime = TimeSpan.FromMilliseconds(300) });

            BtnPlay.Opacity = 0;
            BtnPlay.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(600)) { EasingFunction = ease, BeginTime = TimeSpan.FromMilliseconds(500) });

            BtnGitHub.Opacity = 0;
            BtnGitHub.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 0.7, TimeSpan.FromMilliseconds(600)) { EasingFunction = ease, BeginTime = TimeSpan.FromMilliseconds(400) });

            InitializeLauncher(); await CheckUpdates();

            if (TopLeftTitleText.Text != "BattleCraft Remake Launcher")
                _ = AnimateTerminalText(TopLeftTitleText, "BattleCraft Remake Launcher");
            _ = AnimateTerminalText(VersionText, $"v{VER}");
            StartWelcomeTextLoop();
        }

        private void LoginGridState() { MainPanel.Visibility = Visibility.Hidden; LoginPanel.Visibility = Visibility.Visible; UsernameBox.Clear(); }

        private void BtnLogin_Click(object s, RoutedEventArgs e)
        { var n = UsernameBox.Text.Trim(); if (string.IsNullOrWhiteSpace(n)) return; _settings.Username = n; AppSettings.Save(_settings); SwitchToMain(); }

        private void BtnGitHub_Click(object s, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo("https://github.com/pers1k1") { UseShellExecute = true });
        }

        private void BtnClose_Click(object s, RoutedEventArgs e)
        {
            _serverManager?.ForceKillProcess();
            Application.Current.Shutdown();
        }
        private void BtnMinimize_Click(object s, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void SetProgress(double v)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(() => SetProgress(v)); return; }
            GameProgressBar.BeginAnimation(ProgressBar.ValueProperty, new DoubleAnimation { To = v, Duration = TimeSpan.FromMilliseconds(250), EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
        }

        private string GetThemeDir() { string p = Path.Combine(_settings.GamePath, "launcher_theme"); if (_settings.HasGamePath && !Directory.Exists(p)) try { Directory.CreateDirectory(p); } catch { } return p; }

        private void ApplyCustomTheme()
        {
            MainWnd.SetResourceReference(Control.BackgroundProperty, "PrimaryBrush");
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
                var t3 = _httpClient.GetStringAsync(SERVER_MODPACK_VER_URL + ts);
                var results = await Task.WhenAll(t1, t2, t3);
                
                string modpackVerStr = results[0].Trim();
                string launcherVerStr = results[1].Trim();
                string serverModpackVerStr = results[2].Trim();

                if (Version.TryParse(modpackVerStr, out var onV) && Version.TryParse(_settings.ModpackVersion, out var loV))
                {
                    _onlineModpackVer = modpackVerStr;
                    if (!_settings.IsModpackInstalled) { BtnPlay.Content = "УСТАНОВИТЬ"; BtnPlay.Background = new SolidColorBrush(Color.FromRgb(220, 150, 30)); }
                    else if (onV > loV) { _needsModpackUpdate = true; BtnPlay.Content = "ОБНОВИТЬ"; BtnPlay.Background = new SolidColorBrush(Color.FromRgb(220, 150, 30)); }
                    else { _needsModpackUpdate = false; SetPlayState("idle"); }
                }

                if (Version.TryParse(serverModpackVerStr, out var onSV) && Version.TryParse(_settings.ServerModpackVersion, out var loSV))
                {
                    _onlineServerModpackVer = serverModpackVerStr;
                    _needsServerModpackUpdate = onSV > loSV;
                    UpdateServerButtons();
                }
                StatusText.Text = $"Модпак v{_settings.ModpackVersion}";
                if (launcherVerStr != VER && await ShowCustomDialog($"Обновить лаунчер до {launcherVerStr}?", "Обновление", true)) await UpdateLauncher();
            }
            catch { StatusText.Text = "Ошибка сети"; }
        }

        private async Task UpdateLauncher()
        {
            StatusText.Text = "Обновление...";
            try
            {
                string dir = AppDomain.CurrentDomain.BaseDirectory, cur = Process.GetCurrentProcess().MainModule!.FileName;
                string tmp = Path.Combine(dir, "upd.exe");
                string old = cur + ".old";
                await new FileDownloader().DownloadFileAsync(LAUNCHER_EXE_URL, tmp);
                
                try { if (File.Exists(old)) File.Delete(old); } catch { }
                File.Move(cur, old);
                File.Move(tmp, cur);
                
                Process.Start(new ProcessStartInfo(cur) { UseShellExecute = true }); 
                Application.Current.Shutdown();
            }
            catch { StatusText.Text = "Ошибка обновления"; }
        }

        private async void BtnReinstall_Click(object s, RoutedEventArgs e)
        {
            if (_isBusy) return;
            if (!_settings.HasGamePath) { await ShowCustomDialog("Сначала выберите папку!"); return; }
            if (await ShowCustomDialog("Перекачать моды?", "Подтверждение", true))
            { SetBusy(true); await InstallModpack(true); Log("Готово!"); StatusText.Text = "Моды переустановлены"; SetPlayState("idle"); SetBusy(false); }
        }

        private void BtnChangeIcon_Click(object s, RoutedEventArgs e)
        { var d = new OpenFileDialog { Filter = "Icon|*.ico" }; if (d.ShowDialog() == true) { try { File.Copy(d.FileName, Path.Combine(GetThemeDir(), "icon.ico"), true); ApplyCustomTheme(); } catch { } } }

        private void BloomEnabledCheck_Changed(object s, RoutedEventArgs e) { if (IsLoaded) ApplyBloom(BloomEnabledCheck.IsChecked == true, BloomStrengthSlider.Value); }
        private void BloomStrengthSlider_ValueChanged(object s, RoutedPropertyChangedEventArgs<double> e) { if (IsLoaded) ApplyBloom(BloomEnabledCheck.IsChecked == true, e.NewValue); }

        private void SettingsScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            e.Handled = true;
            var sv = SettingsScrollViewer;
            if (_scrollTarget < 0 || !_scrolling) _scrollTarget = sv.VerticalOffset;
            _scrollTarget = Math.Clamp(_scrollTarget - e.Delta * 0.5, 0, sv.ScrollableHeight);
            if (!_scrolling) { _scrolling = true; CompositionTarget.Rendering += ScrollTick; }
        }

        private void ScrollTick(object? s, EventArgs e)
        {
            var sv = SettingsScrollViewer;
            double cur = sv.VerticalOffset, diff = _scrollTarget - cur;
            if (Math.Abs(diff) < 0.5) { sv.ScrollToVerticalOffset(_scrollTarget); _scrolling = false; CompositionTarget.Rendering -= ScrollTick; return; }
            sv.ScrollToVerticalOffset(cur + diff * 0.25);
        }

        private void SettingsScroll_ScrollChanged(object s, ScrollChangedEventArgs e)
        { if (ColorPresetCombo != null && ColorPresetCombo.IsDropDownOpen) ColorPresetCombo.IsDropDownOpen = false; }

        public void Btn_MouseTrack(object sender, MouseEventArgs e)
        {
            var btn = (Button)sender;
            if (btn.Template.FindName("BtnTr", btn) is TranslateTransform t)
            {
                if (btn.ActualWidth <= 0 || btn.ActualHeight <= 0) return;
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
                if (double.IsInfinity(t.X) || double.IsNaN(t.X)) t.X = 0;
                if (double.IsInfinity(t.Y) || double.IsNaN(t.Y)) t.Y = 0;
                var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                t.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(300)) { EasingFunction = ease });
                t.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(300)) { EasingFunction = ease });
            }
        }

        private void NavPlay_Click(object s, RoutedEventArgs e)
        {
            if (!_isServerTab) return;
            _isServerTab = false;
            UpdateNavHighlight();
            PlayContentPanel.Visibility = Visibility.Visible;
            ServerContentPanel.Visibility = Visibility.Collapsed;

            TopButtons.Visibility = Visibility.Visible;
            BtnGitHub.Visibility = Visibility.Visible;
            SysMonitorRich.Visibility = Visibility.Visible;
        }

        private void NavServer_Click(object s, RoutedEventArgs e)
        {
            if (_isServerTab) return;
            _isServerTab = true;
            UpdateNavHighlight();
            PlayContentPanel.Visibility = Visibility.Collapsed;
            ServerContentPanel.Visibility = Visibility.Visible;

            TopButtons.Visibility = Visibility.Collapsed;
            BtnGitHub.Visibility = Visibility.Collapsed;
            SysMonitorRich.Visibility = Visibility.Collapsed;
            LoadServerList();
        }

        private void UpdateNavHighlight()
        {
            if (_isServerTab)
            {
                NavPlayBtn.BorderBrush = new SolidColorBrush(Colors.Transparent);
                NavPlayBtn.Foreground = new SolidColorBrush(Color.FromArgb(136, 255, 255, 255));
                NavServerBtn.SetResourceReference(Control.BorderBrushProperty, "AccentBrush");
                NavServerBtn.Foreground = new SolidColorBrush(Colors.White);
            }
            else
            {
                NavServerBtn.BorderBrush = new SolidColorBrush(Colors.Transparent);
                NavServerBtn.Foreground = new SolidColorBrush(Color.FromArgb(136, 255, 255, 255));
                NavPlayBtn.SetResourceReference(Control.BorderBrushProperty, "AccentBrush");
                NavPlayBtn.Foreground = new SolidColorBrush(Colors.White);
            }
        }

        private void LoadServerList()
        {
            ServerSelector.SelectionChanged -= ServerSelector_Changed;
            ServerSelector.Items.Clear();

            foreach (var serverConfig in _settings.Servers)
                ServerSelector.Items.Add(new ComboBoxItem { Content = serverConfig.Name, Tag = serverConfig.Name });

            if (_settings.Servers.Count > 0)
            {
                int targetIndex = _settings.Servers.FindIndex(sc => sc.Name == _settings.LastActiveServerName);
                ServerSelector.SelectedIndex = targetIndex >= 0 ? targetIndex : 0;
            }

            ServerSelector.SelectionChanged += ServerSelector_Changed;
            LoadSelectedServerConfig();
        }

        private void ServerSelector_Changed(object s, SelectionChangedEventArgs e)
        {
            SaveActiveServerConfig();
            LoadSelectedServerConfig();
        }

        private void LoadSelectedServerConfig()
        {
            if (ServerSelector.SelectedItem is not ComboBoxItem selectedItem)
            {
                _activeServerConfig = null;
                ServerConfigForm.IsEnabled = false;
                ServerConfigConsoleGrid.Visibility = Visibility.Collapsed;
                UpdateServerButtons();
                return;
            }

            string selectedName = selectedItem.Tag as string ?? "";
            _activeServerConfig = _settings.Servers.FirstOrDefault(sc => sc.Name == selectedName);

            if (_activeServerConfig == null)
            {
                ServerConfigForm.IsEnabled = false;
                ServerConfigConsoleGrid.Visibility = Visibility.Collapsed;
                UpdateServerButtons();
                return;
            }

            ServerConfigConsoleGrid.Visibility = Visibility.Visible;
            ServerConfigForm.IsEnabled = true;
            _settings.LastActiveServerName = _activeServerConfig.Name;

            ServerMotdBox.Text = _activeServerConfig.Motd;
            ServerMaxPlayersBox.Text = _activeServerConfig.MaxPlayers.ToString();
            ServerPortBox.Text = _activeServerConfig.ServerPort.ToString();
            ServerIpBox.Text = _activeServerConfig.ServerIp;
            ServerViewDistanceSlider.Value = _activeServerConfig.ViewDistance;
            ServerRamSlider.Value = _activeServerConfig.ServerRamMb;
            ServerSpawnAnimalsCheck.IsChecked = _activeServerConfig.SpawnAnimals;
            ServerSpawnMonstersCheck.IsChecked = _activeServerConfig.SpawnMonsters;
            ServerOnlineModeCheck.IsChecked = _activeServerConfig.OnlineMode;
            ServerWhitelistCheck.IsChecked = _activeServerConfig.WhitelistEnabled;
            ServerEulaCheck.IsChecked = _activeServerConfig.EulaAccepted;

            WhitelistPanel.Visibility = _activeServerConfig.WhitelistEnabled
                ? Visibility.Visible
                : Visibility.Collapsed;

            RebuildWhitelistUi();
            UpdateServerButtons();
        }

        private void SaveActiveServerConfig()
        {
            if (_activeServerConfig == null) return;

            _activeServerConfig.Motd = ServerMotdBox.Text.Trim();

            if (int.TryParse(ServerMaxPlayersBox.Text, out int maxPlayers))
                _activeServerConfig.MaxPlayers = maxPlayers;

            if (int.TryParse(ServerPortBox.Text, out int port))
                _activeServerConfig.ServerPort = port;

            _activeServerConfig.ServerIp = ServerIpBox.Text.Trim();
            _activeServerConfig.ViewDistance = (int)ServerViewDistanceSlider.Value;
            _activeServerConfig.ServerRamMb = (int)ServerRamSlider.Value;
            _activeServerConfig.SpawnAnimals = ServerSpawnAnimalsCheck.IsChecked == true;
            _activeServerConfig.SpawnMonsters = ServerSpawnMonstersCheck.IsChecked == true;
            _activeServerConfig.OnlineMode = ServerOnlineModeCheck.IsChecked == true;
            _activeServerConfig.WhitelistEnabled = ServerWhitelistCheck.IsChecked == true;
            _activeServerConfig.EulaAccepted = ServerEulaCheck.IsChecked == true;

            AppSettings.Save(_settings);

            if (_activeServerConfig.IsInstalled)
            {
                ServerManager.GenerateServerProperties(_activeServerConfig);
                if (_activeServerConfig.WhitelistEnabled)
                    ServerManager.GenerateWhitelistJson(_activeServerConfig);
            }
        }

        private async void BtnCreateServer_Click(object s, RoutedEventArgs e)
        {
            string? serverName = await ShowInputDialogAsync("Имя нового сервера", $"Server {_settings.Servers.Count + 1}");
            if (serverName == null) return;

            bool nameExists = _settings.Servers.Any(sc => sc.Name.Equals(serverName, StringComparison.OrdinalIgnoreCase));
            if (nameExists)
            {
                await ShowCustomDialog("Сервер с таким именем уже существует!");
                return;
            }

            string sanitizedName = ServerManager.SanitizePathSegment(serverName);

            string defaultServerBasePath = _settings.HasGamePath
                ? Path.Combine(_settings.GamePath, "servers", sanitizedName)
                : "";

            var newConfig = new ServerConfig
            {
                Name = serverName,
                ServerPath = defaultServerBasePath
            };

            _settings.Servers.Add(newConfig);
            _settings.LastActiveServerName = serverName;
            AppSettings.Save(_settings);

            LoadServerList();
        }

        private async void BtnDeleteServer_Click(object s, RoutedEventArgs e)
        {
            if (_activeServerConfig == null) return;

            bool isRunning = _serverManager != null && _serverManager.CurrentState != ServerState.Stopped;
            if (isRunning)
            {
                await ShowCustomDialog("Сначала остановите сервер!");
                return;
            }

            bool confirmed = await ShowCustomDialog(
                $"Удалить сервер '{_activeServerConfig.Name}'? Файлы на диске БУДУТ удалены.",
                "Подтверждение", true);

            if (!confirmed) return;

            string serverBasePath = _activeServerConfig.ServerPath;

            _settings.Servers.RemoveAll(sc => sc.Name == _activeServerConfig.Name);
            _activeServerConfig = null;
            AppSettings.Save(_settings);

            if (!string.IsNullOrWhiteSpace(serverBasePath) && Directory.Exists(serverBasePath))
            {
                try { Directory.Delete(serverBasePath, true); } catch { }
            }

            LoadServerList();
        }

        private async void BtnInstallServer_Click(object s, RoutedEventArgs e)
        {
            if (_activeServerConfig == null || _isServerBusy) return;

            SaveActiveServerConfig();

            if (!_activeServerConfig.EulaAccepted)
            {
                await ShowCustomDialog("Необходимо принять EULA!");
                return;
            }

            if (string.IsNullOrWhiteSpace(_activeServerConfig.ServerPath))
            {
                await ShowCustomDialog("Путь к серверу не задан!");
                return;
            }

            SetServerBusy(true, "Установка сервера...");

            try
            {
                string javaPath = FindJava();
                var installer = new ServerInstaller();
                installer.StatusChanged += OnInstallerStatusChanged;
                installer.LogReceived += AppendConsoleOutput;
                await installer.InstallAsync(_activeServerConfig.ServerPath, javaPath, OnServerProgress);

                _activeServerConfig.IsInstalled = true;
                _settings.ServerModpackVersion = _onlineServerModpackVer;
                _needsServerModpackUpdate = false;
                AppSettings.Save(_settings);
                
                UpdateServerButtons();

                AppendConsoleOutput("[SYS] Сервер установлен.");
            }
            catch (Exception ex)
            {
                AppendConsoleOutput($"[ERR] {ex.Message}");
                await ShowCustomDialog($"Ошибка установки: {ex.Message}");
            }
            finally
            {
                SetServerBusy(false);
            }
        }

        private void OnInstallerStatusChanged(string statusMessage)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(() => OnInstallerStatusChanged(statusMessage));
                return;
            }

            ServerStatusText.Text = statusMessage;
            AppendConsoleOutput($"[SYS] {statusMessage}");
        }

        private async void BtnUpdateServerMods_Click(object s, RoutedEventArgs e)
        {
            if (_activeServerConfig == null || _isServerBusy) return;
            SetServerBusy(true, "Обновление модов...");
            try
            {
                var installer = new ServerInstaller();
                installer.StatusChanged += OnInstallerStatusChanged;
                await installer.UpdateServerMods(_activeServerConfig.ServerPath, OnServerProgress);
                _settings.ServerModpackVersion = _onlineServerModpackVer;
                _needsServerModpackUpdate = false;
                AppSettings.Save(_settings);
                AppendConsoleOutput("[SYS] Моды сервера обновлены.");
            }
            catch (Exception ex)
            {
                AppendConsoleOutput($"[ERR] {ex.Message}");
                await ShowCustomDialog($"Ошибка обновления модов: {ex.Message}");
            }
            finally
            {
                SetServerBusy(false);
            }
        }

        private void EnsureServerManagerInitialized()
        {
            if (_serverManager != null) return;

            _serverManager = new ServerManager();
            _serverManager.OutputReceived += AppendConsoleOutput;
            _serverManager.StateChanged += OnServerStateChanged;
        }

        private async void BtnStartServer_Click(object s, RoutedEventArgs e)
        {
            if (_activeServerConfig == null || _isServerBusy) return;

            try
            {
                SaveActiveServerConfig();
                EnsureServerManagerInitialized();

                ServerConsoleOutput.Text = "";
                AppendConsoleOutput("[SYS] Запуск сервера...");
                UpdateServerButtons();

                string javaPath = FindJava();
                await _serverManager.StartAsync(_activeServerConfig, javaPath);
            }
            catch (Exception ex)
            {
                AppendConsoleOutput($"[ERR] {ex.Message}");
            }
            finally
            {
                UpdateServerButtons();
            }
        }

        private async void BtnStopServer_Click(object s, RoutedEventArgs e)
        {
            if (_serverManager == null) return;
            try
            {
                AppendConsoleOutput("[SYS] Остановка сервера...");
                await _serverManager.StopAsync();
            }
            catch (Exception ex)
            {
                AppendConsoleOutput($"[ERR] Ошибка при остановке: {ex.Message}");
            }
        }

        private async void BtnRestartServer_Click(object s, RoutedEventArgs e)
        {
            if (_serverManager == null || _activeServerConfig == null) return;
            try
            {
                AppendConsoleOutput("[SYS] Перезапуск сервера...");
                string javaPath = FindJava();
                await _serverManager.RestartAsync(_activeServerConfig, javaPath);
            }
            catch (Exception ex)
            {
                AppendConsoleOutput($"[ERR] Ошибка при перезапуске: {ex.Message}");
            }
        }

        private async void BtnRestoreBackup_Click(object s, RoutedEventArgs e)
        {
            if (_activeServerConfig == null) return;

            bool isRunning = _serverManager != null && _serverManager.CurrentState != ServerState.Stopped;
            if (isRunning)
            {
                await ShowCustomDialog("Сначала остановите сервер!");
                return;
            }

            string backupDir = Path.Combine(_activeServerConfig.ServerPath, "backup");
            if (!Directory.Exists(backupDir) || Directory.GetFileSystemEntries(backupDir).Length == 0)
            {
                await ShowCustomDialog("Локальный бэкап не найден! Переустановите сервер.");
                return;
            }

            bool confirmed = await ShowCustomDialog(
                "Восстановить мир из локального бэкапа? Текущий мир будет перезаписан.",
                "Подтверждение", true);

            if (!confirmed) return;

            SetServerBusy(true, "Восстановление мира...");

            try
            {
                string serverDir = Path.Combine(_activeServerConfig.ServerPath, "server");
                await Task.Run(() => ServerInstaller.CopyDirectoryContents(backupDir, serverDir));
                AppendConsoleOutput("[SYS] Мир восстановлен из локального бэкапа.");
            }
            catch (Exception ex)
            {
                await ShowCustomDialog($"Ошибка восстановления: {ex.Message}");
            }
            finally
            {
                SetServerBusy(false);
            }
        }

        private void BtnOpenServerFolder_Click(object s, RoutedEventArgs e)
        {
            if (_activeServerConfig == null || string.IsNullOrWhiteSpace(_activeServerConfig.ServerPath)) return;

            string serverDirectory = Path.Combine(_activeServerConfig.ServerPath, "server");
            string targetDirectory = Directory.Exists(serverDirectory) ? serverDirectory : _activeServerConfig.ServerPath;

            if (Directory.Exists(targetDirectory))
                Process.Start(new ProcessStartInfo(targetDirectory) { UseShellExecute = true });
        }

        private void BtnSendCommand_Click(object s, RoutedEventArgs e)
        {
            SendConsoleCommand();
        }

        private void ServerConsoleInput_KeyDown(object s, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                SendConsoleCommand();
        }

        private void SendConsoleCommand()
        {
            string command = ServerConsoleInput.Text.Trim();
            if (string.IsNullOrWhiteSpace(command) || _serverManager == null) return;

            AppendConsoleOutput($"> {command}");
            _serverManager.SendCommand(command);
            ServerConsoleInput.Text = "";
        }

        private void AppendConsoleOutput(string line)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(() => AppendConsoleOutput(line));
                return;
            }

            ServerConsoleOutput.AppendText(line + "\n");

            if (ServerConsoleOutput.Text.Length > MAX_CONSOLE_CHARS)
                ServerConsoleOutput.Text = ServerConsoleOutput.Text.Substring(ServerConsoleOutput.Text.Length - MAX_CONSOLE_CHARS / 2);

            ServerConsoleOutput.ScrollToEnd();
        }

        private void OnServerStateChanged(ServerState newState)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(() => OnServerStateChanged(newState));
                return;
            }

            string stateLabel = newState switch
            {
                ServerState.Starting => "Запуск...",
                ServerState.Running => "Работает",
                ServerState.Stopping => "Остановка...",
                _ => "Остановлен"
            };

            ServerStatusText.Text = stateLabel;
            UpdateServerButtons();
        }

        private void OnServerProgress(double progressPercent)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(() => OnServerProgress(progressPercent));
                return;
            }

            ServerProgressBar.Value = progressPercent;
        }

        private void UpdateServerButtons()
        {
            bool hasConfig = _activeServerConfig != null;
            bool isInstalled = hasConfig && (_activeServerConfig!.IsInstalled || ServerInstaller.IsInstalled(_activeServerConfig.ServerPath));
            bool isRunning = _serverManager != null && _serverManager.CurrentState == ServerState.Running;
            bool isStopping = _serverManager != null && _serverManager.CurrentState == ServerState.Stopping;
            bool isStopped = _serverManager == null || _serverManager.CurrentState == ServerState.Stopped;

            BtnInstallServer.Visibility = hasConfig && !isInstalled ? Visibility.Visible : Visibility.Collapsed;
            BtnInstallServer.IsEnabled = hasConfig && _activeServerConfig!.EulaAccepted && !_isServerBusy;

            BtnUpdateServerMods.Visibility = hasConfig && isInstalled && isStopped && _needsServerModpackUpdate ? Visibility.Visible : Visibility.Collapsed;
            BtnUpdateServerMods.IsEnabled = !_isServerBusy;

            BtnStartServer.Visibility = isInstalled && isStopped ? Visibility.Visible : Visibility.Collapsed;
            BtnStartServer.IsEnabled = !_isServerBusy;
            BtnStopServer.Visibility = isRunning ? Visibility.Visible : Visibility.Collapsed;
            BtnRestartServer.Visibility = isRunning ? Visibility.Visible : Visibility.Collapsed;
            BtnRestoreBackup.Visibility = isInstalled && isStopped ? Visibility.Visible : Visibility.Collapsed;
            BtnRestoreBackup.IsEnabled = !_isServerBusy;
            BtnOpenServerFolder.Visibility = isInstalled ? Visibility.Visible : Visibility.Collapsed;

            ServerConfigForm.IsEnabled = hasConfig && !_isServerBusy;
            TabGeneralContent.IsEnabled = !isRunning && !isStopping;
            TabGeneralContent.ToolTip = (isRunning || isStopping) ? "Остановите сервер, чтобы взаимодействовать" : null;
        }

        private void SetServerBusy(bool busy, string statusMessage = "")
        {
            _isServerBusy = busy;
            ServerStatusText.Text = statusMessage;
            ServerProgressBar.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            ServerProgressBar.Value = 0;
            UpdateServerButtons();
        }

        private void ServerWhitelistCheck_Changed(object s, RoutedEventArgs e)
        {
            if (!IsLoaded) return;
            WhitelistPanel.Visibility = ServerWhitelistCheck.IsChecked == true
                ? Visibility.Visible
                : Visibility.Collapsed;
            
            SaveActiveServerConfig();
        }

        private void TabGeneralBtn_Click(object s, RoutedEventArgs e)
        {
            TabGeneralContent.Visibility = Visibility.Visible;
            TabWhitelistContent.Visibility = Visibility.Collapsed;
        }

        private void TabWhitelistBtn_Click(object s, RoutedEventArgs e)
        {
            TabGeneralContent.Visibility = Visibility.Collapsed;
            TabWhitelistContent.Visibility = Visibility.Visible;
        }

        private void ServerEulaCheck_Changed(object s, RoutedEventArgs e)
        {
            if (!IsLoaded || _activeServerConfig == null) return;
            _activeServerConfig.EulaAccepted = ServerEulaCheck.IsChecked == true;
            UpdateServerButtons();
        }

        private async void BtnAddWhitelistPlayer_Click(object s, RoutedEventArgs e)
        {
            string playerName = WhitelistPlayerNameBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(playerName) || _activeServerConfig == null) return;

            bool alreadyExists = _activeServerConfig.WhitelistedPlayers
                .Any(p => p.Equals(playerName, StringComparison.OrdinalIgnoreCase));

            if (alreadyExists) return;

            _activeServerConfig.WhitelistedPlayers.Add(playerName);
            WhitelistPlayerNameBox.Text = "";
            RebuildWhitelistUi();
            AppSettings.Save(_settings);

            bool isRunning = _serverManager != null && _serverManager.CurrentState != ServerState.Stopped;
            if (isRunning)
            {
                await ShowCustomDialog("Перезапустите сервер, чтобы изменения применились");
            }
        }

        private async void RemoveWhitelistPlayer(string playerName)
        {
            if (_activeServerConfig == null) return;
            _activeServerConfig.WhitelistedPlayers.RemoveAll(
                p => p.Equals(playerName, StringComparison.OrdinalIgnoreCase));
            RebuildWhitelistUi();
            AppSettings.Save(_settings);

            bool isRunning = _serverManager != null && _serverManager.CurrentState != ServerState.Stopped;
            if (isRunning)
            {
                await ShowCustomDialog("Перезапустите сервер, чтобы изменения применились");
            }
        }

        private void RebuildWhitelistUi()
        {
            WhitelistPlayersContainer.Children.Clear();

            if (_activeServerConfig == null) return;

            foreach (string playerName in _activeServerConfig.WhitelistedPlayers)
            {
                var playerRow = CreateWhitelistPlayerRow(playerName);
                WhitelistPlayersContainer.Children.Add(playerRow);
            }
        }

        private Grid CreateWhitelistPlayerRow(string playerName)
        {
            var rowGrid = new Grid { Margin = new Thickness(8, 3, 8, 3) };
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var nameBlock = new TextBlock
            {
                Text = playerName,
                Foreground = new SolidColorBrush(Color.FromArgb(204, 255, 255, 255)),
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 12
            };

            var removeButton = new Button
            {
                Content = "x",
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(255, 107, 107)),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                Padding = new Thickness(6, 2, 6, 2)
            };

            string capturedName = playerName;
            removeButton.Click += (s, e) => RemoveWhitelistPlayer(capturedName);

            Grid.SetColumn(nameBlock, 0);
            Grid.SetColumn(removeButton, 1);
            rowGrid.Children.Add(nameBlock);
            rowGrid.Children.Add(removeButton);

            return rowGrid;
        }
    }
}
