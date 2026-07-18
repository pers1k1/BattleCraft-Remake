using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using CustomLauncher.Core;

namespace CustomLauncher
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                if (args.ExceptionObject is Exception ex) LauncherLog.WriteCrash("Необработанное исключение", ex);
            };
            TaskScheduler.UnobservedTaskException += (s, args) =>
            {
                LauncherLog.WriteCrash("Необработанная ошибка фоновой задачи", args.Exception);
                args.SetObserved();
            };
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            e.Handled = true;
            string report = LauncherLog.WriteCrash("Необработанное исключение", e.Exception);
            string message = Lang.F("Произошла непредвиденная ошибка: {0}", e.Exception.Message);
            if (!string.IsNullOrEmpty(report)) message += "\n\n" + Lang.F("Отчёт сохранён: {0}", report);
            MessageBox.Show(message, "BattleCraft Launcher", MessageBoxButton.OK, MessageBoxImage.Error);
            if (MainWindow is not { IsLoaded: true }) Shutdown(1);
        }
    }
}
