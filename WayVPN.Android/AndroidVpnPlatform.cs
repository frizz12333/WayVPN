using System;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using WayVPN.VPN;
using System.IO;

namespace WayVPN.Android;

/// <summary>
/// Android-реализация IVpnPlatform:
/// копирует бинарники из Assets и запускает WayVpnService.
/// </summary>
public class AndroidVpnPlatform : IVpnPlatform
{
    private readonly Activity _activity;
    private readonly VPN.WayVPN  _wayVpn;
    public AndroidVpnPlatform(Activity activity, VPN.WayVPN wayVpn)
    {
        _activity = activity;
        _wayVpn   = wayVpn;
        Vpn.ProcessStarter = StartXrayAsync;
        Vpn.ProcessStopper = () => { _javaProcess?.Destroy(); _javaProcess = null; };
    }

    private Java.Lang.Process? _javaProcess;

    private async Task<bool> StartXrayAsync(string xrayPath, string configPath)
    {
        try
        {
            new Java.IO.File(xrayPath).SetExecutable(true, false);
        
            var pb = new Java.Lang.ProcessBuilder(xrayPath, "run", "-c", configPath);
            pb.RedirectErrorStream(true);
            _javaProcess = pb.Start()!;

            Task.Run(() =>
            {
                using var reader = new StreamReader(_javaProcess.InputStream!);
                string? line;
                while ((line = reader.ReadLine()) != null)
                    Console.WriteLine($"[xray] {line}");
            });

            await Vpn.WaitForPortAsync("127.0.0.1", Vpn.Socks5Port);
            return true;
        }
        catch (Exception e)
        {
            Console.WriteLine($"[xray:android] Ошибка: {e.Message}");
            return false;
        }
    }
    public async Task StartAsync(Action onStarted, Action onFailed)
    {
        // 1. Запускаем xray (SOCKS5 прокси)
        bool ok = await _wayVpn.Vpn.StartConnection();
        if (!ok)
        {
            onFailed();
            return;
        }

        // 2. Запрашиваем разрешение VPN и запускаем WayVpnService (TUN + tun2socks)
        VpnStarter.OnVpnStarted = () =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(onStarted);
        };
        VpnStarter.OnVpnDenied = () =>
        {
            // Пользователь отказал — останавливаем xray
            _wayVpn.Vpn.StopConnection();
            Avalonia.Threading.Dispatcher.UIThread.Post(onFailed);
        };

        VpnStarter.Start(_activity);
    }

    public void Stop()
    {
        VpnStarter.Stop(_activity);
        _wayVpn.Vpn.StopConnection();
    }
}