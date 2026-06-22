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

        private const string VER = "7.9.7";
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
        private const string BATTLECRAFT_MOD_VER_URL = "https://raw.githubusercontent.com/pers1k1/vrsns/main/battlecraft_mod_version.txt";
        private const string LAUNCHER_EXE_URL = "https://github.com/pers1k1/BattleCraft-Remake/releases/download/main/BCR.exe";

        private static string BattleCraftJarUrl(string ver) =>
            $"https://github.com/pers1k1/BattleCraft-mod/releases/download/v{ver}/battlecraft-{ver}-all.jar";
        private static readonly string FORGE_JAR_URL = $"https://maven.minecraftforge.net/net/minecraftforge/forge/{MC}-{FORGE}/forge-{MC}-{FORGE}-installer.jar";
        private string _onlineModpackVer = "0.0";
        private string _onlineBattleCraftModVer = "0.0";
        private string _onlineServerModpackVer = "0.0";
        private string _onlineServerMapVer = "0.0";
        private bool _needsBattleCraftModUpdate = false;
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

        private const int GBASE = HORIZON + 6;
        private readonly int[] _mtnFar = new int[SCN_W];
        private readonly int[] _mtnNear = new int[SCN_W];
        private readonly int[] _forest = new int[SCN_W];
        private readonly int[] _ground = new int[SCN_W];

        private int[] _treeX = Array.Empty<int>(), _treeH = Array.Empty<int>(), _treeKind = Array.Empty<int>(), _treeBaseY = Array.Empty<int>();
        private double[] _treeSeed = Array.Empty<double>(), _treeDim = Array.Empty<double>();

        private int[] _canX = Array.Empty<int>(), _canY = Array.Empty<int>(), _canR = Array.Empty<int>(), _sakIdx = Array.Empty<int>();
        private bool[] _canSak = Array.Empty<bool>();
        private double[] _fallX = Array.Empty<double>(), _fallY = Array.Empty<double>(), _fallP = Array.Empty<double>();
        private int[] _fallK = Array.Empty<int>();

        private double _snowCover, _leafCover, _rainWet;
        private readonly double[] _snowPile = new double[SCN_W];
        private readonly double[] _pileTmp = new double[SCN_W];
        private int[] _puddleX = Array.Empty<int>();
        private double[] _puddleR = Array.Empty<double>();

        private int[] _starX = Array.Empty<int>(), _starY = Array.Empty<int>();
        private double[] _starP = Array.Empty<double>();

        private double[] _cloudX = Array.Empty<double>(), _cloudY = Array.Empty<double>(), _cloudW = Array.Empty<double>();

        private double[] _pX = Array.Empty<double>(), _pY = Array.Empty<double>(), _pP = Array.Empty<double>();
        private double[] _pV = Array.Empty<double>(), _pZ = Array.Empty<double>();

        private double _cometX, _cometY; private int _cometLife;

        private double _wxIntensity = 1.0;
        private double _wxTarget = 1.0;
        private Weather _wxPending = Weather.Clear;
        private bool _wxSwitching;

        private int _flash;
        private int _boltX;
        private readonly List<(int x, int y)> _bolt = new();
        private double[] _splX = Array.Empty<double>(), _splY = Array.Empty<double>();
        private int[] _splLife = Array.Empty<int>();

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
                    _ground[x] = HORIZON + (int)Math.Round(2.6 * Math.Sin(x * 0.045 + 0.7) + 1.7 * Math.Sin(x * 0.12 + 2.3) + 1.1 * Math.Sin(x * 0.31 + 1.1));
                    _ground[x] = Math.Max(HORIZON - 5, Math.Min(HORIZON + 4, _ground[x]));
                }

                int tc = 36;
                int clearLo = (int)(SCN_W * 0.40) - 13, clearHi = (int)(SCN_W * 0.40) + 13;
                var gen = new List<(int x, int baseY, int h, int kind, double seed, double dim, double z)>();
                int guard = 0;
                while (gen.Count < tc && guard++ < tc * 30)
                {
                    int x = _sceneRng.Next(2, SCN_W - 2);
                    double z = _sceneRng.NextDouble();
                    if (x >= clearLo && x <= clearHi && z > 0.35) continue;
                    int cx = ((x % SCN_W) + SCN_W) % SCN_W;
                    int baseY = _forest[cx] - 3 + (int)(z * 10);
                    int h = Math.Max(6, (int)((8 + _sceneRng.Next(0, 7)) * (0.7 + 0.55 * z)));
                    int kind = _sceneRng.NextDouble() < 0.45 ? 1 : 0;
                    gen.Add((x, baseY, h, kind, _sceneRng.NextDouble(), 0.74 + 0.26 * z, z));
                }
                gen.Sort((a, b) => a.baseY.CompareTo(b.baseY));

                _treeX = new int[gen.Count]; _treeH = new int[gen.Count]; _treeKind = new int[gen.Count];
                _treeBaseY = new int[gen.Count]; _treeSeed = new double[gen.Count]; _treeDim = new double[gen.Count];
                _canX = new int[gen.Count]; _canY = new int[gen.Count]; _canR = new int[gen.Count]; _canSak = new bool[gen.Count];
                var sakIdx = new List<int>();
                for (int i = 0; i < gen.Count; i++)
                {
                    var t = gen[i];
                    _treeX[i] = t.x; _treeBaseY[i] = t.baseY; _treeH[i] = t.h; _treeKind[i] = t.kind; _treeSeed[i] = t.seed; _treeDim[i] = t.dim;
                    int trunkH = Math.Max(3, (int)(t.h * 0.5));
                    int rad = Math.Max(3, (int)(t.h * 0.48));
                    _canX[i] = t.x; _canY[i] = t.baseY - trunkH - rad / 2; _canR[i] = rad; _canSak[i] = t.kind == 1;
                    if (_canSak[i]) sakIdx.Add(i);
                }
                _sakIdx = sakIdx.ToArray();

                int fallN = 56;
                _fallX = new double[fallN]; _fallY = new double[fallN]; _fallP = new double[fallN]; _fallK = new int[fallN];
                for (int i = 0; i < fallN; i++) RespawnFall(i, _sakIdx.Length > 0);

                int pc = 6; _puddleX = new int[pc]; _puddleR = new double[pc];
                for (int i = 0; i < pc; i++) { _puddleX[i] = 14 + _sceneRng.Next(SCN_W - 28); _puddleR[i] = 3 + _sceneRng.NextDouble() * 4; }

                int im = DateTime.Now.Month;
                if (im == 12 || im <= 2) _snowCover = 0.55;
                else if (im >= 9 && im <= 11) _leafCover = 0.3;

                int sc = 70; _starX = new int[sc]; _starY = new int[sc]; _starP = new double[sc];
                for (int i = 0; i < sc; i++) { _starX[i] = _sceneRng.Next(SCN_W); _starY[i] = _sceneRng.Next(HORIZON - 14); _starP[i] = _sceneRng.NextDouble() * 6.28; }

                int cc = 4; _cloudX = new double[cc]; _cloudY = new double[cc]; _cloudW = new double[cc];
                for (int i = 0; i < cc; i++) { _cloudX[i] = _sceneRng.Next(SCN_W); _cloudY[i] = 10 + _sceneRng.Next(28); _cloudW[i] = 14 + _sceneRng.Next(16); }

                RollWeather(true);

                _worldTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
                _worldTimer.Tick += (s, e) => WorldTick();
                _worldTimer.Start();

                _weatherTimer = new System.Windows.Threading.DispatcherTimer { Interval = RandWeatherInterval() };
                _weatherTimer.Tick += (s, e) => { RollWeather(); _weatherTimer!.Interval = RandWeatherInterval(); };
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

        private TimeSpan RandWeatherInterval() => TimeSpan.FromSeconds(_sceneRng.Next(70, 165));

        private Weather PickWeather()
        {
            int hour = DateTime.Now.Hour;
            bool night = hour >= 21 || hour < 4;
            int month = DateTime.Now.Month;
            bool winter = month == 12 || month <= 2;
            bool autumn = month >= 9 && month <= 11;
            bool spring = month >= 3 && month <= 5;

            if (night && _sceneRng.NextDouble() < 0.18) return Weather.Comets;

            var pool = new List<Weather>();
            void Add(Weather w, int n) { for (int i = 0; i < n; i++) pool.Add(w); }
            Add(Weather.Clear, 34);
            Add(Weather.Wind, 16);
            Add(Weather.Fog, 8 + (autumn ? 8 : 0) + (hour >= 5 && hour < 9 ? 6 : 0));
            Add(Weather.Rain, winter ? 4 : (spring || autumn ? 22 : 15));
            Add(Weather.Snow, winter ? 32 : (month == 11 ? 12 : 0));
            Add(Weather.Sakura, spring ? 22 : 0);
            Add(Weather.Leaves, autumn ? 24 : (month == 9 ? 10 : 0));
            return pool[_sceneRng.Next(pool.Count)];
        }

        private void RollWeather(bool immediate = false)
        {
            var next = PickWeather();

            if (immediate)
            {
                _weather = next; _wxSwitching = false; _wxIntensity = 1.0; _wxTarget = 1.0;
                SetupParticles();
                return;
            }

            if (next == _weather && !_wxSwitching) { _wxTarget = 1.0; return; }

            _wxPending = next; _wxTarget = 0.0; _wxSwitching = true;
        }

        private void SetupParticles()
        {
            int n = _weather switch { Weather.Rain => 120, Weather.Snow => 90, Weather.Sakura => 60, Weather.Leaves => 55, _ => 0 };
            _pX = new double[n]; _pY = new double[n]; _pP = new double[n]; _pV = new double[n]; _pZ = new double[n];
            for (int i = 0; i < n; i++)
            {
                _pX[i] = _sceneRng.NextDouble() * SCN_W;
                _pY[i] = _sceneRng.NextDouble() * SCN_H - 4;
                _pP[i] = _sceneRng.NextDouble() * 6.28;
                _pV[i] = 0.7 + _sceneRng.NextDouble() * 0.6;
                _pZ[i] = _sceneRng.NextDouble();
            }

            int sn = _weather == Weather.Rain ? 26 : 0;
            _splX = new double[sn]; _splY = new double[sn]; _splLife = new int[sn];
            _bolt.Clear(); _flash = 0;
            _cometLife = 0;
        }

        private void StepWeatherIntensity()
        {
            const double step = 0.02;
            if (_wxIntensity < _wxTarget) _wxIntensity = Math.Min(_wxTarget, _wxIntensity + step);
            else if (_wxIntensity > _wxTarget) _wxIntensity = Math.Max(_wxTarget, _wxIntensity - step);

            if (_wxSwitching && _wxIntensity <= 0.001)
            {
                _weather = _wxPending; SetupParticles();
                _wxSwitching = false; _wxTarget = 1.0;
            }
        }

        private void WorldTick()
        {
            _frame++;
            _blobT += 0.08;
            StepWeatherIntensity();
            UpdateAccumulation();
            RenderScene();
            RenderBlob();
        }

        private static readonly (double h, (int r, int g, int b) top, (int r, int g, int b) horiz, double bright)[] _skyKeys =
        {
            (0.0,  (8, 9, 28),     (22, 18, 52),    0.42),
            (4.5,  (18, 16, 50),   (66, 44, 90),    0.52),
            (6.5,  (42, 35, 86),   (236, 150, 96),  0.78),
            (9.0,  (52, 84, 152),  (172, 202, 230), 0.92),
            (13.0, (58, 104, 170), (150, 188, 224), 1.0),
            (16.0, (54, 92, 168),  (184, 172, 212), 0.95),
            (18.5, (40, 28, 78),   (236, 110, 52),  0.80),
            (20.5, (24, 18, 58),   (118, 52, 62),   0.60),
            (22.0, (10, 10, 34),   (34, 26, 68),    0.46),
        };

        private static (int, int, int) LerpRgb((int r, int g, int b) a, (int r, int g, int b) b, double t)
            => ((int)(a.r + (b.r - a.r) * t), (int)(a.g + (b.g - a.g) * t), (int)(a.b + (b.b - a.b) * t));

        private void TimeColors(out (int r, int g, int b) top, out (int r, int g, int b) horiz, out double bright, out double nightF)
        {
            double h = CurHour();
            var keys = _skyKeys;
            int n = keys.Length;
            int idx = 0;
            for (int i = 0; i < n; i++) if (h >= keys[i].h) idx = i;

            var a = keys[idx];
            var b = keys[(idx + 1) % n];
            double h0 = a.h, h1 = b.h <= a.h ? b.h + 24 : b.h;
            double hh = h < h0 ? h + 24 : h;
            double t = h1 > h0 ? (hh - h0) / (h1 - h0) : 0;
            t = t * t * (3 - 2 * t);

            top = LerpRgb(a.top, b.top, t);
            horiz = LerpRgb(a.horiz, b.horiz, t);
            bright = a.bright + (b.bright - a.bright) * t;
            nightF = Math.Max(0, Math.Min(1, (0.62 - bright) / 0.18));
        }

        private void RenderScene()
        {
            if (_sceneBmp == null) return;
            try
            {
                TimeColors(out var top, out var horiz, out var bright, out var nightF);
                double h = CurHour();
                bool night = h >= 21 || h < 4;

                double storm = _weather == Weather.Rain ? _wxIntensity : 0;
                if (storm > 0)
                {
                    top = LerpRgb(top, (46, 48, 62), 0.6 * storm);
                    horiz = LerpRgb(horiz, (70, 74, 90), 0.55 * storm);
                    bright *= 1 - 0.4 * storm;
                    nightF *= 1 - storm;
                }

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

                if (nightF > 0.01)
                {
                    for (int i = 0; i < _starX.Length; i++)
                    {
                        double tw = (0.4 + 0.6 * Math.Abs(Math.Sin(_frame * 0.12 + _starP[i]))) * nightF;
                        BP(_starX[i], _starY[i], 220, 220, 255, tw);
                    }
                }

                DrawSunMoon(night);

                double cloudSpd = _weather == Weather.Wind ? 0.9 : (0.18 + 0.5 * storm);
                for (int i = 0; i < _cloudX.Length; i++)
                {
                    _cloudX[i] += cloudSpd;
                    if (_cloudX[i] > SCN_W + _cloudW[i]) _cloudX[i] = -_cloudW[i];
                    DrawCloud((int)_cloudX[i], (int)_cloudY[i], (int)_cloudW[i], bright, night, storm);
                }
                if (storm > 0) DrawOvercast(storm, night, bright);

                int gmonth = DateTime.Now.Month;
                bool gWinter = gmonth == 12 || gmonth <= 2;
                bool gAutumn = gmonth >= 9 && gmonth <= 11;
                bool gSpring = gmonth >= 3 && gmonth <= 5;
                double snowAmount = _snowCover;

                DrawMountains(gWinter, gAutumn, gSpring, bright, snowAmount);

                (int r, int g, int b) gDirt = gWinter ? (138, 150, 176) : gAutumn ? (78, 56, 36) : gSpring ? (58, 92, 50) : (52, 86, 44);
                (int r, int g, int b) gFloor = gWinter ? (118, 132, 160) : gAutumn ? (50, 36, 24) : gSpring ? (34, 60, 34) : (30, 54, 30);

                int gh = SCN_H - HORIZON;
                for (int x = 0; x < SCN_W; x++)
                {
                    int gtop = _ground[x];
                    for (int y = gtop; y < SCN_H; y++)
                    {
                        double front = (double)(y - HORIZON) / gh;
                        double shade = 0.72 + 0.28 * Math.Max(0, front);
                        double n = 0.9 + 0.2 * Hash2(x, y);
                        double m = shade * n;
                        SP(x, y, (byte)(gDirt.r * bright * m), (byte)(gDirt.g * bright * m), (byte)(gDirt.b * bright * m));
                    }
                }
                for (int x = 0; x < SCN_W; x++)
                    for (int y = Math.Max(_ground[x], _forest[x]); y < SCN_H; y++)
                    {
                        double n = 0.88 + 0.24 * Hash2(x + 5, y + 11);
                        SP(x, y, (byte)(gFloor.r * bright * n), (byte)(gFloor.g * bright * n), (byte)(gFloor.b * bright * n));
                    }

                DrawGroundDetail(gWinter, gAutumn, gSpring, bright);
                DrawAccumulation(bright);
                DrawTrees(gWinter, gAutumn, gSpring, bright, snowAmount);
                if (gSpring) UpdateFalling(true);
                else if (gAutumn) UpdateFalling(false);

                bool badWeather = _weather == Weather.Rain || _weather == Weather.Snow;
                if (!badWeather) DrawKite();

                switch (_weather)
                {
                    case Weather.Rain: UpdateRain(); break;
                    case Weather.Snow: UpdateSnow(); break;
                    case Weather.Wind: UpdateWind(); break;
                    case Weather.Fog: DrawFog(0.34 * _wxIntensity); break;
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
            double nf = night ? 0.62 : 1.0;
            byte cr = (byte)(48 * nf), cg = (byte)(44 * nf), cb = (byte)(62 * nf);
            byte lr = (byte)(70 * nf), lg = (byte)(64 * nf), lb = (byte)(88 * nf);
            byte kr = (byte)(212 * nf), kg = (byte)(168 * nf), kb = (byte)(138 * nf);

            SP(px - 1, gy, cr, cg, cb); SP(px - 1, gy - 1, cr, cg, cb);
            SP(px + 1, gy, cr, cg, cb); SP(px + 1, gy - 1, cr, cg, cb);

            SP(px - 1, gy - 2, cr, cg, cb); SP(px, gy - 2, lr, lg, lb); SP(px + 1, gy - 2, cr, cg, cb);
            SP(px - 1, gy - 3, cr, cg, cb); SP(px, gy - 3, lr, lg, lb); SP(px + 1, gy - 3, cr, cg, cb);
            SP(px - 1, gy - 4, cr, cg, cb); SP(px, gy - 4, cr, cg, cb); SP(px + 1, gy - 4, cr, cg, cb);

            SP(px - 2, gy - 3, cr, cg, cb);
            SP(px + 2, gy - 4, cr, cg, cb); SP(px + 2, gy - 5, cr, cg, cb);

            SP(px, gy - 5, kr, kg, kb);
            SP(px, gy - 6, kr, kg, kb); SP(px + 1, gy - 6, kr, kg, kb);
            SP(px, gy - 7, (byte)(40 * nf), (byte)(34 * nf), (byte)(30 * nf)); SP(px + 1, gy - 7, (byte)(40 * nf), (byte)(34 * nf), (byte)(30 * nf));

            SP(px + 2, gy - 6, cr, cg, cb); SP(px + 1, gy - 8, cr, cg, cb);

            var col = (Color)FindResource("AccentColor");
            byte ar = (byte)(col.R * nf), ag = (byte)(col.G * nf), ab = (byte)(col.B * nf);
            byte ah = (byte)Math.Min(255, col.R * nf + 40), ah2 = (byte)Math.Min(255, col.G * nf + 40), ah3 = (byte)Math.Min(255, col.B * nf + 40);
            for (int dx = -3; dx <= 3; dx++) SP(px + dx, gy - 9, ar, ag, ab);
            for (int dx = -2; dx <= 2; dx++) SP(px + dx, gy - 10, ah, ah2, ah3);
            SP(px, gy - 11, ah, ah2, ah3);
            SP(px - 3, gy - 8, ar, ag, ab); SP(px + 3, gy - 8, ar, ag, ab);
        }

        private void UpdateRain()
        {
            double inten = _wxIntensity;
            int count = (int)Math.Ceiling(_pX.Length * inten);
            double afade = Math.Min(1, inten * 1.4);

            for (int i = 0; i < count; i++)
            {
                double z = _pZ[i];
                double dep = 0.5 + 0.5 * (1 - z);
                double sp = (6.0 + _pV[i] * 2.5) * dep;
                _pX[i] -= 1.4 * dep; _pY[i] += sp;
                int col = (((int)_pX[i]) % SCN_W + SCN_W) % SCN_W;
                double land = _ground[col] + (1 - z) * (SCN_H - _ground[col]);
                if (_pY[i] >= land || _pX[i] < -2)
                {
                    if (_pY[i] >= land && _sceneRng.NextDouble() < 0.5) SpawnSplash(col, (int)land);
                    _pX[i] = _sceneRng.NextDouble() * SCN_W; _pY[i] = -2; _pZ[i] = _sceneRng.NextDouble();
                    continue;
                }
                int y = (int)_pY[i];
                double slant = -1.4 / sp;
                int len = 3 + (int)(_pV[i] * 3 * dep);
                double da = afade * (0.35 + 0.65 * (1 - z));
                for (int k = 1; k <= len; k++)
                {
                    double f = 1.0 - (double)k / len;
                    BPx(_pX[i] - slant * k, y - k, 158, 196, 236, (0.12 + 0.5 * f) * da);
                }
                BPx(_pX[i], y, 206, 228, 255, 0.85 * da);
            }

            UpdateSplashes(afade);

            if (inten > 0.55 && _flash <= 0 && _sceneRng.NextDouble() < 0.014) TriggerLightning();
            if (_flash > 0) { DrawLightning(); _flash--; }

            DrawFog(0.09 * inten);
        }

        private void SpawnSplash(int x, int y)
        {
            for (int i = 0; i < _splLife.Length; i++)
            {
                if (_splLife[i] <= 0)
                {
                    _splX[i] = x;
                    _splY[i] = y;
                    _splLife[i] = 5;
                    return;
                }
            }
        }

        private void UpdateSplashes(double afade)
        {
            for (int i = 0; i < _splLife.Length; i++)
            {
                if (_splLife[i] <= 0) continue;
                int sx = (int)_splX[i], sy = (int)_splY[i];
                int spread = 5 - _splLife[i];
                double a = (_splLife[i] / 5.0) * 0.6 * afade;
                BP(sx - spread, sy, 170, 200, 240, a);
                BP(sx + spread, sy, 170, 200, 240, a);
                BP(sx, sy - 1, 180, 210, 245, a * 0.6);
                _splLife[i]--;
            }
        }

        private void TriggerLightning()
        {
            _flash = 7;
            _boltX = _sceneRng.Next(28, SCN_W - 28);
            _bolt.Clear();
            int x = _boltX, y = 13;
            while (y < HORIZON - 2)
            {
                _bolt.Add((x, y));
                x += _sceneRng.Next(-3, 4);
                y += _sceneRng.Next(2, 5);
            }
        }

        private void DrawLightning()
        {
            double fa = 0.5 * Math.Pow(_flash / 7.0, 1.4);
            for (int y = 0; y < SCN_H; y++)
            {
                double topGlow = 1.0 + 0.7 * (1 - (double)y / SCN_H);
                for (int x = 0; x < SCN_W; x++) BP(x, y, 232, 238, 255, Math.Min(0.9, fa * topGlow));
            }

            if (_flash >= 3)
            {
                double ba = Math.Min(1, _flash / 7.0 + 0.3);
                if (_bolt.Count > 0)
                {
                    var (ox, oy) = _bolt[0];
                    for (int dy = -3; dy <= 3; dy++)
                        for (int dx = -4; dx <= 4; dx++)
                            if (dx * dx + dy * dy <= 14) BP(ox + dx, oy + dy, 240, 246, 255, ba * 0.5);
                }
                foreach (var (x, y) in _bolt)
                {
                    SP(x, y, 255, 255, 255);
                    BP(x - 1, y, 200, 220, 255, ba * 0.7);
                    BP(x + 1, y, 200, 220, 255, ba * 0.7);
                }
            }
        }

        private void UpdateSnow()
        {
            double inten = _wxIntensity;
            int count = (int)Math.Ceiling(_pX.Length * inten);
            double afade = Math.Min(1, inten * 1.4);
            double gust = Math.Sin(_frame * 0.02) * 0.5;

            for (int i = 0; i < count; i++)
            {
                double z = _pZ[i];
                double dep = 0.45 + 0.55 * (1 - z);
                _pY[i] += (0.8 + _pV[i] * 0.8) * dep;
                _pX[i] += (Math.Sin(_pY[i] * 0.12 + _pP[i]) * 0.6 + gust) * dep;
                int col = (((int)_pX[i]) % SCN_W + SCN_W) % SCN_W;
                double land = _ground[col] + (1 - z) * (SCN_H - _ground[col]);
                if (_pY[i] >= land)
                {
                    _snowPile[col] = Math.Min(7.0, _snowPile[col] + 0.05 * inten);
                    _pY[i] = -1; _pX[i] = _sceneRng.NextDouble() * SCN_W; _pZ[i] = _sceneRng.NextDouble();
                    continue;
                }
                int x = col, y = (int)_pY[i];
                double da = afade * (0.4 + 0.6 * (1 - z));
                bool big = _pV[i] > 1.05 && z < 0.5;
                BP(x, y, 245, 248, 255, 0.92 * da);
                if (big)
                {
                    BP(x + 1, y, 235, 240, 252, 0.6 * da);
                    BP(x, y + 1, 235, 240, 252, 0.6 * da);
                }
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
                    double edge = Math.Sin((double)k / len * Math.PI) * _wxIntensity;
                    BP((int)xx, (int)yy, 225, 226, 240, 0.5 * edge);
                    BP((int)xx, (int)yy + 1, 210, 212, 230, 0.22 * edge);
                }
            }
        }

        private void UpdateSakura()
        {
            double afade = Math.Min(1, _wxIntensity * 1.4);
            int count = (int)Math.Ceiling(_pX.Length * _wxIntensity);
            for (int i = 0; i < count; i++)
            {
                double z = _pZ[i];
                double dep = 0.5 + 0.5 * (1 - z);
                _pY[i] += 0.9 * dep; _pX[i] += Math.Sin(_pY[i] * 0.10 + _pP[i]) * 1.1 * dep;
                int col = (((int)_pX[i]) % SCN_W + SCN_W) % SCN_W;
                double land = _ground[col] + (1 - z) * (SCN_H - _ground[col]);
                if (_pY[i] >= land) { _pY[i] = -1; _pX[i] = _sceneRng.NextDouble() * SCN_W; _pZ[i] = _sceneRng.NextDouble(); continue; }
                int x = col, y = (int)_pY[i];
                double da = afade * (0.45 + 0.55 * (1 - z));
                bool flip = ((int)(_pY[i] * 0.3 + _pP[i] * 3) & 1) == 0;
                BP(x, y, 248, 196, 222, 0.92 * da);
                BP(flip ? x + 1 : x - 1, y, 240, 170, 205, 0.7 * da);
            }
        }

        private void UpdateLeaves()
        {
            double afade = Math.Min(1, _wxIntensity * 1.4);
            int count = (int)Math.Ceiling(_pX.Length * _wxIntensity);
            for (int i = 0; i < count; i++)
            {
                double z = _pZ[i];
                double dep = 0.5 + 0.5 * (1 - z);
                _pY[i] += 1.1 * dep; _pX[i] += Math.Sin(_pY[i] * 0.08 + _pP[i]) * 1.6 * dep;
                int col = (((int)_pX[i]) % SCN_W + SCN_W) % SCN_W;
                double land = _ground[col] + (1 - z) * (SCN_H - _ground[col]);
                if (_pY[i] >= land) { _pY[i] = -2; _pX[i] = _sceneRng.NextDouble() * SCN_W; _pZ[i] = _sceneRng.NextDouble(); continue; }
                int x = col, y = (int)_pY[i];

                int kind = (int)(_pP[i] * 3) % 3;
                (byte r, byte g, byte b) c = kind == 0 ? ((byte)214, (byte)120, (byte)42)
                                          : kind == 1 ? ((byte)190, (byte)70, (byte)48)
                                          :             ((byte)206, (byte)160, (byte)60);
                double da = afade * (0.45 + 0.55 * (1 - z));
                bool flip = ((int)(_pY[i] * 0.25 + _pP[i] * 4) & 1) == 0;
                BP(x, y, c.r, c.g, c.b, 0.95 * da);
                BP(flip ? x + 1 : x - 1, y, c.r, c.g, c.b, 0.8 * da);
            }
        }

        private void DrawFog(double strength)
        {
            if (strength <= 0) return;
            for (int y = 0; y < SCN_H; y++)
            {
                double a = strength * (0.45 + 0.8 * y / SCN_H);
                for (int x = 0; x < SCN_W; x++) BP(x, y, 206, 208, 220, Math.Min(0.85, a));
            }
            for (int bi = 0; bi < 3; bi++)
            {
                double by = HORIZON - 10 + bi * 12 + Math.Sin(_frame * 0.015 + bi) * 2;
                double drift = _frame * (0.5 + bi * 0.18) + bi * 53;
                for (int x = 0; x < SCN_W; x++)
                {
                    double wv = 0.5 + 0.5 * Math.Sin(x * 0.06 + drift * 0.04 + bi * 1.7);
                    double thick = 2 + 3 * wv;
                    double ca = strength * 1.7 * wv;
                    for (int dy = 0; dy < thick; dy++) BP(x, (int)by + dy, 216, 218, 228, Math.Min(0.6, ca * (1 - dy / thick)));
                }
            }
        }

        private void UpdateComets()
        {
            if (_cometLife <= 0 && _sceneRng.NextDouble() < 0.06)
            {
                _cometX = _sceneRng.Next(SCN_W / 2, SCN_W); _cometY = _sceneRng.Next(HORIZON - 26); _cometLife = 16;
            }
            if (_cometLife > 0)
            {
                _cometX -= 6; _cometY += 3; _cometLife--;
                int x = (int)_cometX, y = (int)_cometY;
                CometPixel(x, y, 255, 255, 255, _wxIntensity);
                CometPixel(x + 1, y, 255, 255, 255, 0.7 * _wxIntensity);
                CometPixel(x, y + 1, 238, 244, 255, 0.5 * _wxIntensity);
                for (int i = 1; i <= 8; i++) CometPixel(x + i * 2, y - i, 200, 215, 255, Math.Max(0, 0.8 - i * 0.1) * _wxIntensity);
            }
        }

        private void CometPixel(int x, int y, byte r, byte g, byte b, double a)
        {
            if ((uint)x < SCN_W && y >= Math.Min(_mtnFar[x], _mtnNear[x])) return;
            BP(x, y, r, g, b, a);
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

        private void DrawMountains(bool winter, bool autumn, bool spring, double bright, double snowAmount)
        {
            (int r, int g, int b) farRock = winter ? (90, 98, 126) : autumn ? (96, 82, 100) : (70, 58, 104);
            (int r, int g, int b) nearRock = winter ? (58, 66, 94) : autumn ? (58, 46, 56) : (40, 30, 66);

            for (int x = 0; x < SCN_W; x++)
            {
                int ft = Math.Max(0, _mtnFar[x]);
                for (int y = ft; y < GBASE; y++)
                    SP(x, y, (byte)(farRock.r * bright), (byte)(farRock.g * bright), (byte)(farRock.b * bright));
                if (snowAmount > 0)
                {
                    int sh = (int)(snowAmount * (HORIZON - ft) * 0.72 + Hash2(x, 3) * 2);
                    int edge = Math.Min(HORIZON, ft + sh);
                    for (int y = ft; y < edge; y++)
                        BP(x, y, (byte)(212 * bright), (byte)(222 * bright), (byte)(240 * bright), 0.9);
                }
            }
            for (int x = 0; x < SCN_W; x++)
            {
                int nt = Math.Max(0, _mtnNear[x]);
                for (int y = nt; y < GBASE; y++)
                    SP(x, y, (byte)(nearRock.r * bright), (byte)(nearRock.g * bright), (byte)(nearRock.b * bright));
                if (snowAmount > 0)
                {
                    int sh = (int)(snowAmount * (HORIZON - nt) * 0.62 + Hash2(x, 9) * 2);
                    int edge = Math.Min(HORIZON, nt + sh);
                    for (int y = nt; y < edge; y++)
                    {
                        double sd = (double)(y - nt) / Math.Max(1, sh);
                        BP(x, y, (byte)((238 - 14 * sd) * bright), (byte)((245 - 12 * sd) * bright), (byte)(255 * bright), 0.95);
                    }
                    if (edge < HORIZON)
                        BP(x, edge, (byte)(nearRock.r * bright), (byte)(nearRock.g * bright), (byte)(nearRock.b * bright), 0.45);
                }
            }
        }

        private void DrawTrees(bool winter, bool autumn, bool spring, double bright, double snowAmount)
        {
            double windAmp = _weather == Weather.Wind ? 2.7 : _weather == Weather.Rain ? 1.3 : 0.45;
            if (_weather == Weather.Wind || _weather == Weather.Rain) windAmp *= _wxIntensity;
            double gust = _weather == Weather.Wind ? 0.7 + 0.6 * (0.5 + 0.5 * Math.Sin(_frame * 0.025)) : 1.0;
            for (int i = 0; i < _treeX.Length; i++)
            {
                double sway = Math.Sin(_frame * 0.11 + _treeSeed[i] * 6.28) * windAmp * gust;
                DrawLeafTree(_treeX[i], _treeBaseY[i], _treeH[i], winter, autumn, spring, bright * _treeDim[i], snowAmount, _treeSeed[i], _treeKind[i] == 1, sway);
            }
        }

        private void DrawLeafTree(int tx, int baseY, int h, bool winter, bool autumn, bool spring, double bright, double snow, double seed, bool sakura, double sway)
        {
            int trunkH = Math.Max(3, (int)(h * 0.5));
            int rad = Math.Max(3, (int)(h * 0.48));
            int forkY = baseY - trunkH;
            int seg = (int)(seed * 50);
            byte tr = (byte)(60 * bright), tg = (byte)(42 * bright), tb = (byte)(26 * bright);
            byte dr = (byte)(40 * bright), dg = (byte)(28 * bright), db = (byte)(18 * bright);

            for (int k = 0; k <= trunkH; k++)
            {
                int bend = (int)Math.Round(sway * k / (double)Math.Max(1, trunkH));
                SP(tx + bend, baseY - k, tr, tg, tb);
                if (k < trunkH - 1) SP(tx + bend - 1, baseY - k, dr, dg, db);
            }
            SP(tx, baseY + 1, dr, dg, db);

            double cbx = tx + sway;
            int nb = 3 + (int)(Hash2(tx, baseY) * 2.99);
            var tipX = new int[nb];
            var tipY = new int[nb];
            for (int j = 0; j < nb; j++)
            {
                double f = nb == 1 ? 0.5 : (double)j / (nb - 1);
                double ang = -Math.PI / 2 + (f - 0.5) * 1.55 + (Hash2(tx + j * 7, forkY) - 0.5) * 0.4;
                double len = rad * 0.85 + Hash2(tx + j, baseY + j) * rad * 0.6;
                double bx = cbx, by = forkY;
                int steps = Math.Max(2, (int)len);
                for (int s = 0; s <= steps; s++)
                {
                    SP((int)Math.Round(bx), (int)Math.Round(by), tr, tg, tb);
                    if (s == steps / 2) { SP((int)Math.Round(bx) + (j % 2 == 0 ? 1 : -1), (int)Math.Round(by), dr, dg, db); }
                    bx += Math.Cos(ang);
                    by += Math.Sin(ang);
                }
                tipX[j] = (int)Math.Round(bx);
                tipY[j] = (int)Math.Round(by);
            }

            if (winter)
            {
                if (snow > 0)
                {
                    for (int j = 0; j < nb; j++) BP(tipX[j], tipY[j] - 1, 240, 246, 255, 0.85 * snow);
                    BP((int)Math.Round(cbx), forkY - 1, 238, 244, 255, 0.6 * snow);
                }
                return;
            }

            double keep = autumn ? 0.5 : 1.0;
            FoliageBlob((int)Math.Round(cbx), forkY - rad / 2, Math.Max(2, rad - 1), seg, sakura, autumn, spring, bright, keep);
            for (int j = 0; j < nb; j++)
                FoliageBlob(tipX[j], tipY[j], Math.Max(2, rad - 2), seg + j * 13, sakura, autumn, spring, bright, keep);

            if (snow > 0)
                for (int j = 0; j < nb; j++)
                    BP(tipX[j], tipY[j] - Math.Max(2, rad - 2), 238, 244, 255, 0.7 * snow);
        }

        private void FoliageBlob(int cx, int cy, int r, int seg, bool sakura, bool autumn, bool spring, double bright, double keep)
        {
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (dx * dx + dy * dy > r * r) continue;
                    int px = cx + dx, py = cy + dy;
                    double rr = Hash2(px + seg, py);
                    if (keep < 1.0 && Hash2(px + seg + 11, py + 5) > keep) continue;
                    (byte cr, byte cg, byte cb) c;
                    if (sakura)
                        c = spring ? (rr < 0.5 ? ((byte)248, (byte)190, (byte)220) : rr < 0.8 ? ((byte)252, (byte)224, (byte)238) : ((byte)255, (byte)252, (byte)254))
                          : autumn ? (rr < 0.55 ? ((byte)214, (byte)150, (byte)150) : ((byte)206, (byte)168, (byte)92))
                          :          ((byte)46, (byte)(96 + (rr < 0.5 ? 14 : -10)), (byte)52);
                    else
                        c = autumn ? (rr < 0.34 ? ((byte)206, (byte)104, (byte)40) : rr < 0.67 ? ((byte)182, (byte)66, (byte)44) : ((byte)204, (byte)156, (byte)54))
                          :          ((byte)38, (byte)(90 + (rr < 0.5 ? 16 : -8)), (byte)44);
                    double sh = dy < -r / 3 ? 1.12 : dy > r / 3 ? 0.78 : 1.0;
                    SP(px, py, (byte)Math.Min(255, c.cr * bright * sh), (byte)Math.Min(255, c.cg * bright * sh), (byte)Math.Min(255, c.cb * bright * sh));
                    if (spring && !sakura && rr > 0.9) BP(px, py, 246, 240, 250, 0.85);
                }
        }

        private void RespawnFall(int i, bool sakuraOnly)
        {
            if (_canX.Length == 0) return;
            int t = sakuraOnly && _sakIdx.Length > 0 ? _sakIdx[_sceneRng.Next(_sakIdx.Length)] : _sceneRng.Next(_canX.Length);
            int r = _canR[t];
            _fallX[i] = _canX[t] + _sceneRng.Next(-r, r + 1);
            _fallY[i] = _canY[t] + _sceneRng.Next(-r, 1);
            _fallP[i] = _sceneRng.NextDouble() * 6.28;
            _fallK[i] = _sceneRng.Next(3);
        }

        private void UpdateFalling(bool spring)
        {
            double wind = Math.Sin(_frame * 0.03) * (spring ? 0.6 : 0.9);
            for (int i = 0; i < _fallX.Length; i++)
            {
                _fallY[i] += (spring ? 0.45 : 0.6) + 0.25 * (0.5 + 0.5 * Math.Sin(_fallP[i] + _frame * 0.1));
                _fallX[i] += Math.Sin(_fallY[i] * (spring ? 0.12 : 0.09) + _fallP[i]) * (spring ? 0.9 : 1.4) + wind;
                int col = (((int)_fallX[i]) % SCN_W + SCN_W) % SCN_W;
                if (_fallY[i] >= _ground[col] + (SCN_H - _ground[col]) * 0.85 || _fallX[i] < -2 || _fallX[i] > SCN_W + 2)
                {
                    RespawnFall(i, spring);
                    continue;
                }
                int x = col, y = (int)_fallY[i];
                (byte r, byte g, byte b) c = spring
                    ? (_fallK[i] == 0 ? ((byte)248, (byte)188, (byte)216) : _fallK[i] == 1 ? ((byte)252, (byte)222, (byte)236) : ((byte)242, (byte)166, (byte)204))
                    : (_fallK[i] == 0 ? ((byte)214, (byte)120, (byte)42) : _fallK[i] == 1 ? ((byte)190, (byte)70, (byte)48) : ((byte)206, (byte)160, (byte)60));
                bool flip = ((int)(_fallY[i] * 0.3 + _fallP[i] * 3) & 1) == 0;
                BP(x, y, c.r, c.g, c.b, 0.92);
                BP(flip ? x + 1 : x - 1, y, c.r, c.g, c.b, 0.6);
            }
        }

        private void DrawGroundDetail(bool winter, bool autumn, bool spring, double bright)
        {
            int gh = SCN_H - HORIZON;

            for (int y = HORIZON - 5; y < SCN_H; y++)
            {
                double front = (double)(y - HORIZON) / gh;
                for (int x = 0; x < SCN_W; x++)
                {
                    if (y < _ground[x]) continue;
                    double r = Hash2(x, y);
                    if (spring)
                    {
                        double dens = 0.015 + 0.022 * front;
                        if (r < dens)
                        {
                            double pick = Hash2(x + 9, y + 3);
                            (byte cr, byte cg, byte cb) = pick < 0.3 ? ((byte)244, (byte)180, (byte)210)
                                                        : pick < 0.6 ? ((byte)246, (byte)238, (byte)150)
                                                        : pick < 0.8 ? ((byte)236, (byte)240, (byte)246)
                                                        :              ((byte)210, (byte)150, (byte)236);
                            BP(x, y, cr, cg, cb, 0.85 * Math.Max(0.5, bright));
                        }
                        else if (r < dens + 0.10)
                            BP(x, y, (byte)(86 * bright), (byte)(140 * bright), (byte)(70 * bright), 0.5);
                    }
                    else if (!winter && !autumn)
                    {
                        if (r < 0.10) BP(x, y, (byte)(80 * bright), (byte)(132 * bright), (byte)(64 * bright), 0.5);
                        else if (r > 0.94) BP(x, y, (byte)(44 * bright), (byte)(78 * bright), (byte)(40 * bright), 0.5);
                    }
                }
            }
        }

        private void UpdateAccumulation()
        {
            int m = DateTime.Now.Month;
            bool winter = m == 12 || m <= 2;
            bool autumn = m >= 9 && m <= 11;
            bool snowing = _weather == Weather.Snow;
            bool raining = _weather == Weather.Rain;
            bool leafing = _weather == Weather.Leaves;
            double wx = _wxIntensity;

            double snowTarget = snowing ? 1.0 : (winter ? 0.55 : 0.0);
            double snowRate = snowing ? 0.006 * wx : (winter ? 0.0015 : 0.02);
            _snowCover += Math.Sign(snowTarget - _snowCover) * Math.Min(snowRate, Math.Abs(snowTarget - _snowCover));

            double pileDecay = snowing ? 0.0 : (winter ? 0.008 : 0.06);
            if (pileDecay > 0)
                for (int x = 0; x < SCN_W; x++) _snowPile[x] = Math.Max(0, _snowPile[x] - pileDecay);
            for (int x = 0; x < SCN_W; x++)
            {
                int xl = x > 0 ? x - 1 : x, xr = x < SCN_W - 1 ? x + 1 : x;
                _pileTmp[x] = (_snowPile[xl] + _snowPile[x] * 2 + _snowPile[xr]) * 0.25;
            }
            Array.Copy(_pileTmp, _snowPile, SCN_W);

            double leafTarget = leafing ? 1.0 : (autumn ? 0.3 : 0.0);
            double leafRate = leafing ? 0.004 * wx : 0.006;
            _leafCover += Math.Sign(leafTarget - _leafCover) * Math.Min(leafRate, Math.Abs(leafTarget - _leafCover));

            double wetTarget = raining ? 1.0 : 0.0;
            double wetRate = raining ? 0.02 * wx : 0.01;
            _rainWet += Math.Sign(wetTarget - _rainWet) * Math.Min(wetRate, Math.Abs(wetTarget - _rainWet));
        }

        private void DrawAccumulation(double bright)
        {
            int gh = SCN_H - HORIZON;

            if (_rainWet > 0.01)
            {
                for (int x = 0; x < SCN_W; x++)
                    for (int y = _ground[x]; y < SCN_H; y++)
                        BP(x, y, 24, 28, 40, 0.18 * _rainWet);
                for (int i = 0; i < _puddleX.Length; i++)
                {
                    int px = _puddleX[i];
                    int py = SCN_H - 4 - (i % 3) * 6;
                    double rr = _puddleR[i] * _rainWet;
                    for (int dx = -(int)(rr * 1.6); dx <= (int)(rr * 1.6); dx++)
                        for (int dy = -(int)(rr * 0.5); dy <= (int)(rr * 0.5); dy++)
                        {
                            double e = dx * dx / (rr * rr * 2.56) + dy * dy / (rr * rr * 0.25);
                            if (e > 1) continue;
                            int x = px + dx, y = py + dy;
                            if (y < _ground[((x % SCN_W) + SCN_W) % SCN_W]) continue;
                            double rip = 0.5 + 0.5 * Math.Sin(_frame * 0.18 + dx * 0.6);
                            BP(x, y, (byte)(110 + 40 * rip), (byte)(140 + 40 * rip), (byte)(180 + 40 * rip), 0.55 * _rainWet);
                        }
                }
            }

            if (_leafCover > 0.01)
            {
                for (int y = HORIZON - 4; y < SCN_H; y++)
                {
                    double front = (double)(y - HORIZON) / gh;
                    double dens = _leafCover * (0.05 + 0.18 * Math.Max(0, front));
                    for (int x = 0; x < SCN_W; x++)
                    {
                        if (y < _ground[x]) continue;
                        if (Hash2(x + 31, y + 13) < dens)
                        {
                            double pick = Hash2(x + 7, y + 19);
                            (byte cr, byte cg, byte cb) = pick < 0.34 ? ((byte)206, (byte)104, (byte)40)
                                                        : pick < 0.67 ? ((byte)176, (byte)64, (byte)44)
                                                        :               ((byte)200, (byte)152, (byte)54);
                            BP(x, y, (byte)(cr * bright), (byte)(cg * bright), (byte)(cb * bright), 0.9);
                        }
                    }
                }
            }

            if (_snowCover > 0.01)
            {
                double sb = Math.Max(0.6, bright);
                byte wr = (byte)(238 * sb), wg = (byte)(244 * sb), wb = (byte)(252 * sb);
                for (int y = HORIZON - 5; y < SCN_H; y++)
                {
                    double front = (double)(y - HORIZON) / gh;
                    for (int x = 0; x < SCN_W; x++)
                    {
                        if (y < _ground[x]) continue;
                        double a = _snowCover * (0.55 + 0.45 * Math.Max(0, front));
                        BP(x, y, wr, wg, wb, Math.Min(1, a));
                    }
                }
                for (int x = 0; x < SCN_W; x++)
                {
                    int d = (int)_snowPile[x];
                    if (d <= 0) continue;
                    int top = _ground[x] - d;
                    for (int y = top; y <= _ground[x] + 1; y++)
                    {
                        double f = (double)(y - top) / Math.Max(1, d + 1);
                        BP(x, y, 250, 252, 255, Math.Min(1, 0.5 + 0.5 * f));
                    }
                }
                for (int x = 0; x < SCN_W; x++)
                    if (Hash2(x, _frame / 40) < 0.01)
                    {
                        int y = _ground[x] + 1 + (int)(Hash2(x + 2, 5) * gh);
                        BP(x, y, 255, 255, 255, 0.6 * _snowCover);
                    }
            }
        }

        private void BPx(double fx, int y, byte r, byte g, byte b, double a)
        {
            int xi = (int)Math.Floor(fx);
            double frac = fx - xi;
            BP(xi, y, r, g, b, a * (1 - frac));
            BP(xi + 1, y, r, g, b, a * frac);
        }

        private static double Hash2(int x, int y)
        {
            int h = x * 374761393 + y * 668265263;
            h = (h ^ (h >> 13)) * 1274126177;
            return ((h ^ (h >> 16)) & 0x7fff) / 32767.0;
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

        private void DrawCloud(int cx, int cy, int w, double bright, bool night, double storm = 0)
        {
            byte r = (byte)((night ? 60 : 210) * (night ? 1 : bright));
            byte g = (byte)((night ? 62 : 214) * (night ? 1 : bright));
            byte b = (byte)((night ? 86 : 228) * (night ? 1 : bright));
            if (storm > 0)
            {
                r = (byte)(r + (58 - r) * storm);
                g = (byte)(g + (60 - g) * storm);
                b = (byte)(b + (72 - b) * storm);
            }
            double alpha = 0.5 + 0.35 * storm;
            int h = w / 3;
            for (int x = 0; x < w; x++)
            {
                double edge = Math.Sin((double)x / w * Math.PI);
                int hh = (int)(h * edge);
                for (int y = -hh; y <= hh / 2; y++) BP(cx + x - w / 2, cy + y, r, g, b, alpha);
            }
        }

        private void DrawOvercast(double storm, bool night, double bright)
        {
            double drift = _frame * 0.35;
            byte r = (byte)((night ? 34 : 64) * (night ? 1 : bright));
            byte g = (byte)((night ? 36 : 68) * (night ? 1 : bright));
            byte b = (byte)((night ? 50 : 82) * (night ? 1 : bright));
            for (int x = 0; x < SCN_W; x++)
            {
                double edge = 13 + 6 * Math.Sin(x * 0.07 + drift * 0.02) + 3 * Math.Sin(x * 0.19 + drift * 0.05) + 2 * Math.Sin(x * 0.5 + 1);
                int e = (int)edge;
                for (int y = 0; y <= e; y++)
                {
                    double a = storm * (0.92 - 0.55 * (double)y / Math.Max(1, e));
                    BP(x, y, r, g, b, Math.Min(0.92, a));
                }
                BP(x, e + 1, r, g, b, storm * 0.4);
                BP(x, e + 2, r, g, b, storm * 0.18);
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

                bool battlecraftAvailable = _onlineBattleCraftModVer != "0.0" && Version.TryParse(_onlineBattleCraftModVer, out _);
                if (battlecraftAvailable && (didInstall || _needsBattleCraftModUpdate || !BattleCraftModInstalled()))
                {
                    await InstallBattleCraftMod();
                    _needsBattleCraftModUpdate = false;
                    didInstall = true;
                }

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

        private bool BattleCraftModInstalled()
        {
            try
            {
                string modsDir = Path.Combine(_settings.GamePath, "mods");
                return Directory.Exists(modsDir) && Directory.GetFiles(modsDir, "battlecraft*.jar").Length > 0;
            }
            catch { return false; }
        }

        private async Task InstallBattleCraftMod()
        {
            string ver = _onlineBattleCraftModVer;
            if (ver == "0.0" || !Version.TryParse(ver, out _)) return;

            string modsDir = Path.Combine(_settings.GamePath, "mods");
            Directory.CreateDirectory(modsDir);

            try
            {
                foreach (var f in Directory.GetFiles(modsDir, "battlecraft*.jar"))
                    try { File.Delete(f); } catch { }
            }
            catch { }

            string url = BattleCraftJarUrl(ver);
            string dest = Path.Combine(modsDir, Path.GetFileName(new Uri(url).AbsolutePath));

            bool success = false;
            while (!success)
            {
                try
                {
                    var dl = new FileDownloader();
                    dl.LogMessage += LogNet;
                    dl.ProgressChanged += v => Dispatcher.BeginInvoke(() => { GameProgressBar.IsIndeterminate = false; SetProgress(v); });
                    await dl.DownloadFileAsync(url, dest);
                    _settings.BattleCraftModVersion = ver;
                    AppSettings.Save(_settings);
                    success = true;
                }
                catch (Exception ex)
                {
                    try { if (File.Exists(dest)) File.Delete(dest); } catch { }
                    bool retry = await ShowCustomDialog(
                        $"Загрузка клиента оборвалась.\nОшибка: {ex.Message}\nПродолжить скачивание?",
                        "Ошибка скачивания", true);
                    if (!retry) throw new OperationCanceledException("Установка отменена пользователем.");
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
                    $"утречко, {username}", $"о, проснулся, {username}", $"ну чё, выспался нахуй, {username}?",
                    $"бля, рано ты, {username}", $"доброе, {username}, как спалось", $"пиздец ты рано, {username}",
                    $"проснулся и сразу сюда, найс, {username}", $"с утреца кайф, {username}", $"просыпайся, соня, {username}",
                    $"ну ты и ранняя пташка, {username}", $"кофе врубил, {username}?", $"доброе утро, легенда, {username}",
                    $"ща бы кофе, да, {username}?", $"ты вообще спал, {username}?", $"норм выспался, {username}?",
                    $"ну здарова, {username}", $"утро, {username}, погнали", $"бодрого утра нахуй, {username}",
                    $"блять как ты рано, {username}", $"доброе бля, {username}", $"с добрым утром, {username}",
                    $"утро доброе, {username}, ты легенда"
                };
                return p[rnd.Next(p.Length)];
            }
            if (hour >= 12 && hour < 16)
            {
                string[] p = {
                    $"даров, {username}", $"ну чё как, {username}?", $"здарова, легенда, {username}",
                    $"чё нового, {username}?", $"как день, {username}?", $"найс что зашёл, {username}",
                    $"бля, давно тебя не было, {username}", $"ну ты и пропал, {username}", $"кайфового дня, {username}",
                    $"чё там по делам, {username}?", $"день норм идёт, {username}?", $"о, явился, {username}",
                    $"ну здарова нахуй, {username}", $"как сам, {username}?", $"ты сегодня бодрый, {username}?",
                    $"пиздец рад тебя видеть, {username}", $"ну чё, {username}, погнали", $"продуктивного дня, {username}",
                    $"красавчик что зашёл, {username}", $"чё по настрою, {username}?", $"день топ, да, {username}?",
                    $"здарова, братан, {username}"
                };
                return p[rnd.Next(p.Length)];
            }
            if (hour >= 16 && hour < 21)
            {
                string[] p = {
                    $"вечерочек, {username}", $"ну чё, как день прошёл, {username}?", $"здарова, {username}, вечер кайф",
                    $"наконец вечер, {username}", $"бля, устал поди, {username}?", $"вечерний кайф, {username}",
                    $"ну ты и засиделся, {username}", $"тёплого вечера, {username}", $"чё, отдыхаешь, {username}?",
                    $"вечер норм, {username}?", $"о, вечерний гость, {username}", $"пиздец как день пролетел, {username}",
                    $"вечер наш, {username}", $"найс вечер, легенда, {username}", $"как сам под вечер, {username}?",
                    $"ну здарова, {username}, расслабься", $"вечером всегда лучше, {username}", $"чё нового за день, {username}?",
                    $"ща бы чилл, да, {username}?", $"вечерочек пиздец уютный, {username}", $"ну чё там, {username}?",
                    $"устал нахуй, {username}?"
                };
                return p[rnd.Next(p.Length)];
            }

            string[] n = {
                $"ну ты и полуночник, {username}", $"не спишь, {username}?", $"бля, ночь на дворе, {username}",
                $"ночной вайб, {username}", $"спать? да рано ещё, {username}", $"луна светит, {username}",
                $"ночь наша, {username}", $"ну ещё чуть и спать, {username}?", $"пиздец ты поздно, {username}",
                $"тихой ночи, {username}", $"ты вообще ложиться думаешь, {username}?", $"сон для слабых, да, {username}?",
                $"глубокая ночь, а мы тут, {username}", $"ночь кайф, {username}", $"ночная смена, {username}",
                $"не засиживайся сильно, {username}", $"сладких снов потом, {username}", $"опять не спишь, блять, {username}",
                $"тёмная ночь, {username}", $"ночь пиздец длинная, {username}", $"ну здарова, сова, {username}",
                $"ложись давай, {username}", $"ночной гость, найс, {username}"
            };
            return n[rnd.Next(n.Length)];
        }

        private string GetRandomQuestion()
        {
            int hour = DateTime.Now.Hour;
            var rnd = _rnd;
            var q = new System.Collections.Generic.List<string> {
                "чё как", "чё нового", "ну чё, как сам?", "всё ровно?",
                "ты как вообще?", "ну чё, погнали?", "жми, чё ждёшь",
                "ждём только тебя", "грузим моды...", "синхрон...",
                "настрой норм?", "ну чё там по планам?", "как настроение?",
                "бля, давно тебя не было", "ну рассказывай", "всё хорошо?"
            };

            if (hour >= 16 || hour < 4)
            {
                q.Add("как день прошёл?");
                q.Add("устал поди?");
                q.Add("тяжёлый день?");
            }
            if (hour >= 4 && hour < 12)
            {
                q.Add("как спалось?");
                q.Add("выспался?");
                q.Add("готов к дню?");
            }
            return q[rnd.Next(q.Count)];
        }

        private string GetWeatherPhrase()
        {
            var rnd = _rnd;
            string[] p = _weather switch
            {
                Weather.Rain => new[] {
                    "дождина ебашит, а ты тут", "слышь, ливень за окном", "под дождь заходить — святое",
                    "погнали, пока гроза не накрыла", "мокро снаружи, тепло у нас", "дождь стучит, моды грузятся",
                    "гром гремит, а нам похуй", "капает, бля, романтика", "под такой дождь только катать",
                    "молнии сверкают, погнали"
                },
                Weather.Snow => new[] {
                    "снежок пошёл, красота", "зима ебанула, заходи греться", "снег валит, а мы в деле",
                    "под снежок катать — кайф", "снежинки летят, моды летят", "холодрыга снаружи, уютно тут",
                    "первый снег, погнали", "снегопад нахуй, красота же"
                },
                Weather.Wind => new[] {
                    "ветрище поднялся, держись", "ветер воет, а нам норм", "сдует нахуй, заходи быстрей",
                    "ветродуй сегодня, бодрит", "ветер гонит тучи, погнали"
                },
                Weather.Fog => new[] {
                    "туман лёг, мистика", "нихуя не видно снаружи, туман", "туманчик навалил, атмосферно",
                    "в тумане как в сказке, заходи"
                },
                Weather.Comets => new[] {
                    "звездопад, бля, загадывай", "кометы летят, лови момент", "ночное небо горит, красота",
                    "падающие звёзды, успей загадать"
                },
                Weather.Sakura => new[] {
                    "сакура цветёт, эстетика", "лепестки летят, весна пришла", "сакура опадает, лови вайб"
                },
                Weather.Leaves => new[] {
                    "листва кружит, осень", "листья падают, уютная пора", "осенний вайб, заходи"
                },
                _ => new[] { "погодка ясная, грех не катнуть", "небо чистое, погнали" }
            };
            return p[rnd.Next(p.Length)];
        }

        private async void StartWelcomeTextLoop()
        {
            if (_welcomeLoopStarted) return;
            _welcomeLoopStarted = true;

            string chars = "$?#!*%@^&~";
            var rnd = _rnd;
            int slot = 0;

            while (true)
            {
                bool weatherSlot = slot == 2 && _weather != Weather.Clear && _wxIntensity > 0.5;
                string phrase = slot == 0 ? GetRandomGreeting(_settings.Username)
                              : weatherSlot ? GetWeatherPhrase()
                              : GetRandomQuestion();
                slot = (slot + 1) % 3;

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

                try
                {
                    string bcVerStr = (await _httpClient.GetStringAsync(BATTLECRAFT_MOD_VER_URL + ts)).Trim();
                    if (Version.TryParse(bcVerStr, out var onBC))
                    {
                        _onlineBattleCraftModVer = bcVerStr;
                        if (Version.TryParse(_settings.BattleCraftModVersion, out var loBC))
                            _needsBattleCraftModUpdate = _settings.IsModpackInstalled && onBC > loBC;
                        if (Version.TryParse(_settings.ServerBattleCraftModVersion, out var loSBC) && onBC > loSBC)
                            _needsServerModpackUpdate = true;
                        if (_settings.IsModpackInstalled && _needsBattleCraftModUpdate && !_needsModpackUpdate)
                        {
                            BtnPlay.Content = "ОБНОВИТЬ";
                            BtnPlay.Background = new SolidColorBrush(Color.FromRgb(220, 150, 30));
                        }
                    }
                }
                catch { }

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
                    if (currentStage == 3)
                    {
                        if (_onlineBattleCraftModVer != "0.0" && Version.TryParse(_onlineBattleCraftModVer, out _))
                            await installer.InstallBattleCraftMod(serverDir, BattleCraftJarUrl(_onlineBattleCraftModVer), OnServerProgress);
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
            _settings.ServerBattleCraftModVersion = _onlineBattleCraftModVer;
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
                string serverModsDir = Path.Combine(_activeServerConfig.ServerPath, "server");
                await installer.UpdateServerMods(serverModsDir, OnServerProgress);
                if (_onlineBattleCraftModVer != "0.0" && Version.TryParse(_onlineBattleCraftModVer, out _))
                    await installer.InstallBattleCraftMod(serverModsDir, BattleCraftJarUrl(_onlineBattleCraftModVer), OnServerProgress);
                _settings.ServerModpackVersion = _onlineServerModpackVer;
                _settings.ServerBattleCraftModVersion = _onlineBattleCraftModVer;
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
