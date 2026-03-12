using System;
using Android.App;
using Android.Content;
using Android.Net;

namespace WayVPN.Android;

public static class VpnStarter
{
    private const int VpnRequestCode = 1001;

    public static Action? OnVpnStarted { get; set; }
    public static Action? OnVpnDenied  { get; set; }

    public static void Start(Activity activity)
    {
        Intent? prepareIntent = VpnService.Prepare(activity);

        if (prepareIntent != null)
            // Система покажет диалог запроса VPN-разрешения
            activity.StartActivityForResult(prepareIntent, VpnRequestCode);
        else
            LaunchService(activity);
    }

    public static void Stop(Context context)
    {
        var intent = new Intent(context, typeof(WayVpnService));
        intent.SetAction(WayVpnService.ActionStop);
        context.StartService(intent);
        Console.WriteLine("[VpnStarter] Сервис остановлен");
    }

    // Вызывается из WayVpnService когда TUN и tun2socks реально запущены
    public static void NotifyStarted()
    {
        Console.WriteLine("[VpnStarter] NotifyStarted");
        OnVpnStarted?.Invoke();
        OnVpnStarted = null;
    }

    // Вызывается из WayVpnService при ошибке запуска
    public static void NotifyFailed()
    {
        Console.WriteLine("[VpnStarter] NotifyFailed");
        OnVpnDenied?.Invoke();
        OnVpnDenied = null;
    }

    // Вызовите в MainActivity.OnActivityResult
    public static void OnActivityResult(Activity activity, int requestCode, Result resultCode)
    {
        if (requestCode != VpnRequestCode) return;

        if (resultCode == Result.Ok)
            LaunchService(activity);
        else
        {
            Console.WriteLine("[VpnStarter] Пользователь отказал в разрешении VPN");
            OnVpnDenied?.Invoke();
            OnVpnDenied = null;
        }
    }

    private static void LaunchService(Context context)
    {
        var intent = new Intent(context, typeof(WayVpnService));
        intent.SetAction(WayVpnService.ActionStart);
        context.StartForegroundService(intent);
        Console.WriteLine("[VpnStarter] Сервис запущен, ждём NotifyStarted...");
        // НЕ вызываем OnVpnStarted здесь — ждём вызова из WayVpnService.NotifyStarted()
    }
}