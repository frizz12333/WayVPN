using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Net;
using Android.OS;
using WayVPN.VPN;
using Android.Systems;

namespace WayVPN.Android;

[Service(
    Name = "com.wayvpn.WayVpnService",
    Permission = "android.permission.BIND_VPN_SERVICE",
    ForegroundServiceType = global::Android.Content.PM.ForegroundService.TypeSpecialUse)]
[IntentFilter(new[] { "android.net.VpnService" })]
public class WayVpnService : VpnService
{
    [System.Runtime.InteropServices.DllImport("libc")]
    private static extern int close(int fd);
    
    [System.Runtime.InteropServices.DllImport("tun_launcher")]
    private static extern int launch_tun2socks(int fd, string bin, string proxy);

    [System.Runtime.InteropServices.DllImport("tun_launcher")]
    private static extern void kill_tun2socks(int pid);

    private int _tun2socksPid = -1;
    private int _tunRawFd = -1; // добавьте поле
    
    public const string ActionStart = "ACTION_START";
    public const string ActionStop  = "ACTION_STOP";

    private const string TunAddress = "198.18.0.1";
    private const int    TunPrefix  = 16;
    private const int    TunMtu     = 1500;

    private ParcelFileDescriptor? _tunFd;
    private CancellationTokenSource _cts = new();

    public override StartCommandResult OnStartCommand(
        Intent? intent, StartCommandFlags flags, int startId)
    {
        if (intent?.Action == ActionStop)
        {
            StopVpn();
            StopSelf();
            return StartCommandResult.NotSticky;
        }

        _ = StartVpnAsync();
        return StartCommandResult.Sticky;
    }

    private async Task StartVpnAsync()
    {
        try
        {
            Vpn.SetBinDir(ApplicationInfo!.NativeLibraryDir!);
            string dataDir = GetDir("vpn", FileCreationMode.Private)!.AbsolutePath;
            Vpn.SetDataDir(dataDir);

            // Копируем geo файлы из assets в dataDir рядом с конфигом
            CopyAssetToDir("xray/geoip.dat",   dataDir);
            CopyAssetToDir("xray/geosite.dat", dataDir);
            
            ShowNotification("Подключение...");

            _tunFd = CreateTunInterface();
            if (_tunFd == null)
                throw new Exception("Не удалось создать TUN-интерфейс");

            StartTun2Socks(_tunFd);

            _cts = new CancellationTokenSource();
            ShowNotification("Подключён");
            VpnStarter.NotifyStarted();
            Console.WriteLine("[VpnService] ✓ VPN запущен");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[VpnService] Ошибка: {ex.Message}");
            VpnStarter.NotifyFailed();
            StopVpn();
            StopSelf();
        }
    }

    private void CopyAssetToDir(string assetPath, string destDir)
    {
        string destFile = Path.Combine(destDir, Path.GetFileName(assetPath));
        if (File.Exists(destFile)) return; // уже скопирован
        try
        {
            using var input  = Assets!.Open(assetPath);
            using var output = File.Create(destFile);
            input.CopyTo(output);
            Console.WriteLine($"[Assets] Скопирован {assetPath} → {destFile}");
        }
        catch (Exception e)
        {
            Console.WriteLine($"[Assets] Ошибка копирования {assetPath}: {e.Message}");
        }
    }
    
    private ParcelFileDescriptor? CreateTunInterface()
    {
        var builder = new Builder(this)
            .SetSession("WayVPN")
            .SetMtu(TunMtu)
            .AddAddress(TunAddress, TunPrefix)
            .AddDnsServer("8.8.8.8")
            .AddDnsServer("1.1.1.1")
            .AddRoute("0.0.0.0", 0)
            .AddRoute("::", 0);

        try { builder.AddDisallowedApplication(PackageName!); }
        catch (Exception e)
        { Console.WriteLine($"[VpnService] AddDisallowedApplication: {e.Message}"); }

        return builder.Establish();
    }

    private void StartTun2Socks(ParcelFileDescriptor tunPfd)
    {
        string bin = Vpn.GetTun2SocksPath();
        if (!File.Exists(bin))
            throw new FileNotFoundException($"tun2socks не найден: {bin}");

        int fd = tunPfd.DetachFd();
        _tunRawFd = fd; // сохраняем для закрытия при Stop
        Console.WriteLine($"[tun2socks] detached fd={fd}");

        string proxy = $"socks5://127.0.0.1:{Vpn.Socks5Port}";
        _tun2socksPid = launch_tun2socks(fd, bin, proxy);

        if (_tun2socksPid < 0)
            throw new Exception("launch_tun2socks failed");

        Console.WriteLine($"[tun2socks] запущен через нативный fork, pid={_tun2socksPid}");
    }
    private void StopVpn()
    {
        _cts.Cancel();

        // Сначала убиваем tun2socks
        try
        {
            kill_tun2socks(_tun2socksPid);
            _tun2socksPid = -1;
            Console.WriteLine("[tun2socks] Остановлен");
        }
        catch (Exception e) { Console.WriteLine($"[tun2socks] Kill error: {e.Message}"); }

        // Закрываем TUN fd — Android деактивирует туннель
        if (_tunRawFd >= 0)
        {
            close(_tunRawFd);
            _tunRawFd = -1;
            Console.WriteLine("[VpnService] TUN fd закрыт");
        }

        StopForeground(StopForegroundFlags.Remove);
        Console.WriteLine("[VpnService] Остановлен");
    }

    private void ShowNotification(string status)
    {
        const string channelId = "wayvpn";
        var manager = (NotificationManager?)GetSystemService(NotificationService);

        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
        {
            var ch = new NotificationChannel(channelId, "WayVPN", NotificationImportance.Low);
            manager?.CreateNotificationChannel(ch);
        }

        var openIntent = PackageManager?.GetLaunchIntentForPackage(PackageName!)
            ?.SetFlags(ActivityFlags.SingleTop);
        var pendingOpen = PendingIntent.GetActivity(
            this, 0, openIntent, PendingIntentFlags.Immutable);

        var stopIntent = new Intent(this, typeof(WayVpnService));
        stopIntent.SetAction(ActionStop);
        var pendingStop = PendingIntent.GetService(
            this, 1, stopIntent, PendingIntentFlags.Immutable);

        var notification = new Notification.Builder(this, channelId)
            .SetContentTitle("WayVPN")
            .SetContentText(status)
            .SetSmallIcon(global::Android.Resource.Drawable.IcDialogInfo)
            .SetContentIntent(pendingOpen)
            .AddAction(new Notification.Action.Builder(
                global::Android.Resource.Drawable.IcMenuCloseClearCancel,
                "Отключить", pendingStop).Build())
            .SetOngoing(true)
            .Build();

        StartForeground(1, notification);
    }

    public override void OnRevoke()
    {
        StopVpn();
        base.OnRevoke();
    }

    public override IBinder? OnBind(Intent? intent) => null;

    private static int GetRawFd(Java.IO.FileDescriptor fd)
    {
        var field = fd.Class.GetDeclaredField("descriptor");
        field!.Accessible = true;
        return ((Java.Lang.Integer)field.Get(fd)!).IntValue();
    }
}