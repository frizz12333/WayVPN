using System;
using System.IO;
using Avalonia;

namespace WayVPN.Desktop;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // Восстанавливаем маршруты при любом завершении
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            // Экстренная очистка если приложение упало
            try
            {
                string ipBin = File.Exists("/sbin/ip") ? "/sbin/ip" : "/usr/sbin/ip";
                var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                        ipBin, "link delete wayvpn0")
                    { UseShellExecute = false });
                p?.WaitForExit(2000);
            }
            catch { /* ignore */ }
        };

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }
    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
