using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.Auth.Microsoft;
using CmlLib.Core.Installers;
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
using System.Runtime.InteropServices;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
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
        private long _lastProgressTick;
        private long _lastByteTick;
        private long _lastByteCount;
        private long _lastByteSpeedTick;
        private long _lastNetLogTick;

        private ServerManager? _serverManager;
        private ServerConfig? _activeServerConfig;
        private bool _isServerTab;
        private bool _isServerBusy;
        private readonly System.Collections.Concurrent.ConcurrentQueue<string> _consoleQueue = new();
        private System.Windows.Threading.DispatcherTimer? _consoleFlushTimer;
        private const int MAX_CONSOLE_CHARS = 100000;
        private DiscordManager _discordManager = new();

        private class StatusTextDummy
        {
            private MainWindow _w;
            public StatusTextDummy(MainWindow w) { _w = w; }
            public string Text { set { _w.Log(value); } get { return ""; } }
        }
        private StatusTextDummy StatusText => new StatusTextDummy(this);

        private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(10) };

        private const string VER = "7.7";
        private const string MC = "1.20.1";
        private const string FORGE = "47.4.20";
        private const string FULL_ID = MC + "-forge-" + FORGE;
        private const string DefPrimary = "#0D0D1E";
        private const string DefAccent = "#BB86FC";
        private const string MODPACK_VER_URL = "https://raw.githubusercontent.com/pers1k1/vrsns/main/modpack_version.txt";
        private const string LAUNCHER_VER_URL = "https://raw.githubusercontent.com/pers1k1/vrsns/main/launcher_version.txt";
        private const string SERVER_MODPACK_VER_URL = "https://raw.githubusercontent.com/pers1k1/vrsns/main/server_modpack_version.txt";
        private const string SERVER_MAP_VER_URL = "https://raw.githubusercontent.com/pers1k1/vrsns/main/server_map_version.txt";
        private const string MODPACK_URL = "https://github.com/pers1k1/modpack/releases/download/main/release.zip";
        private const string LAUNCHER_EXE_URL = "https://github.com/pers1k1/BattleCraft-Remake/releases/download/main/BCR.exe";
        private static readonly string FORGE_JAR_URL = $"https://maven.minecraftforge.net/net/minecraftforge/forge/{MC}-{FORGE}/forge-{MC}-{FORGE}-installer.jar";
        private string _onlineModpackVer = "0.0";
        private string _onlineServerModpackVer = "0.0";
        private string _onlineServerMapVer = "0.0";
        private bool _needsServerModpackUpdate = false;
        private bool _needsServerMapUpdate = false;
        private bool _waitingForPortKillConfirmation = false;
        private bool _welcomeLoopStarted = false;
        private static readonly Random _rnd = new();

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
            LauncherLog.Init();
            InitializeComponent();
            _consoleFlushTimer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(100) };
            _consoleFlushTimer.Tick += (s, e) => FlushConsoleQueue();
            _consoleFlushTimer.Start();
            InitializeLauncherCore();
            InitPixelWorld();
            StateChanged += (s, e) => UpdateSceneAnimation();
            Activated += (s, e) => UpdateSceneAnimation();
            Deactivated += (s, e) => UpdateSceneAnimation();
            _ = BootSequenceAsync();

            ResilientHttpClientFactory.DownloadRetry += notice => Log(notice);
        }

        protected override void OnClosed(EventArgs e)
        {
            try { _worldTimer?.Stop(); _weatherTimer?.Stop(); } catch { }
            _discordManager?.Dispose();
            base.OnClosed(e);
        }

        #region Пиксельный мир (фон, время суток, погода)

        private readonly Random _sceneRng = new();

        private enum Weather { Clear, Wind, Rain, Snow, Fog, Comets, Sakura, Leaves }
        private Weather _weather = Weather.Clear;

        private const int SCN_W = 200;
        private const int SCN_H = 118;
        private const int HORIZON = 74;
        private WriteableBitmap? _sceneBmp;
        private byte[] _sceneBuf = new byte[SCN_W * SCN_H * 4];
        private System.Windows.Threading.DispatcherTimer? _worldTimer;
        private System.Windows.Threading.DispatcherTimer? _weatherTimer;
        private bool _sceneRunning = true;
        private int _frame;
        private double CurHour() => DateTime.Now.Hour + DateTime.Now.Minute / 60.0;

        private readonly int[] _mtnFar = new int[SCN_W];
        private readonly int[] _mtnNear = new int[SCN_W];
        private readonly int[] _forest = new int[SCN_W];

        private int[] _starX = Array.Empty<int>(), _starY = Array.Empty<int>();
        private double[] _starP = Array.Empty<double>();

        private double[] _cloudX = Array.Empty<double>(), _cloudY = Array.Empty<double>(), _cloudW = Array.Empty<double>();

        private double[] _pX = Array.Empty<double>(), _pY = Array.Empty<double>(), _pP = Array.Empty<double>();

        private double _cometX, _cometY; private int _cometLife;

        private void InitPixelWorld()
        {
            try
            {
                _sceneBmp = new WriteableBitmap(SCN_W, SCN_H, 96, 96, PixelFormats.Bgra32, null);
                if (PixelSceneImage != null) PixelSceneImage.Source = _sceneBmp;
                InitBlob();

                for (int x = 0; x < SCN_W; x++)
                {
                    _mtnFar[x] = (int)(HORIZON - 18 - 12 * Math.Sin(x * 0.045) - 6 * Math.Sin(x * 0.11 + 2));
                    _mtnNear[x] = (int)(HORIZON - 6 - 9 * Math.Sin(x * 0.06 + 1.3) - 5 * Math.Sin(x * 0.15 + 4));
                    _forest[x] = (int)(SCN_H - 14 - 4 * Math.Sin(x * 0.5) - 3 * Math.Sin(x * 0.9 + 1) - (_sceneRng.NextDouble() < 0.12 ? 4 : 0));
                }

                int sc = 70; _starX = new int[sc]; _starY = new int[sc]; _starP = new double[sc];
                for (int i = 0; i < sc; i++) { _starX[i] = _sceneRng.Next(SCN_W); _starY[i] = _sceneRng.Next(HORIZON - 14); _starP[i] = _sceneRng.NextDouble() * 6.28; }

                int cc = 4; _cloudX = new double[cc]; _cloudY = new double[cc]; _cloudW = new double[cc];
                for (int i = 0; i < cc; i++) { _cloudX[i] = _sceneRng.Next(SCN_W); _cloudY[i] = 10 + _sceneRng.Next(28); _cloudW[i] = 14 + _sceneRng.Next(16); }

                RollWeather();

                _worldTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
                _worldTimer.Tick += (s, e) => WorldTick();
                _worldTimer.Start();

                _weatherTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMinutes(15) };
                _weatherTimer.Tick += (s, e) => RollWeather();
                _weatherTimer.Start();

                WorldTick();
            }
            catch { }
        }

        private void UpdateSceneAnimation()
        {
            bool shouldRun = WindowState != WindowState.Minimized && IsActive;
            if (shouldRun == _sceneRunning) return;
            _sceneRunning = shouldRun;
            if (shouldRun) { _worldTimer?.Start(); _weatherTimer?.Start(); }
            else { _worldTimer?.Stop(); _weatherTimer?.Stop(); }
        }

        private void RollWeather()
        {
            int hour = DateTime.Now.Hour;
            bool night = hour >= 21 || hour < 4;
            int month = DateTime.Now.Month;
            bool winter = month == 12 || month <= 2;
            bool autumn = month >= 9 && month <= 11;
            bool spring = month >= 3 && month <= 5;

            if (night && _sceneRng.NextDouble() < 0.18) { _weather = Weather.Comets; SetupParticles(); return; }

            var pool = new List<Weather>();
            void Add(Weather w, int n) { for (int i = 0; i < n; i++) pool.Add(w); }
            Add(Weather.Clear, 34);
            Add(Weather.Wind, 16);
            Add(Weather.Fog, 8 + (autumn ? 8 : 0) + (hour >= 5 && hour < 9 ? 6 : 0));
            Add(Weather.Rain, winter ? 4 : (spring || autumn ? 22 : 15));
            Add(Weather.Snow, winter ? 32 : (month == 11 ? 12 : 0));
            Add(Weather.Sakura, spring ? 22 : 0);
            Add(Weather.Leaves, autumn ? 24 : (month == 9 ? 10 : 0));
            _weather = pool[_sceneRng.Next(pool.Count)];
            SetupParticles();
        }

        private void SetupParticles()
        {
            int n = _weather switch { Weather.Rain => 95, Weather.Snow => 70, Weather.Sakura => 55, Weather.Leaves => 50, _ => 0 };
            _pX = new double[n]; _pY = new double[n]; _pP = new double[n];
            bool leaves = _weather == Weather.Leaves;
            for (int i = 0; i < n; i++)
            {
                _pX[i] = _sceneRng.Next(SCN_W);

                _pY[i] = leaves ? (HORIZON - 8 + _sceneRng.NextDouble() * (SCN_H - HORIZON + 8)) : _sceneRng.Next(SCN_H);
                _pP[i] = _sceneRng.NextDouble() * 6.28;
            }
            _cometLife = 0;
        }

        private void WorldTick()
        {
            _frame++;
            _blobT += 0.08;
            RenderScene();
            RenderBlob();
        }

        private void TimeColors(out (int r, int g, int b) top, out (int r, int g, int b) horiz, out double bright, out bool night)
        {
            double h = CurHour();

            if (h >= 4 && h < 12)        { top = (42, 35, 86); horiz = (236, 132, 78); bright = 0.84; night = false; }
            else if (h >= 12 && h < 16)  { top = (58, 104, 170); horiz = (150, 188, 224); bright = 1.0; night = false; }
            else if (h >= 16 && h < 21)  { top = (40, 28, 78); horiz = (224, 92, 40); bright = 0.82; night = false; }
            else                         { top = (10, 10, 32); horiz = (30, 24, 64); bright = 0.5; night = true; }
        }

        private void RenderScene()
        {
            if (_sceneBmp == null) return;
            try
            {
                TimeColors(out var top, out var horiz, out var bright, out var night);

                for (int y = 0; y < HORIZON; y++)
                {
                    double t = (double)y / HORIZON;
                    byte r = (byte)(top.r + (horiz.r - top.r) * t);
                    byte g = (byte)(top.g + (horiz.g - top.g) * t);
                    byte b = (byte)(top.b + (horiz.b - top.b) * t);
                    for (int x = 0; x < SCN_W; x++) SP(x, y, r, g, b);
                }

                for (int y = HORIZON; y < SCN_H; y++)
                    for (int x = 0; x < SCN_W; x++) SP(x, y, (byte)(20 * bright), (byte)(16 * bright), (byte)(30 * bright));

                if (night)
                {
                    for (int i = 0; i < _starX.Length; i++)
                    {
                        double tw = 0.4 + 0.6 * Math.Abs(Math.Sin(_frame * 0.12 + _starP[i]));
                        byte v = (byte)(220 * tw);
                        SP(_starX[i], _starY[i], v, v, (byte)(255 * tw));
                    }
                }

                DrawSunMoon(night);

                double cloudSpd = _weather == Weather.Wind ? 0.9 : 0.18;
                for (int i = 0; i < _cloudX.Length; i++)
                {
                    _cloudX[i] += cloudSpd;
                    if (_cloudX[i] > SCN_W + _cloudW[i]) _cloudX[i] = -_cloudW[i];
                    DrawCloud((int)_cloudX[i], (int)_cloudY[i], (int)_cloudW[i], bright, night);
                }

                for (int x = 0; x < SCN_W; x++)
                {
                    for (int y = Math.Max(0, _mtnFar[x]); y < HORIZON; y++) SP(x, y, (byte)(70 * bright), (byte)(58 * bright), (byte)(104 * bright));
                    for (int y = Math.Max(0, _mtnNear[x]); y < HORIZON; y++) SP(x, y, (byte)(40 * bright), (byte)(30 * bright), (byte)(66 * bright));
                }
                for (int y = HORIZON; y < SCN_H; y++)
                    for (int x = 0; x < SCN_W; x++) SP(x, y, (byte)(18 * bright), (byte)(14 * bright), (byte)(28 * bright));
                for (int x = 0; x < SCN_W; x++)
                    for (int y = Math.Max(HORIZON, _forest[x]); y < SCN_H; y++) SP(x, y, (byte)(8 * bright), (byte)(7 * bright), (byte)(16 * bright));

                bool badWeather = _weather == Weather.Rain || _weather == Weather.Snow;
                if (!badWeather) DrawKite();

                switch (_weather)
                {
                    case Weather.Rain: UpdateRain(); break;
                    case Weather.Snow: UpdateSnow(); break;
                    case Weather.Wind: UpdateWind(); break;
                    case Weather.Fog: DrawFog(); break;
                    case Weather.Comets: if (night) UpdateComets(); break;
                    case Weather.Sakura: UpdateSakura(); break;
                    case Weather.Leaves: UpdateLeaves(); break;
                }

                if (badWeather) DrawUmbrellaPerson(night);

                _sceneBmp.WritePixels(new Int32Rect(0, 0, SCN_W, SCN_H), _sceneBuf, SCN_W * 4, 0);
            }
            catch { }
        }

        private void DrawSunMoon(bool night)
        {
            double h = CurHour();
            if (!night)
            {

                double frac = Math.Min(1, Math.Max(0, (h - 4) / 17.0));
                int cx = (int)(frac * (SCN_W - 1));
                int cy = (int)(HORIZON - Math.Sin(frac * Math.PI) * (HORIZON - 10));
                double pulse = (Math.Sin(_frame * 0.13) + 1) / 2.0;
                bool dawn = h < 12.0;
                bool sunset = h >= 16.0;

                if (dawn)
                {

                    HorizonGlow(cx, 255, 180, 120, 0.5);
                    DrawDisc(cx, cy, 9, 255, 236, 170, 0.20 + 0.10 * pulse);
                    int rays = 9; double len = 4 + pulse * 5;
                    for (int i = 0; i < rays; i++)
                    {
                        double a = i * Math.PI * 2 / rays + _frame * 0.02;
                        DrawRay(cx, cy, 5, len, a, 255, 236, 165, 0.45 + 0.45 * pulse);
                    }
                    DrawDisc(cx, cy, 4, 255, 238, 175, 1.0);
                }
                else if (sunset)
                {

                    double prog = Math.Min(1, Math.Max(0, (h - 16) / 5.0));
                    byte r = 255, g = (byte)(150 - prog * 80), b = (byte)(80 - prog * 45);
                    double flick = 0.16 + 0.10 * pulse + _sceneRng.NextDouble() * 0.05;
                    HorizonGlow(cx, 230, 90, 40, 0.55 - prog * 0.2);
                    DrawDisc(cx, cy, 10, 240, 110, 50, flick);
                    DrawDisc(cx, cy, 5, r, g, b, 1.0);

                    if (_sceneRng.NextDouble() < 0.5)
                        BP(cx + _sceneRng.Next(-5, 6), cy - _sceneRng.Next(0, 7), 255, 170, 90, 0.6);
                }
                else
                {
                    DrawDisc(cx, cy, 7, 255, 244, 196, 0.22);
                    DrawDisc(cx, cy, 4, 255, 226, 150, 1.0);
                }
            }
            else
            {
                double nf = ((h + (h < 4 ? 24 : 0)) - 21) / 7.0;
                int cx = (int)(nf * (SCN_W - 1));
                int cy = (int)(HORIZON - Math.Sin(Math.Max(0, Math.Min(1, nf)) * Math.PI) * (HORIZON - 12));
                DrawDisc(cx, cy, 6, 200, 205, 235, 0.18);
                DrawDisc(cx, cy, 4, 226, 230, 245, 1.0);
                SP(cx + 1, cy - 1, 180, 186, 210); SP(cx - 1, cy + 1, 180, 186, 210);
            }
        }

        private void HorizonGlow(int cx, byte r, byte g, byte b, double strength)
        {
            for (int y = HORIZON - 14; y < HORIZON; y++)
            {
                double vy = 1.0 - (HORIZON - y) / 14.0;
                for (int x = 0; x < SCN_W; x++)
                {
                    double dx = Math.Abs(x - cx) / 70.0;
                    double a = strength * vy * Math.Max(0, 1 - dx);
                    if (a > 0.01) BP(x, y, r, g, b, a);
                }
            }
        }

        private void DrawRay(int cx, int cy, double r0, double len, double angle, byte r, byte g, byte b, double a)
        {
            double dxs = Math.Cos(angle), dys = Math.Sin(angle);
            for (double d = r0; d < r0 + len; d += 1.0)
                BP((int)(cx + dxs * d), (int)(cy + dys * d), r, g, b, a * (1 - (d - r0) / len));
        }

        private void DrawKite()
        {
            double wind = _weather == Weather.Wind ? 1.0 : 0.0;
            double bx = SCN_W * 0.26 + Math.Sin(_frame * 0.04) * (6 + wind * 14) + wind * 10;
            double by = 22 + Math.Sin(_frame * 0.06 + 1) * (3 + wind * 4);
            int kx = (int)bx, ky = (int)by;

            var col = (Color)FindResource("AccentColor");
            byte r = col.R, g = col.G, b = col.B;
            SP(kx, ky - 3, r, g, b);
            SP(kx - 1, ky - 2, r, g, b); SP(kx, ky - 2, 255, 255, 255); SP(kx + 1, ky - 2, r, g, b);
            SP(kx - 2, ky, r, g, b); SP(kx - 1, ky, 255, 255, 255); SP(kx, ky, 255, 255, 255); SP(kx + 1, ky, r, g, b); SP(kx + 2, ky, r, g, b);
            SP(kx - 1, ky + 2, r, g, b); SP(kx, ky + 2, r, g, b); SP(kx + 1, ky + 2, r, g, b);
            SP(kx, ky + 3, r, g, b);

            for (int i = 1; i <= 6; i++)
            {
                int tx = kx + (int)(Math.Sin(_frame * 0.1 + i * 0.7) * (1 + wind));
                int ty = ky + 3 + i * 2;
                SP(tx, ty, 240, 200, 220);
            }
        }

        private void DrawUmbrellaPerson(bool night)
        {
            int px = (int)(SCN_W * 0.40);
            int gy = _forest[px] - 1;
            if (gy < HORIZON) gy = HORIZON;
            byte sr = 18, sg = 16, sb = 26;

            SP(px, gy, sr, sg, sb); SP(px, gy - 1, sr, sg, sb); SP(px, gy - 2, sr, sg, sb);
            SP(px - 1, gy, sr, sg, sb); SP(px + 1, gy, sr, sg, sb);

            SP(px, gy - 3, sr, sg, sb);

            SP(px, gy - 4, sr, sg, sb); SP(px, gy - 5, sr, sg, sb);

            var col = (Color)FindResource("AccentColor");
            for (int dx = -3; dx <= 3; dx++) SP(px + dx, gy - 6, col.R, col.G, col.B);
            SP(px - 2, gy - 7, col.R, col.G, col.B); SP(px + 2, gy - 7, col.R, col.G, col.B);
            SP(px - 1, gy - 7, col.R, col.G, col.B); SP(px, gy - 7, col.R, col.G, col.B); SP(px + 1, gy - 7, col.R, col.G, col.B);
        }

        private void UpdateRain()
        {
            for (int i = 0; i < _pX.Length; i++)
            {
                _pX[i] -= 1.6; _pY[i] += 7;
                if (_pY[i] >= SCN_H || _pX[i] < 0) { _pX[i] = _sceneRng.Next(SCN_W); _pY[i] = -2; }
                int x = (int)_pX[i], y = (int)_pY[i];
                BP(x, y, 150, 190, 235, 0.7); BP(x + 1, y - 1, 150, 190, 235, 0.45); BP(x + 1, y - 2, 150, 190, 235, 0.3);
            }
            DrawFog(0.06);
        }

        private void UpdateSnow()
        {
            for (int i = 0; i < _pX.Length; i++)
            {
                _pY[i] += 1.4; _pX[i] += Math.Sin(_pY[i] * 0.12 + _pP[i]) * 0.6;
                if (_pY[i] >= SCN_H) { _pY[i] = -1; _pX[i] = _sceneRng.Next(SCN_W); }
                int x = ((int)_pX[i] + SCN_W) % SCN_W, y = (int)_pY[i];
                BP(x, y, 245, 248, 255, 0.92);
            }
        }

        private void UpdateWind()
        {
            const int gusts = 6;
            for (int g = 0; g < gusts; g++)
            {
                double baseY = 12 + g * 9 + Math.Sin(_frame * 0.03 + g) * 2;
                double amp = 2.2 + Math.Sin(_frame * 0.045 + g * 1.7) * 1.8;
                double phase = _frame * 0.16 + g * 2.0;
                double head = ((_frame * 2.4 + g * 47) % (SCN_W + 80)) - 40;
                int len = 34 + g * 4;
                for (int k = 0; k < len; k++)
                {
                    double xx = head - k;
                    double yy = baseY + Math.Sin(xx * 0.22 + phase) * amp
                                      + Math.Sin(xx * 0.07 - _frame * 0.05) * 1.2;
                    double edge = Math.Sin((double)k / len * Math.PI);
                    BP((int)xx, (int)yy, 225, 226, 240, 0.5 * edge);
                    BP((int)xx, (int)yy + 1, 210, 212, 230, 0.22 * edge);
                }
            }
        }

        private void UpdateSakura()
        {
            for (int i = 0; i < _pX.Length; i++)
            {
                _pY[i] += 0.9; _pX[i] += Math.Sin(_pY[i] * 0.10 + _pP[i]) * 1.1;
                if (_pY[i] >= SCN_H) { _pY[i] = -1; _pX[i] = _sceneRng.Next(SCN_W); }
                int x = ((int)_pX[i] + SCN_W) % SCN_W, y = (int)_pY[i];
                bool flip = ((int)(_pY[i] * 0.3 + _pP[i] * 3) & 1) == 0;
                BP(x, y, 248, 196, 222, 0.92);
                BP(flip ? x + 1 : x - 1, y, 240, 170, 205, 0.7);
            }
        }

        private void UpdateLeaves()
        {
            for (int i = 0; i < _pX.Length; i++)
            {
                _pY[i] += 1.1; _pX[i] += Math.Sin(_pY[i] * 0.08 + _pP[i]) * 1.6;

                if (_pY[i] >= SCN_H) { _pY[i] = HORIZON - 8 + _sceneRng.NextDouble() * 12; _pX[i] = _sceneRng.Next(SCN_W); }
                int x = ((int)_pX[i] + SCN_W) % SCN_W, y = (int)_pY[i];

                int kind = (int)(_pP[i] * 3) % 3;
                (byte r, byte g, byte b) c = kind == 0 ? ((byte)214, (byte)120, (byte)42)
                                          : kind == 1 ? ((byte)190, (byte)70, (byte)48)
                                          :             ((byte)206, (byte)160, (byte)60);
                bool flip = ((int)(_pY[i] * 0.25 + _pP[i] * 4) & 1) == 0;
                BP(x, y, c.r, c.g, c.b, 0.95);
                BP(flip ? x + 1 : x - 1, y, c.r, c.g, c.b, 0.8);
            }
        }

        private void DrawFog(double strength = 0.16)
        {
            for (int y = 0; y < SCN_H; y++)
            {
                double a = strength * (0.4 + 0.6 * y / SCN_H);
                for (int x = 0; x < SCN_W; x++) BP(x, y, 200, 200, 214, a);
            }
        }

        private void UpdateComets()
        {
            if (_cometLife <= 0 && _sceneRng.NextDouble() < 0.06)
            {
                _cometX = _sceneRng.Next(SCN_W / 2, SCN_W); _cometY = _sceneRng.Next(HORIZON - 20); _cometLife = 14;
            }
            if (_cometLife > 0)
            {
                _cometX -= 6; _cometY += 3; _cometLife--;
                int x = (int)_cometX, y = (int)_cometY;
                DrawDisc(x, y, 1, 255, 255, 255, 1.0);
                for (int i = 1; i <= 7; i++) BP(x + i * 2, y - i, 200, 215, 255, Math.Max(0, 0.8 - i * 0.11));
            }
        }

        private void SP(int x, int y, byte r, byte g, byte b)
        {
            if ((uint)x >= SCN_W || (uint)y >= SCN_H) return;
            int i = (y * SCN_W + x) * 4;
            _sceneBuf[i] = b; _sceneBuf[i + 1] = g; _sceneBuf[i + 2] = r; _sceneBuf[i + 3] = 255;
        }

        private void BP(int x, int y, byte r, byte g, byte b, double a)
        {
            if ((uint)x >= SCN_W || (uint)y >= SCN_H || a <= 0) return;
            int i = (y * SCN_W + x) * 4;
            _sceneBuf[i] = (byte)(_sceneBuf[i] * (1 - a) + b * a);
            _sceneBuf[i + 1] = (byte)(_sceneBuf[i + 1] * (1 - a) + g * a);
            _sceneBuf[i + 2] = (byte)(_sceneBuf[i + 2] * (1 - a) + r * a);
            _sceneBuf[i + 3] = 255;
        }

        private void DrawDisc(int cx, int cy, int rad, byte r, byte g, byte b, double a)
        {
            for (int y = -rad; y <= rad; y++)
                for (int x = -rad; x <= rad; x++)
                    if (x * x + y * y <= rad * rad)
                    {
                        if (a >= 1.0) SP(cx + x, cy + y, r, g, b);
                        else BP(cx + x, cy + y, r, g, b, a);
                    }
        }

        private void DrawCloud(int cx, int cy, int w, double bright, bool night)
        {
            byte r = (byte)((night ? 60 : 210) * (night ? 1 : bright));
            byte g = (byte)((night ? 62 : 214) * (night ? 1 : bright));
            byte b = (byte)((night ? 86 : 228) * (night ? 1 : bright));
            int h = w / 3;
            for (int x = 0; x < w; x++)
            {
                double edge = Math.Sin((double)x / w * Math.PI);
                int hh = (int)(h * edge);
                for (int y = -hh; y <= hh / 2; y++) BP(cx + x - w / 2, cy + y, r, g, b, 0.5);
            }
        }

        #endregion

        #region Загрузочный экран (boot / pixel-art / terminal)

        private static readonly string[] CreeperMap =
        {
            "00000000",
            "00000000",
            "01100110",
            "01100110",
            "00011000",
            "00111100",
            "00111100",
            "00100100",
        };

        private void BuildBootPixelArt()
        {
            if (BootPixelCanvas == null) return;
            BootPixelCanvas.Children.Clear();

            const double cell = 18;
            int rows = CreeperMap.Length;
            int cols = CreeperMap[0].Length;
            double offX = (BootPixelCanvas.Width - cols * cell) / 2.0;
            double offY = (BootPixelCanvas.Height - rows * cell) / 2.0;

            var faceMain = Color.FromRgb(0x5E, 0x9B, 0x3F);
            var faceDark = Color.FromRgb(0x4A, 0x7C, 0x32);
            var featureBrush = new SolidColorBrush(Color.FromRgb(0x18, 0x20, 0x10));

            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    bool feature = CreeperMap[r][c] == '1';
                    Brush fill = feature
                        ? featureBrush
                        : new SolidColorBrush(_sceneRng.NextDouble() < 0.4 ? faceDark : faceMain);

                    var px = new System.Windows.Shapes.Rectangle
                    {
                        Width = cell,
                        Height = cell,
                        Fill = fill,
                        Opacity = 0,
                        RenderTransformOrigin = new Point(0.5, 0.5)
                    };
                    var sc = new ScaleTransform(0, 0);
                    px.RenderTransform = sc;
                    Canvas.SetLeft(px, offX + c * cell);
                    Canvas.SetTop(px, offY + r * cell);
                    BootPixelCanvas.Children.Add(px);

                    var begin = TimeSpan.FromMilliseconds((r + c) * 24);
                    var fade = new DoubleAnimation(1, TimeSpan.FromMilliseconds(160)) { BeginTime = begin };
                    var pop = new DoubleAnimation(1, TimeSpan.FromMilliseconds(240))
                    {
                        BeginTime = begin,
                        EasingFunction = new BackEase { Amplitude = 0.6, EasingMode = EasingMode.EaseOut }
                    };
                    px.BeginAnimation(UIElement.OpacityProperty, fade);
                    sc.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
                    sc.BeginAnimation(ScaleTransform.ScaleYProperty, pop.Clone());
                }
            }

            var accent = (Color)FindResource("AccentColor");
            var glow = new DropShadowEffect { Color = accent, BlurRadius = 26, ShadowDepth = 0, Opacity = 0 };
            BootPixelCanvas.Effect = glow;
            var glowIn = new DoubleAnimation(0, 0.7, TimeSpan.FromMilliseconds(450)) { BeginTime = TimeSpan.FromMilliseconds(380) };
            glowIn.Completed += (s, e) =>
            {
                var pulse = new DoubleAnimation(0.45, 0.8, TimeSpan.FromSeconds(1.6))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                };
                glow.BeginAnimation(DropShadowEffect.OpacityProperty, pulse);
            };
            glow.BeginAnimation(DropShadowEffect.OpacityProperty, glowIn);
        }

        private async Task BootSequenceAsync()
        {
            if (BootOverlay == null) return;
            try
            {
                BuildBootPixelArt();
                await Task.Delay(300);

                if (BootTitle != null)
                {
                    BootTitle.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(400)));
                    BootTitleTr?.BeginAnimation(TranslateTransform.YProperty,
                        new DoubleAnimation(0, TimeSpan.FromMilliseconds(450)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                }
                if (BootSubtitle != null)
                    BootSubtitle.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(350)) { BeginTime = TimeSpan.FromMilliseconds(100) });

                await Task.Delay(200);

                var accentBrush = (Brush)FindResource("AccentBrush");
                var okBrush = new SolidColorBrush(Color.FromRgb(0x7C, 0xDB, 0x6A));
                var lines = new (string tag, Brush tagBrush, string msg)[]
                {
                    ("[BOOT] ", accentBrush, "BattleCraft Remake Launcher v" + VER),
                    ("[ OK ] ", okBrush,     "Инициализация ядра"),
                    ("[ OK ] ", okBrush,     "Загрузка конфигурации"),
                    ("[ OK ] ", okBrush,     "Проверка целостности библиотек"),
                    ("[ OK ] ", okBrush,     "Подключение Discord RPC"),
                    ("[ OK ] ", okBrush,     "Подготовка интерфейса"),
                    ("  >    ", accentBrush, "Запуск лаунчера"),
                };

                for (int i = 0; i < lines.Length; i++)
                {
                    await TypeBootLineAsync(lines[i].tag, lines[i].tagBrush, lines[i].msg);
                    SetBootProgress((i + 1) * 100.0 / lines.Length);
                    await Task.Delay(45);
                }

                await Task.Delay(250);
                await FadeOutBootAsync();
            }
            catch { }
        }

        private async Task TypeBootLineAsync(string tag, Brush tagBrush, string message)
        {
            if (BootLogText == null) return;
            var tagRun = new Run(tag) { Foreground = tagBrush, FontWeight = FontWeights.Bold };
            var msgRun = new Run("") { Foreground = new SolidColorBrush(Color.FromArgb(0xDD, 0xFF, 0xFF, 0xFF)) };
            BootLogText.Inlines.Add(tagRun);
            BootLogText.Inlines.Add(msgRun);
            BootLogText.Inlines.Add(new LineBreak());

            foreach (char ch in message)
            {
                msgRun.Text += ch;
                await Task.Delay(6 + _sceneRng.Next(7));
            }
        }

        private void SetBootProgress(double target)
        {
            if (BootProgress == null) return;
            BootProgress.BeginAnimation(System.Windows.Controls.Primitives.RangeBase.ValueProperty,
                new DoubleAnimation(target, TimeSpan.FromMilliseconds(280)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            if (BootPercent != null) BootPercent.Text = $"{Math.Round(target)}%";
        }

        private Task FadeOutBootAsync()
        {
            var tcs = new TaskCompletionSource<bool>();
            if (BootOverlay == null) { tcs.SetResult(true); return tcs.Task; }
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(400)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            fade.Completed += (s, e) =>
            {
                BootOverlay.Visibility = Visibility.Collapsed;
                tcs.TrySetResult(true);
            };
            BootOverlay.BeginAnimation(OpacityProperty, fade);
            return tcs.Task;
        }

        #endregion

        #region Пиксель-блоб справа (метаболлы)

        private WriteableBitmap? _blobBmp;
        private const int BLOB_W = 44;
        private const int BLOB_H = 62;
        private readonly byte[] _blobBuf = new byte[BLOB_W * BLOB_H * 4];
        private double _blobT;

        private void InitBlob()
        {
            try
            {
                _blobBmp = new WriteableBitmap(BLOB_W, BLOB_H, 96, 96, PixelFormats.Bgra32, null);
                if (BlobImage != null) BlobImage.Source = _blobBmp;
                RenderBlob();
            }
            catch { }
        }

        private void RenderBlob()
        {
            if (_blobBmp == null) return;
            try
            {
                var accent = (Color)FindResource("AccentColor");
                Color core = LerpColor(accent, Colors.White, 0.80);
                Color rim = LerpColor(accent, Colors.White, 0.42);

                double t = _blobT;

                var balls = new (double cx, double cy, double r)[]
                {
                    (BLOB_W * 0.50 + Math.Sin(t * 0.50) * BLOB_W * 0.13,       BLOB_H * 0.50 + Math.Sin(t * 0.33) * BLOB_H * 0.40, BLOB_W * 0.22),
                    (BLOB_W * 0.50 + Math.Sin(t * 0.40 + 2.0) * BLOB_W * 0.15, BLOB_H * 0.50 + Math.Sin(t * 0.28 + 2.0) * BLOB_H * 0.38, BLOB_W * 0.20),
                    (BLOB_W * 0.50 + Math.Cos(t * 0.45 + 1.0) * BLOB_W * 0.12, BLOB_H * 0.50 + Math.Cos(t * 0.31 + 4.0) * BLOB_H * 0.42, BLOB_W * 0.19),
                    (BLOB_W * 0.50 + Math.Sin(t * 0.60 + 3.0) * BLOB_W * 0.10, BLOB_H * 0.50 + Math.Sin(t * 0.37 + 1.0) * BLOB_H * 0.36, BLOB_W * 0.17),
                    (BLOB_W * 0.50 + Math.Cos(t * 0.55 + 5.0) * BLOB_W * 0.14, BLOB_H * 0.50 + Math.Cos(t * 0.26 + 3.0) * BLOB_H * 0.44, BLOB_W * 0.18),
                    (BLOB_W * 0.50 + Math.Sin(t * 0.48 + 4.0) * BLOB_W * 0.11, BLOB_H * 0.50 + Math.Sin(t * 0.30 + 5.0) * BLOB_H * 0.40, BLOB_W * 0.16),
                };

                int idx = 0;
                for (int y = 0; y < BLOB_H; y++)
                {
                    for (int x = 0; x < BLOB_W; x++)
                    {
                        double f = 0;
                        foreach (var b in balls)
                        {
                            double dx = x - b.cx, dy = y - b.cy;
                            f += (b.r * b.r) / (dx * dx + dy * dy + 1.0);
                        }

                        byte rr, gg, bb, aa;
                        if (f > 2.3) { rr = core.R; gg = core.G; bb = core.B; aa = 255; }
                        else if (f > 1.5) { rr = rim.R; gg = rim.G; bb = rim.B; aa = 255; }
                        else { rr = gg = bb = 0; aa = 0; }
                        _blobBuf[idx++] = bb;
                        _blobBuf[idx++] = gg;
                        _blobBuf[idx++] = rr;
                        _blobBuf[idx++] = aa;
                    }
                }
                _blobBmp.WritePixels(new Int32Rect(0, 0, BLOB_W, BLOB_H), _blobBuf, BLOB_W * 4, 0);
            }
            catch { }
        }

        private static Color LerpColor(Color a, Color b, double t)
        {
            return Color.FromRgb(
                (byte)(a.R + (b.R - a.R) * t),
                (byte)(a.G + (b.G - a.G) * t),
                (byte)(a.B + (b.B - a.B) * t));
        }

        #endregion

        private void Log(string message)
        {
            string prefix = message.Contains("Ошибка") ? "[ERR]" : "[SYS]";
            LogTagged(prefix, message);
        }

        private void LogNet(string message) => LogTagged("[NET]", message);

        private void LogTagged(string prefix, string message)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(() => LogTagged(prefix, message)); return; }
            LauncherLog.Write($"{prefix} {message}");
            _logLines.Add($"{prefix} {message}");
            if (_logLines.Count > 200) _logLines.RemoveAt(0);
            LogTerminalText.Text = string.Join("\n", _logLines);
            if (LogScroll != null) { LogScroll.UpdateLayout(); LogScroll.ScrollToBottom(); }
        }

        private void StartTimers()
        {
            try { if (_sysMonTimer != null) { _sysMonTimer.Stop(); _sysMonTimer = null; } _sysMonTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(5) }; _sysMonTimer.Tick += (s, e) => UpdateSysMonitor(); _sysMonTimer.Start(); UpdateSysMonitor(); } catch { }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength, dwMemoryLoad;
            public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
        }
        [DllImport("kernel32.dll")] private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buf);

        private void UpdateSysMonitor()
        {
            try
            {
                if (MemoryText == null) return;
                var ms = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
                if (GlobalMemoryStatusEx(ref ms) && ms.ullTotalPhys > 0)
                {
                    double total = ms.ullTotalPhys / 1073741824.0;
                    double used = (ms.ullTotalPhys - ms.ullAvailPhys) / 1073741824.0;
                    MemoryText.Text = $"{used:F1} / {total:F1} GiB";
                }
                else
                {
                    var mi = GC.GetGCMemoryInfo();
                    MemoryText.Text = $"{mi.MemoryLoadBytes / 1073741824.0:F1} / {mi.TotalAvailableMemoryBytes / 1073741824.0:F1} GiB";
                }
            }
            catch { }
        }

        private void InitializeLauncherCore()
        {
            try
            {
                string oldFile = Process.GetCurrentProcess().MainModule!.FileName + ".old";
                if (File.Exists(oldFile)) File.Delete(oldFile);
            }
            catch { }

            _settings = AppSettings.Load();
            FillColorPresets();
            ApplyThemeFromSettings();
            ApplyCustomTheme();
            StartTimers();

            _discordManager.LauncherVersion = VER;
            _discordManager.ModpackVersion = _settings.ModpackVersion;
            _discordManager.Initialize();

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
        { var d = new OpenFolderDialog(); if (d.ShowDialog() == true) SetupPathBox.Text = ResolveGamePath(d.FolderName); }

        private static string ResolveGamePath(string chosen)
        {
            if (string.IsNullOrWhiteSpace(chosen)) return "";
            chosen = chosen.Trim();
            string trimmed = chosen.TrimEnd('\\', '/');
            if (string.Equals(Path.GetFileName(trimmed), "BattleCraft", StringComparison.OrdinalIgnoreCase))
                return trimmed;
            return Path.Combine(chosen, "BattleCraft");
        }

        private async Task PrepareGameFolderAsync(string path)
        {
            try
            {
                await Task.Run(() =>
                {
                    if (Directory.Exists(path)) Directory.Delete(path, true);
                    Directory.CreateDirectory(path);
                });
            }
            catch (Exception ex) { Log($"Ошибка подготовки папки игры: {ex.Message}"); }
        }

        private async void BtnSetupMicrosoft_Click(object s, RoutedEventArgs e)
        {
            try
            {
                var handler = JELoginHandlerBuilder.BuildDefault();
                var sessionObj = await handler.AuthenticateInteractively();

                if (sessionObj == null || string.IsNullOrEmpty(sessionObj.Username)) return;

                _settings.Username = sessionObj.Username;
                _settings.UserType = "msa";
                AppSettings.Save(_settings);

                SetupUsernameBox.Text = sessionObj.Username;
                SetupUsernameBox.IsEnabled = false;
                SetupUsernameBox.Opacity = 0.5;
                await ShowCustomDialog($"Авторизован как: {sessionObj.Username}");
            }
            catch (Exception ex)
            {
                await ShowCustomDialog($"Ошибка авторизации: {ex.Message}");
            }
        }

        private async void BtnSetupComplete_Click(object s, RoutedEventArgs e)
        {
            string nick = SetupUsernameBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(nick)) { await ShowCustomDialog("Авторизуйтесь через Microsoft или введите никнейм!"); return; }
            string path = ResolveGamePath(SetupPathBox.Text);
            if (string.IsNullOrWhiteSpace(path)) { await ShowCustomDialog("Выберите папку для игры!"); return; }

            if (!string.Equals(path, _settings.GamePath, StringComparison.OrdinalIgnoreCase))
            {
                await PrepareGameFolderAsync(path);
                _settings.IsModpackInstalled = false;
                _settings.ModpackVersion = "0.0";
            }

            _settings.GamePath = path; _settings.RamMb = 4096;

            if (_settings.UserType != "msa" || _settings.Username != nick)
            {
                _settings.Username = nick;
                _settings.UserType = "offline";
            }

            AppSettings.Save(_settings);

            SetupPanel.Visibility = Visibility.Hidden;
            TopButtons.Visibility = Visibility.Visible;
            UsernameBox.Text = nick; RamSlider.Value = 4096; SetupPathBox.Text = path; PathBox.Text = path;
            _ = AnimateTerminalText(TopLeftTitleText, "BattleCraft Remake Launcher");
            SwitchToMain();
        }

        private void FillColorPresets()
        {
            if (ColorPresetCombo == null) return;
            ColorPresetCombo.Items.Clear();
            ColorPresetCombo.MaxDropDownHeight = 480;
            (string name, string tag)[] presets =
            {
                ("Космос",    "#0D0D1E|#BB86FC"),
                ("Малина",    "#1A1A2E|#E94560"),
                ("Океан",     "#0A192F|#64FFDA"),
                ("Лес",       "#1B2631|#2ECC71"),
                ("Золото",    "#2C1810|#FFD700"),
                ("Янтарь",    "#1A1512|#FF9F43"),
                ("Мята",      "#0F2027|#00F260"),
                ("Лаванда",   "#0E0B16|#E6D8F5"),
                ("Роза",      "#120A16|#F3C6E6"),
                ("Сакура",    "#14101A|#F7CAD0"),
                ("Сирень",    "#0D0C18|#C9B6F0"),
                ("Перламутр", "#100E18|#DCE3F0"),
            };
            foreach (var (name, tag) in presets)
                ColorPresetCombo.Items.Add(new ComboBoxItem { Content = name, Tag = tag });
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

            double consolePct = (_settings.ConsoleOpacity ?? 1.0) * 100.0;
            ApplyConsoleOpacity(consolePct, false);
            if (ConsoleOpacitySlider != null) ConsoleOpacitySlider.Value = consolePct;

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

                double lum = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;
                var onAccent = lum > 0.6 ? Color.FromRgb(0x10, 0x0C, 0x18) : Colors.White;
                this.Resources["OnAccentBrush"] = new SolidColorBrush(onAccent);
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

        private void ApplyConsoleOpacity(double pct, bool save = true)
        {
            double o = pct / 100.0;
            if (PlayContentPanel != null) PlayContentPanel.Opacity = o;
            if (ServerContentPanel != null) ServerContentPanel.Opacity = o;
            if (save) { _settings.ConsoleOpacity = o; AppSettings.Save(_settings); }
        }

        private void ConsoleOpacitySlider_ValueChanged(object s, RoutedPropertyChangedEventArgs<double> e) { if (IsLoaded) ApplyConsoleOpacity(e.NewValue); }

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

            var parameters = MinecraftLauncherParameters.CreateDefault(_minecraftPath, ResilientHttpClientFactory.Shared);
            if (parameters.GameInstaller is GameInstallerBase installerBase)
                installerBase.CheckFileChecksum = false;
            _launcher = new MinecraftLauncher(parameters);
            _launcher.FileProgressChanged += (s, e) =>
            {
                string name = e.Name ?? "";
                int done = e.ProgressedTasks;
                int total = e.TotalTasks;

                long now = Environment.TickCount64;
                long prev = System.Threading.Interlocked.Read(ref _lastProgressTick);
                if (now - prev < 120) return;
                if (System.Threading.Interlocked.CompareExchange(ref _lastProgressTick, now, prev) != prev) return;

                double percent = total > 0 ? (double)done / total * 100 : -1;
                bool noBytes = now - System.Threading.Interlocked.Read(ref _lastByteTick) > 1500;
                bool debug = _settings.DebugConsole;
                Dispatcher.BeginInvoke(() =>
                {
                    if (debug && !string.IsNullOrEmpty(name)) Log($"Файл: {name} ({done}/{total})");
                    else StatusText.Text = name;
                    if (percent >= 0 && noBytes) SetProgress(percent);
                }, System.Windows.Threading.DispatcherPriority.Background);
            };

            _launcher.ByteProgressChanged += (s, e) =>
            {
                long total = e.TotalBytes;
                long done = e.ProgressedBytes;
                if (total <= 0) return;

                long now = Environment.TickCount64;
                long prev = System.Threading.Interlocked.Read(ref _lastByteTick);
                if (now - prev < 200) return;
                if (System.Threading.Interlocked.CompareExchange(ref _lastByteTick, now, prev) != prev) return;

                double percent = (double)done / total * 100;

                double speed = 0;
                long lastBytes = System.Threading.Interlocked.Read(ref _lastByteCount);
                long lastTick = System.Threading.Interlocked.Read(ref _lastByteSpeedTick);
                if (done >= lastBytes && lastTick > 0 && now > lastTick)
                    speed = (done - lastBytes) * 1000.0 / (now - lastTick);
                System.Threading.Interlocked.Exchange(ref _lastByteCount, done);
                System.Threading.Interlocked.Exchange(ref _lastByteSpeedTick, now);

                bool logNow = now - System.Threading.Interlocked.Read(ref _lastNetLogTick) > 1500;
                if (logNow) System.Threading.Interlocked.Exchange(ref _lastNetLogTick, now);

                Dispatcher.BeginInvoke(() =>
                {
                    SetProgress(percent);
                    if (logNow)
                    {
                        string line = $"Загрузка {percent:F0}% · {FileDownloader.FormatSize(done)} / {FileDownloader.FormatSize(total)}";
                        if (speed > 0) line += $" · {FileDownloader.FormatSpeed(speed)}";
                        LogNet(line);
                    }
                }, System.Windows.Threading.DispatcherPriority.Background);
            };
        }

        private async void BtnPlay_Click(object sender, RoutedEventArgs e)
        {
            if (_gameProcess != null)
            {
                try { _gameProcess.Kill(); } catch { }
                _gameProcess = null;
                Show();
                SetPlayState("idle");
                StatusText.Text = "Готов";
                SetProgress(0);
                _discordManager.SetMenuState();
                return;
            }
            if (!_settings.HasGamePath) { await ShowCustomDialog("Выберите папку для игры в настройках!"); return; }

            BtnPlay.IsEnabled = false; SetBusy(true);
            bool didInstall = false;
            try
            {
                if (!Directory.Exists(_settings.GamePath)) Directory.CreateDirectory(_settings.GamePath);

                string dhServerDataPath = Path.Combine(_settings.GamePath, "Distant_Horizons_server_data");
                if (Directory.Exists(dhServerDataPath))
                {
                    try { Directory.Delete(dhServerDataPath, true); } catch { }
                }

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

                MSession? mSession = null;
                if (_settings.UserType == "msa")
                {
                    var handler = JELoginHandlerBuilder.BuildDefault();
                    dynamic? sessionObj = null;
                    try { sessionObj = await handler.AuthenticateSilently(); } catch { }

                    if (sessionObj != null && !string.IsNullOrEmpty(sessionObj.Username))
                    {
                        mSession = new MSession();
                        mSession.Username = sessionObj.Username;
                        mSession.AccessToken = sessionObj.AccessToken;
                        mSession.UUID = sessionObj.UUID;
                        mSession.UserType = "msa";
                    }
                    else
                    {
                        await ShowCustomDialog("Срок действия сессии истек. Пожалуйста, авторизуйтесь заново.");
                        SetProgress(0); SetPlayState("idle"); BtnPlay.IsEnabled = true; SetBusy(false);
                        LoginGridState();
                        return;
                    }
                }
                else
                {
                    mSession = MSession.CreateOfflineSession(_settings.Username);
                }

                var opt = new MLaunchOption { MaximumRamMb = _settings.RamMb, Session = mSession, JavaPath = FindJava() };
                _gameProcess = await _launcher.CreateProcessAsync(ver.Name, opt);
                InjectJvmArgs(_gameProcess);

                _gameProcess.StartInfo.CreateNoWindow = !_settings.DebugConsole;
                _gameProcess.StartInfo.UseShellExecute = false;

                _gameProcess.Start();
                _logLines.Clear(); LogTerminalText.Text = "";
                SetPlayState("running"); BtnPlay.IsEnabled = true; SetBusy(false);
                _discordManager.SetPlayingState(_activeServerConfig?.Name ?? "Одиночная игра");

                await _gameProcess.WaitForExitAsync();
                _gameProcess = null;
                SetPlayState("idle");
                StatusText.Text = "Готов";
                _discordManager.SetMenuState();
            }
            catch (OperationCanceledException) { Log("Установка отменена."); StatusText.Text = "Отменено"; SetPlayState("idle"); }
            catch (Exception ex) { await HandleErrorAsync(ex, "Ошибка запуска"); }
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
            try
            {
                SetProgress(0);
                StatusText.Text = "Загрузка файлов Minecraft...";
                await _launcher.InstallAsync(MC); EnsureProfiles();

                StatusText.Text = "Загрузка установщика Forge...";
                string jar = Path.Combine(Path.GetTempPath(), "forge_installer.jar");
                if (File.Exists(jar)) File.Delete(jar);
                var forgeDl = new FileDownloader();
                forgeDl.LogMessage += LogNet;
                forgeDl.ProgressChanged += p => Dispatcher.BeginInvoke(() => { GameProgressBar.IsIndeterminate = false; SetProgress(p); });
                await forgeDl.DownloadFileAsync(FORGE_JAR_URL, jar);

                StatusText.Text = "Установка библиотек Forge...";
                Log("Этот этап займёт от 1 до 5 минут, не закрывайте лаунчер.");
                ShowForgeWarning(true);
                await RunForgeInstaller(jar);
                ShowForgeWarning(false);

                await _launcher.GetAllVersionsAsync();
                try { File.Delete(jar); } catch { }
                CleanForgeLog();
                Log("Forge установлен.");
            }
            catch (Exception ex) { ShowForgeWarning(false); await HandleErrorAsync(ex, "Ошибка Forge"); }
            finally { GameProgressBar.IsIndeterminate = false; }
        }

        private async Task RunForgeInstaller(string jar)
        {
            var psi = new ProcessStartInfo
            {
                FileName = FindJava(),
                Arguments = $"-jar \"{jar}\" --installClient \"{_settings.GamePath}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8
            };

            var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
            DataReceivedEventHandler onData = (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(e.Data)) return;
                LauncherLog.Write($"[FORGE] {e.Data.Trim()}");
            };
            proc.OutputDataReceived += onData;
            proc.ErrorDataReceived += onData;

            proc.Start();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            var creep = StartCreepProgress(95, 200);
            try { await proc.WaitForExitAsync(); }
            finally { StopCreepProgress(creep); SetProgress(100); }
        }

        private System.Windows.Threading.DispatcherTimer StartCreepProgress(double to, double seconds)
        {
            double value = 0;
            SetProgress(0);
            double step = (to - value) / (seconds * 2);
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            timer.Tick += (s, e) =>
            {
                value = Math.Min(to, value + step);
                GameProgressBar.IsIndeterminate = false;
                SetProgress(value);
            };
            timer.Start();
            return timer;
        }

        private static void StopCreepProgress(System.Windows.Threading.DispatcherTimer? timer)
        {
            try { timer?.Stop(); } catch { }
        }

        private void ShowForgeWarning(bool show)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(() => ShowForgeWarning(show)); return; }
            if (ForgeWarningPanel != null) ForgeWarningPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        }

        private void CleanForgeLog()
        {
            try { foreach (var dir in new[] { Path.GetTempPath(), _settings.GamePath, AppDomain.CurrentDomain.BaseDirectory })
                foreach (var f in Directory.GetFiles(dir, "*.jar.log")) try { File.Delete(f); } catch { }
            } catch { }
        }

        private async Task HandleErrorAsync(Exception ex, string context)
        {
            string logPath = LauncherLog.WriteCrash(context, ex);

            bool isJavaError = ex is System.ComponentModel.Win32Exception
                || ex.Message.Contains("не удается найти")
                || ex.Message.Contains("cannot find the file");

            if (isJavaError)
            {
                if (await ShowCustomDialog("Похоже, что отсутствует Java. Скачать и установить Java 17 автоматически?", "Ошибка Java", true))
                {
                    await DownloadAndInstallJava();
                    return;
                }
            }

            if (await ShowCustomDialog($"{context}: {ex.Message}\n\nОткрыть файл с логами?", "Ошибка", true))
            {
                string toOpen = !string.IsNullOrEmpty(logPath) && File.Exists(logPath) ? logPath : AppSettings.GetConfigDir();
                try { Process.Start(new ProcessStartInfo(toOpen) { UseShellExecute = true }); } catch { }
            }
        }

        private async Task DownloadAndInstallJava()
        {
            try
            {
                SetBusy(true);
                GameProgressBar.IsIndeterminate = true;
                StatusText.Text = "Скачивание Java 17...";

                string tempZip = Path.Combine(Path.GetTempPath(), "jre17.zip");
                if (File.Exists(tempZip)) File.Delete(tempZip);

                var downloader = new FileDownloader();
                downloader.LogMessage += LogNet;
                downloader.ProgressChanged += (p) => Dispatcher.BeginInvoke(() => { GameProgressBar.IsIndeterminate = false; GameProgressBar.Value = p; });

                await downloader.DownloadFileAsync("https://api.adoptium.net/v3/binary/latest/17/ga/windows/x64/jre/hotspot/normal/eclipse", tempZip);

                StatusText.Text = "Установка Java 17...";
                GameProgressBar.IsIndeterminate = true;
                await Task.Run(() =>
                {
                    string targetDir = Path.Combine(_settings.GamePath, "runtime", "java-runtime-gamma", "windows-x64", "java-runtime-gamma");
                    if (Directory.Exists(targetDir)) Directory.Delete(targetDir, true);
                    Directory.CreateDirectory(targetDir);

                    System.IO.Compression.ZipFile.ExtractToDirectory(tempZip, targetDir);
                    File.Delete(tempZip);

                    var subDirs = Directory.GetDirectories(targetDir);
                    if (subDirs.Length == 1)
                    {
                        string extractedDir = subDirs[0];
                        foreach (var file in Directory.GetFiles(extractedDir)) File.Move(file, Path.Combine(targetDir, Path.GetFileName(file)));
                        foreach (var dir in Directory.GetDirectories(extractedDir)) Directory.Move(dir, Path.Combine(targetDir, Path.GetFileName(dir)));
                        Directory.Delete(extractedDir);
                    }
                });

                await ShowCustomDialog("Java 17 успешно установлена! Попробуйте запустить игру снова.", "Успех");
            }
            catch (Exception ex)
            {
                await ShowCustomDialog($"Ошибка установки Java: {ex.Message}");
            }
            finally
            {
                SetBusy(false);
                GameProgressBar.IsIndeterminate = false;
                GameProgressBar.Value = 0;
                StatusText.Text = "Готов";
            }
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
                string[] optFiles = { "options.txt", "optionsof.txt", "optionsshaders.txt" };
                foreach (var f in optFiles)
                {
                    string p = Path.Combine(_settings.GamePath, f);
                    try { if (File.Exists(p)) File.Delete(p); } catch { }
                }
            }

            bool success = false;
            while (!success)
            {
                string zip = Path.Combine(Path.GetTempPath(), "modpack_download.zip");
                try
                {
                    var dl = new FileDownloader();
                    dl.LogMessage += LogNet;
                    dl.ProgressChanged += v => Dispatcher.BeginInvoke(() => { GameProgressBar.IsIndeterminate = false; SetProgress(v); });
                    await dl.DownloadFileAsync(MODPACK_URL, zip);
                    StatusText.Text = "Распаковка...";
                    await Task.Run(() => { ZipFile.ExtractToDirectory(zip, _settings.GamePath, true); try { File.Delete(zip); } catch { } });
                    Log("Распаковка завершена!");
                    _settings.IsModpackInstalled = true;
                    _settings.ModpackVersion = _onlineModpackVer != "0.0" ? _onlineModpackVer : "1.0";
                    _discordManager.ModpackVersion = _settings.ModpackVersion;
                    if (_gameProcess == null) _discordManager.SetMenuState();
                    AppSettings.Save(_settings);
                    success = true;
                }
                catch (Exception ex)
                {
                    try { if (File.Exists(zip)) File.Delete(zip); } catch { }
                    bool retry = await ShowCustomDialog(
                        $"Загрузка клиента оборвалась.\nОшибка: {ex.Message}\nПродолжить скачивание?",
                        "Ошибка скачивания", true);

                    if (!retry)
                    {
                        Log("Установка отменена. Очистка файлов...");
                        foreach (var dir in ModpackDirs)
                        {
                            string p = Path.Combine(_settings.GamePath, dir);
                            try { if (Directory.Exists(p)) Directory.Delete(p, true); } catch { }
                        }
                        throw new OperationCanceledException("Установка отменена пользователем.");
                    }
                }
            }
        }

        private async Task AnimateTerminalText(TextBlock tb, string targetText)
        {
            tb.Text = "";
            string chars = "$?#!*%@^&~";
            var rnd = _rnd;
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
            var rnd = _rnd;

            if (hour >= 4 && hour < 12)
            {
                string[] p = {
                    $"утречко, {username}", $"о, проснулся, {username}", $"ну чё, выспался, {username}?",
                    $"го фрагать с утра, {username}", $"утро кофе майн, {username}", $"доброе, {username}, как спалось",
                    $"ранний вход, уважение, {username}", $"утренний вайб, {username}", $"просыпайся соня, {username}",
                    $"новый день новые фраги, {username}", $"найс что зашёл с утра, {username}", $"го катку пока все спят, {username}",
                    $"кста уже утро, {username}", $"чиллим с утреца, {username}", $"доброе утро легенда, {username}",
                    $"ну ты и ранняя пташка, {username}", $"утро, {username}, врываемся", $"бодрого утра, {username}",
                    $"с утра всегда лучше фрагается, {username}", $"проснись и пой, {username}", $"кофе врубил, {username}?",
                    $"утро доброе бля, {username}", $"го пока пинг низкий, {username}", $"утренняя движуха, {username}"
                };
                return p[rnd.Next(p.Length)];
            }
            if (hour >= 12 && hour < 16)
            {
                string[] p = {
                    $"даров, {username}", $"ну чё по катке, {username}", $"обеденный гейминг, {username}?",
                    $"день в разгаре, {username}", $"го играть, {username}", $"как день проходит, {username}?",
                    $"найс что зашёл, {username}", $"дневной чилл, {username}", $"врываемся, {username}",
                    $"чё нового, {username}?", $"пора фрагать, {username}", $"дневная доза майна, {username}",
                    $"ты сегодня в ударе, {username}?", $"день топ, {username}", $"погнали катку, {username}",
                    $"кайфового дня, {username}", $"ну ты красавчик что зашёл, {username}", $"обед майн изии, {username}",
                    $"солнце жарит а ты в майне, {username}", $"продуктивного дня, {username}", $"чё там по фрагам, {username}?",
                    $"день норм проходит, {username}?", $"го врубаем, {username}", $"дневная движуха, {username}"
                };
                return p[rnd.Next(p.Length)];
            }
            if (hour >= 16 && hour < 21)
            {
                string[] p = {
                    $"вечерочек, {username}", $"вечер топ для катки, {username}", $"ну чё вечерний раш, {username}?",
                    $"закат красивый кста, {username}", $"чиллим вечером, {username}", $"вечер майн кайф, {username}",
                    $"наконец вечер, {username}", $"го вечерние катки, {username}", $"уютного вечера, {username}",
                    $"вечерний вайб поймал, {username}?", $"релакс врубаем, {username}", $"вечер для победителей, {username}",
                    $"ну ты и засиделся, {username}", $"вечером всегда лучше фрагается, {username}", $"тёплого вечера, {username}",
                    $"закат догорает а ты в строю, {username}", $"вечерний гейминг лучший, {username}", $"отдыхай вечером, {username}",
                    $"вечер наш, {username}", $"найс вечер, {username}", $"вечерочек пздц уютный, {username}",
                    $"как день прошёл, {username}?", $"вечерняя движуха, {username}", $"го пока не поздно, {username}"
                };
                return p[rnd.Next(p.Length)];
            }

            string[] n = {
                $"ну ты и полуночник, {username}", $"ночные катки топ, {username}", $"не спишь, {username}? рил",
                $"звёзды светят а ты в майне, {username}", $"ночь тишина фраги, {username}", $"доброй ночи но ещё одна катка, {username}",
                $"ночной вайб, {username}", $"спать? пфф ещё рано, {username}", $"луна светит, {username}",
                $"ночь наша, {username}", $"ну ещё чуть чуть и спать, {username}?", $"бессонница, {username}? го играть",
                $"тихой ночи, {username}", $"ночные приключения ждут, {username}", $"сон для слабых, {username}?",
                $"глубокая ночь а мы тут, {username}", $"звёздное небо кайф, {username}", $"ночная смена, {username}",
                $"не засиживайся сильно, {username}", $"сладких снов потом, {username}", $"ночью пинг ниже, го, {username}",
                $"тёмная ночь, {username}", $"ночь пздц длинная, ещё катка, {username}", $"полуночный гейминг, {username}?"
            };
            return n[rnd.Next(n.Length)];
        }

        private string GetRandomQuestion()
        {
            int hour = DateTime.Now.Hour;
            var rnd = _rnd;
            var q = new System.Collections.Generic.List<string> {
                "чё как", "чё нового", "го катку?", "ну чё играем?",
                "скилл на месте?", "готов фрагать?", "всё ровно?",
                "настрой боевой?", "жми играть, изии", "го писать историю",
                "ждём только тебя", "пинг норм?", "грузим моды...", "синхрон...",
                "вайб поймал?", "ну чё там по планам?"
            };

            if (hour >= 16 || hour < 4)
            {
                q.Add("как день прошёл?");
                q.Add("много нафрагал?");
                q.Add("устал поди?");
            }
            if (hour >= 4 && hour < 12)
            {
                q.Add("как спалось?");
                q.Add("готов к дню?");
                q.Add("утренний раш?");
            }
            return q[rnd.Next(q.Count)];
        }

        private async void StartWelcomeTextLoop()
        {
            if (_welcomeLoopStarted) return;
            _welcomeLoopStarted = true;

            string chars = "$?#!*%@^&~";
            var rnd = _rnd;
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
            if (ModpackVerText != null) ModpackVerText.Text = $"v{_settings.ModpackVersion}";
            StartWelcomeTextLoop();
        }

        private void LoginGridState()
        {
            MainPanel.Visibility = Visibility.Hidden;
            LoginPanel.Visibility = Visibility.Visible;
            if (_settings.UserType == "msa") UsernameBox.Text = "";
            else UsernameBox.Text = _settings.Username;
        }

        private async void BtnLoginMicrosoft_Click(object s, RoutedEventArgs e)
        {
            try
            {
                var handler = JELoginHandlerBuilder.BuildDefault();
                var sessionObj = await handler.AuthenticateInteractively();

                if (sessionObj == null || string.IsNullOrEmpty(sessionObj.Username)) return;

                _settings.Username = sessionObj.Username;
                _settings.UserType = "msa";
                AppSettings.Save(_settings);
                SwitchToMain();
            }
            catch (Exception ex)
            {
                await ShowCustomDialog($"Ошибка авторизации: {ex.Message}");
            }
        }

        private async void BtnLoginOffline_Click(object s, RoutedEventArgs e)
        {
            var n = UsernameBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(n)) { await ShowCustomDialog("Введите никнейм!"); return; }
            _settings.Username = n;
            _settings.UserType = "offline";
            AppSettings.Save(_settings);
            SwitchToMain();
        }

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

        private async void BtnCloseSettings_Click(object s, RoutedEventArgs e)
        {
            if (int.TryParse(RamBox.Text, out int ram)) _settings.RamMb = ram;
            string np = ResolveGamePath(PathBox.Text);
            if (!string.IsNullOrWhiteSpace(np) && !string.Equals(np, _settings.GamePath, StringComparison.OrdinalIgnoreCase))
            {
                await PrepareGameFolderAsync(np);
                _settings.IsModpackInstalled = false;
                _settings.ModpackVersion = "0.0";
                _settings.GamePath = np;
                PathBox.Text = np;
            }
            AppSettings.Save(_settings); if (_settings.HasGamePath) InitializeLauncher();
            var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(150));
            fade.Completed += (s2, e2) => { SettingsPanel.Visibility = Visibility.Hidden; SettingsPanel.BeginAnimation(OpacityProperty, null); SettingsPanel.Opacity = 1; };
            SettingsPanel.BeginAnimation(OpacityProperty, fade);
        }

        private void BtnSaveSettings_Click(object s, RoutedEventArgs e) => BtnCloseSettings_Click(s, e);
        private void DebugCheck_Changed(object s, RoutedEventArgs e) { if (IsLoaded) { _settings.DebugConsole = DebugCheck.IsChecked == true; AppSettings.Save(_settings); } }
        private void BtnSelectFolder_Click(object s, RoutedEventArgs e) { var d = new OpenFolderDialog(); if (d.ShowDialog() == true) PathBox.Text = ResolveGamePath(d.FolderName); }

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
                var t4 = _httpClient.GetStringAsync(SERVER_MAP_VER_URL + ts);
                var results = await Task.WhenAll(t1, t2, t3, t4);

                string modpackVerStr = results[0].Trim();
                string launcherVerStr = results[1].Trim();
                string serverModpackVerStr = results[2].Trim();
                string serverMapVerStr = results[3].Trim();

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
                }

                if (Version.TryParse(serverMapVerStr, out var onMV) && Version.TryParse(_settings.ServerMapVersion, out var loMV))
                {
                    _onlineServerMapVer = serverMapVerStr;
                    _needsServerMapUpdate = onMV > loMV;
                }

                UpdateServerButtons();
                StatusText.Text = $"Модпак v{_settings.ModpackVersion}";
                if (Version.TryParse(launcherVerStr, out var onlineLauncherV) && Version.TryParse(VER, out var currentLauncherV)
                    && onlineLauncherV > currentLauncherV
                    && await ShowCustomDialog($"Обновить лаунчер до {launcherVerStr}?", "Обновление", true)) await UpdateLauncher();
            }
            catch { StatusText.Text = "Ошибка сети"; }
        }

        private async Task UpdateLauncher()
        {
            ShowUpdateOverlay();
            try
            {
                string dir = AppDomain.CurrentDomain.BaseDirectory, cur = Process.GetCurrentProcess().MainModule!.FileName;
                string tmp = Path.Combine(dir, "upd.exe");
                string old = cur + ".old";

                var dl = new FileDownloader();
                dl.LogMessage += LogNet;
                dl.ProgressChanged += p => Dispatcher.BeginInvoke(() => SetUpdateProgress(p));
                await dl.DownloadFileAsync(LAUNCHER_EXE_URL, tmp);

                SetUpdateProgress(100);
                UpdateSubText.Text = "Перезапуск…";
                await Task.Delay(400);

                try { if (File.Exists(old)) File.Delete(old); } catch { }
                File.Move(cur, old);
                File.Move(tmp, cur);

                Process.Start(new ProcessStartInfo(cur) { UseShellExecute = true });
                Application.Current.Shutdown();
            }
            catch { HideUpdateOverlay(); StatusText.Text = "Ошибка обновления"; }
        }

        private void ShowUpdateOverlay()
        {
            UpdateProgressBar.BeginAnimation(ProgressBar.ValueProperty, null);
            UpdateProgressBar.Value = 0;
            UpdatePercentText.Text = "0%";
            UpdateSubText.Text = "Скачивание новой версии…";

            UpdateOverlay.Visibility = Visibility.Visible;
            UpdateOverlay.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));

            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            UpdateCardScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.92, 1, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease });
            UpdateCardScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.92, 1, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease });
            UpdateCardTranslate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(24, 0, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease });

            UpdateSpinnerRotate.BeginAnimation(RotateTransform.AngleProperty,
                new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1.1)) { RepeatBehavior = RepeatBehavior.Forever });
        }

        private void HideUpdateOverlay()
        {
            UpdateSpinnerRotate.BeginAnimation(RotateTransform.AngleProperty, null);
            UpdateOverlay.BeginAnimation(OpacityProperty, null);
            UpdateOverlay.Visibility = Visibility.Hidden;
        }

        private void SetUpdateProgress(double percent)
        {
            UpdateProgressBar.BeginAnimation(ProgressBar.ValueProperty,
                new DoubleAnimation { To = percent, Duration = TimeSpan.FromMilliseconds(200), EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
            UpdatePercentText.Text = $"{percent:F0}%";
        }

        private async void BtnReinstall_Click(object s, RoutedEventArgs e)
        {
            if (_isBusy) return;
            if (!_settings.HasGamePath) { await ShowCustomDialog("Сначала выберите папку!"); return; }
            if (await ShowCustomDialog("Перекачать моды?", "Подтверждение", true))
            {
                SetBusy(true);
                try { await InstallModpack(true); Log("Готово!"); StatusText.Text = "Моды переустановлены"; SetPlayState("idle"); }
                catch (OperationCanceledException) { Log("Установка отменена."); StatusText.Text = "Отменено"; }
                catch (Exception ex) { await HandleErrorAsync(ex, "Ошибка переустановки"); }
                finally { SetBusy(false); }
            }
        }

        private void BtnChangeIcon_Click(object s, RoutedEventArgs e)
        { var d = new OpenFileDialog { Filter = "Icon|*.ico" }; if (d.ShowDialog() == true) { try { File.Copy(d.FileName, Path.Combine(GetThemeDir(), "icon.ico"), true); ApplyCustomTheme(); } catch { } } }

        private void BloomEnabledCheck_Changed(object s, RoutedEventArgs e) { if (IsLoaded) ApplyBloom(BloomEnabledCheck.IsChecked == true, BloomStrengthSlider.Value); }
        private void BloomStrengthSlider_ValueChanged(object s, RoutedPropertyChangedEventArgs<double> e) { if (IsLoaded) ApplyBloom(BloomEnabledCheck.IsChecked == true, e.NewValue); }

        private void SettingsScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {

            if (ColorPresetCombo != null && ColorPresetCombo.IsDropDownOpen) return;
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
        }

        private void NavServer_Click(object s, RoutedEventArgs e)
        {
            if (_isServerTab) return;
            _isServerTab = true;
            UpdateNavHighlight();
            PlayContentPanel.Visibility = Visibility.Collapsed;
            ServerContentPanel.Visibility = Visibility.Visible;

            try { int maxRam = (int)(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1048576L); ServerRamSlider.Maximum = Math.Max(2048, maxRam); }
            catch { ServerRamSlider.Maximum = 8192; }

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
            string javaPath = FindJava();
            var installer = new ServerInstaller();
            installer.StatusChanged += OnInstallerStatusChanged;
            installer.LogReceived += AppendConsoleOutput;

            string serverDir = Path.Combine(_activeServerConfig.ServerPath, "server");
            string backupDir = Path.Combine(_activeServerConfig.ServerPath, "backup");
            if (!Directory.Exists(serverDir)) Directory.CreateDirectory(serverDir);
            if (!Directory.Exists(backupDir)) Directory.CreateDirectory(backupDir);

            int currentStage = 0;
            bool success = false;

            while (!success)
            {
                try
                {
                    if (currentStage == 0)
                    {
                        await installer.InstallForgeRuntime(serverDir, javaPath, OnServerProgress);
                        currentStage++;
                    }
                    if (currentStage == 1)
                    {
                        await installer.DownloadAndApplyServerData(serverDir, backupDir, OnServerProgress);
                        currentStage++;
                    }
                    if (currentStage == 2)
                    {
                        await installer.UpdateServerMods(serverDir, OnServerProgress);
                        currentStage++;
                    }
                    success = true;
                }
                catch (Exception ex)
                {
                    AppendConsoleOutput($"[ERR] Ошибка на этапе {currentStage + 1}: {ex.Message}");
                    bool retry = await ShowCustomDialog(
                        $"Загрузка оборвалась на этапе {currentStage + 1}.\nОшибка: {ex.Message}\nПродолжить скачивание этого этапа?",
                        "Ошибка скачивания", true);

                    if (!retry)
                    {
                        AppendConsoleOutput("[SYS] Установка отменена. Удаление файлов...");
                        try { Directory.Delete(_activeServerConfig.ServerPath, true); } catch { }
                        SetServerBusy(false);
                        return;
                    }
                }
            }

            _activeServerConfig.IsInstalled = true;
            _settings.ServerModpackVersion = _onlineServerModpackVer;
            _settings.ServerMapVersion = _onlineServerMapVer;
            _needsServerModpackUpdate = false;
            _needsServerMapUpdate = false;
            AppSettings.Save(_settings);

            UpdateServerButtons();
            AppendConsoleOutput("[SYS] Сервер установлен.");
            SetServerBusy(false);
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
                await installer.UpdateServerMods(Path.Combine(_activeServerConfig.ServerPath, "server"), OnServerProgress);
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

            if (_needsServerModpackUpdate)
            {
                await ShowCustomDialog("Сначала обновите моды сервера!");
                return;
            }

            if (_needsServerMapUpdate)
            {
                bool updateMap = await ShowCustomDialog("Доступно обновление карты сервера. Хотите обновить? (Текущая карта будет сброшена!)", "Обновление карты", true);
                if (updateMap)
                {
                    SetServerBusy(true, "Обновление карты...");
                    try
                    {
                        var installer = new ServerInstaller();
                        installer.StatusChanged += OnInstallerStatusChanged;
                        string serverDir = Path.Combine(_activeServerConfig.ServerPath, "server");
                        string backupDir = Path.Combine(_activeServerConfig.ServerPath, "backup");
                        await installer.UpdateServerMap(serverDir, backupDir, OnServerProgress);
                        _settings.ServerMapVersion = _onlineServerMapVer;
                        _needsServerMapUpdate = false;
                        AppSettings.Save(_settings);
                        AppendConsoleOutput("[SYS] Карта сервера обновлена.");
                    }
                    catch (Exception ex)
                    {
                        AppendConsoleOutput($"[ERR] Ошибка обновления карты: {ex.Message}");
                        SetServerBusy(false);
                        return;
                    }
                    finally
                    {
                        SetServerBusy(false);
                    }
                }
                else
                {

                    _settings.ServerMapVersion = _onlineServerMapVer;
                    _needsServerMapUpdate = false;
                    AppSettings.Save(_settings);
                }
            }

            try
            {
                SaveActiveServerConfig();
                EnsureServerManagerInitialized();

                ServerConsoleOutput.Text = "";
                AppendConsoleOutput("[SYS] Запуск сервера...");
                UpdateServerButtons();

                string javaPath = FindJava();
                await _serverManager!.StartAsync(_activeServerConfig, javaPath);
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
                await Task.Run(() =>
                {
                    if (Directory.Exists(backupDir) && Directory.Exists(serverDir))
                    {
                        foreach (var dir in Directory.GetDirectories(backupDir))
                        {
                            string targetDir = Path.Combine(serverDir, Path.GetFileName(dir));
                            if (Directory.Exists(targetDir)) Directory.Delete(targetDir, true);
                        }
                        foreach (var file in Directory.GetFiles(backupDir))
                        {
                            string targetFile = Path.Combine(serverDir, Path.GetFileName(file));
                            if (File.Exists(targetFile)) File.Delete(targetFile);
                        }
                    }
                    ServerInstaller.CopyDirectoryContents(backupDir, serverDir);
                });
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
            if (string.IsNullOrWhiteSpace(command)) return;

            AppendConsoleOutput($"> {command}");
            ServerConsoleInput.Text = "";

            if (_waitingForPortKillConfirmation)
            {
                _waitingForPortKillConfirmation = false;
                if (command.Equals("Y", StringComparison.OrdinalIgnoreCase))
                {
                    AppendConsoleOutput("[SYS] Принудительное завершение всех процессов Java...");
                    try
                    {
                        foreach (var process in Process.GetProcessesByName("java"))
                        {
                            process.Kill();
                        }
                        AppendConsoleOutput("[SYS] Процессы завершены. Попробуйте запустить сервер снова.");
                    }
                    catch (Exception ex)
                    {
                        AppendConsoleOutput($"[ERR] Не удалось завершить процессы: {ex.Message}");
                    }
                }
                else
                {
                    AppendConsoleOutput("[SYS] Действие отменено.");
                }
                return;
            }

            if (_serverManager == null) return;
            _serverManager.SendCommand(command);
        }

        private void AppendConsoleOutput(string line)
        {
            _consoleQueue.Enqueue(line);
        }

        private void FlushConsoleQueue()
        {
            if (_consoleQueue.IsEmpty) return;

            var builder = new System.Text.StringBuilder();
            bool portBusy = false;
            while (_consoleQueue.TryDequeue(out var line))
            {
                builder.Append(line).Append('\n');
                if (line.Contains("FAILED TO BIND TO PORT") || line.Contains("Address already in use"))
                    portBusy = true;
            }

            ServerConsoleOutput.AppendText(builder.ToString());

            if (portBusy && !_waitingForPortKillConfirmation)
            {
                _waitingForPortKillConfirmation = true;
                ServerConsoleOutput.AppendText("[SYS] ОШИБКА: Порт занят! Убить зависшие процессы Java? Введите Y или N\n");
            }

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
            bool isStarting = _serverManager != null && _serverManager.CurrentState == ServerState.Starting;
            bool isStopping = _serverManager != null && _serverManager.CurrentState == ServerState.Stopping;
            bool isStopped = _serverManager == null || _serverManager.CurrentState == ServerState.Stopped;
            bool isActive = isRunning || isStarting;

            BtnInstallServer.Visibility = hasConfig && !isInstalled ? Visibility.Visible : Visibility.Collapsed;
            BtnInstallServer.IsEnabled = hasConfig && _activeServerConfig!.EulaAccepted && !_isServerBusy;

            BtnUpdateServerMods.Visibility = hasConfig && isInstalled && isStopped && _needsServerModpackUpdate ? Visibility.Visible : Visibility.Collapsed;
            BtnUpdateServerMods.IsEnabled = !_isServerBusy;

            BtnStartServer.Visibility = isInstalled && isStopped && !_needsServerModpackUpdate ? Visibility.Visible : Visibility.Collapsed;
            BtnStartServer.IsEnabled = !_isServerBusy;
            BtnStopServer.Visibility = isActive ? Visibility.Visible : Visibility.Collapsed;
            BtnRestartServer.Visibility = isActive ? Visibility.Visible : Visibility.Collapsed;
            BtnRestoreBackup.Visibility = isInstalled && isStopped ? Visibility.Visible : Visibility.Collapsed;
            BtnRestoreBackup.IsEnabled = !_isServerBusy;
            BtnOpenServerFolder.Visibility = isInstalled ? Visibility.Visible : Visibility.Collapsed;

            ServerConfigForm.IsEnabled = hasConfig && !_isServerBusy;
            TabGeneralContent.IsEnabled = isStopped && !_isServerBusy;
            TabGeneralContent.ToolTip = !isStopped ? "Остановите сервер, чтобы взаимодействовать" : null;
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
            AppSettings.Save(_settings);
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
