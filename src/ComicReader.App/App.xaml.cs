using System.IO;
using System.Windows;
using System.Windows.Threading;
using ComicReader.App.Services;

namespace ComicReader.App;

public partial class App : Application
{
    private static bool SmokeMode => Environment.GetEnvironmentVariable("COMICREADER_SMOKE") == "1";

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, _) =>
        {
            if (SmokeMode) Environment.Exit(2);
        };

        AppServices.Initialize();

        var window = new MainWindow { DataContext = AppServices.Main };
        MainWindow = window;
        window.Show();

        var args = e.Args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToArray();
        if (args.Length > 0 && (File.Exists(args[0]) || Directory.Exists(args[0])))
        {
            var target = args[0];
            window.Dispatcher.BeginInvoke(new Action(() => AppServices.Main.OpenBook(target)), DispatcherPriority.Background);
        }

        if (SmokeMode)
        {
            window.Dispatcher.BeginInvoke(new Action(async () =>
            {
                await Task.Delay(4000);
                window.Close();
            }), DispatcherPriority.Background);
        }
    }

    private static void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        if (SmokeMode)
        {
            Environment.Exit(2);
            return;
        }

        MessageBox.Show(e.Exception.Message, "出错了", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
