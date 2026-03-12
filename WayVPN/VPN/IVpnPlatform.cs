using System;
using System.Threading.Tasks;

namespace WayVPN.VPN;

/// <summary>
/// Платформо-зависимые операции VPN.
/// Android: реализует WayVPN.Android.AndroidVpnPlatform
/// Desktop: реализует WayVPN.VPN.DesktopVpnPlatform
/// </summary>
public interface IVpnPlatform
{
    /// <summary>Установить бинарники и запустить VPN. Вызывает onStarted или onFailed по завершении.</summary>
    Task StartAsync(Action onStarted, Action onFailed);

    /// <summary>Остановить VPN.</summary>
    void Stop();
    
}