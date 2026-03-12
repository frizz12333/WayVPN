using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace WayVPN.VPN;

/// <summary>
/// Desktop-реализация: запускает xray + tun2socks + настраивает TUN интерфейс.
/// Linux: требует sudo или CAP_NET_ADMIN
/// Windows: требует права администратора + tun2socks с Wintun
/// </summary>
public class DesktopVpnPlatform : IVpnPlatform
{
    private const string TunName    = "wayvpn0";
    private const string TunAddress = "198.18.0.1";
    private const int    TunPrefix  = 16;
    private const int    TunMtu     = 1500;

    private readonly Vpn _vpn;
    private Process?     _tun2socksProcess;

    public DesktopVpnPlatform(Vpn vpn)
    {
        _vpn = vpn;
    }

    public async Task StartAsync(Action onStarted, Action onFailed)
    {
        try
        {
            // 1. Запускаем xray (SOCKS5 прокси)
            bool ok = await _vpn.StartConnection();
            if (!ok) { onFailed(); return; }

            // 2. Создаём TUN и запускаем tun2socks
            if (OperatingSystem.IsLinux())
                await SetupLinuxTunAsync();
            else if (OperatingSystem.IsWindows())
                await SetupWindowsTunAsync();
            else
                throw new PlatformNotSupportedException("Поддерживается только Linux и Windows");

            onStarted();
        }
        catch (Exception e)
        {
            Console.WriteLine($"[Desktop] Ошибка запуска: {e.Message}");
            Stop();
            onFailed();
        }
    }

    public void Stop()
    {
        try
        {
            if (_tun2socksProcess is { HasExited: false })
            {
                _tun2socksProcess.Kill();
                _tun2socksProcess.WaitForExit(2000);
            }
        }
        catch { /* ignore */ }

        if (OperatingSystem.IsLinux())
        {
            string ipBin = File.Exists("/sbin/ip") ? "/sbin/ip" : "/usr/sbin/ip";
            // Удаляем все маршруты через wayvpn0
            RunCommand(ipBin, $"route del 0.0.0.0/0 dev {TunName}");
            RunCommand(ipBin, $"route del 195.46.165.194/32");
            // Удаляем интерфейс
            RunCommand(ipBin, $"link delete {TunName}");
        }
        else if (OperatingSystem.IsWindows())
        {
            RunCommand("route", "delete 0.0.0.0 mask 0.0.0.0");
            RunCommand("route", $"delete 195.46.165.194");
        }

        _vpn.StopConnection();
        Console.WriteLine("[Desktop] VPN остановлен");
    }
    // ──────────────────────────── LINUX ────────────────────────────

    private async Task SetupLinuxTunAsync()
    {
        Console.WriteLine("[Desktop] Настройка TUN (Linux)...");

        // Создаём TUN интерфейс
        RunCommand("ip", $"tuntap add dev {TunName} mode tun");
        RunCommand("ip", $"addr add {TunAddress}/{TunPrefix} dev {TunName}");
        RunCommand("ip", $"link set dev {TunName} mtu {TunMtu} up");

        // Сохраняем текущий gateway чтобы трафик к серверу xray не зациклился
        string gateway  = GetLinuxDefaultGateway();
        string serverIp = "195.46.165.194"; // IP сервера xray
        Console.WriteLine($"[Desktop] Gateway: {gateway}, ServerIP: {serverIp}");

        // Маршрут к серверу xray через реальный интерфейс (не через TUN)
        if (!string.IsNullOrEmpty(gateway))
            RunCommand("ip", $"route add {serverIp}/32 via {gateway}");

        // Весь остальной трафик через TUN
        RunCommand("ip", $"route add 0.0.0.0/0 dev {TunName} metric 1");

        // Запускаем tun2socks
        StartTun2SocksDesktop(TunName);

        await Task.Delay(500);
        Console.WriteLine("[Desktop] TUN (Linux) готов");
    }

    private static string GetLinuxDefaultGateway()
    {
        try
        {
            string ipBin = File.Exists("/sbin/ip") ? "/sbin/ip" : "/usr/sbin/ip";
            var psi = new ProcessStartInfo(ipBin, "route show default")
            {
                RedirectStandardOutput = true,
                UseShellExecute        = false
            };
            using var p = Process.Start(psi)!;
            string output = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            // Формат: "default via 192.168.1.1 dev eth0 ..."
            var parts = output.Split(' ');
            for (int i = 0; i < parts.Length - 1; i++)
                if (parts[i] == "via") return parts[i + 1].Trim();
        }
        catch (Exception e) { Console.WriteLine($"[Desktop] Gateway error: {e.Message}"); }
        return string.Empty;
    }

    // ──────────────────────────── WINDOWS ────────────────────────────

    private async Task SetupWindowsTunAsync()
    {
        Console.WriteLine("[Desktop] Настройка TUN (Windows)...");

        // tun2socks на Windows с Wintun сам создаёт интерфейс через tun://name
        StartTun2SocksDesktop($"tun://{TunName}");

        await Task.Delay(2000); // ждём пока tun2socks создаст интерфейс

        // Настраиваем IP адрес интерфейса
        RunCommand("netsh", $"interface ip set address name=\"{TunName}\" static {TunAddress} 255.255.0.0");
        RunCommand("netsh", $"interface ip set dns name=\"{TunName}\" static 8.8.8.8");

        // Маршрут к серверу xray через реальный gateway
        string gateway  = GetWindowsDefaultGateway();
        string serverIp = "195.46.165.194";
        if (!string.IsNullOrEmpty(gateway))
            RunCommand("route", $"add {serverIp} mask 255.255.255.255 {gateway} metric 1");

        // Весь трафик через TUN
        RunCommand("route", $"add 0.0.0.0 mask 0.0.0.0 {TunAddress} metric 1");

        Console.WriteLine("[Desktop] TUN (Windows) готов");
    }

    private static string GetWindowsDefaultGateway()
    {
        try
        {
            var psi = new ProcessStartInfo("route", "print 0.0.0.0")
            {
                RedirectStandardOutput = true,
                UseShellExecute        = false
            };
            using var p = Process.Start(psi)!;
            string output = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            foreach (var line in output.Split('\n'))
            {
                var trimmed = line.TrimStart();
                if (trimmed.StartsWith("0.0.0.0"))
                {
                    var parts = trimmed.Split(new char[]{' '}, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 3) return parts[2].Trim();
                }
            }
        }
        catch (Exception e) { Console.WriteLine($"[Desktop] Gateway error: {e.Message}"); }
        return string.Empty;
    }

    // ──────────────────────────── ОБЩЕЕ ────────────────────────────

    private void StartTun2SocksDesktop(string device)
    {
        string bin = Vpn.GetTun2SocksPath();
        if (!File.Exists(bin))
            throw new FileNotFoundException($"tun2socks не найден: {bin}. Положите бинарник в папку рядом с приложением.");

        string args = $"-device {device} -proxy socks5://127.0.0.1:{Vpn.Socks5Port} -loglevel info";
        Console.WriteLine($"[tun2socks] {bin} {args}");

        _tun2socksProcess = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName               = bin,
                Arguments              = args,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true,
            }
        };
        _tun2socksProcess.OutputDataReceived += (_, e) =>
        { if (e.Data != null) Console.WriteLine($"[tun2socks] {e.Data}"); };
        _tun2socksProcess.ErrorDataReceived += (_, e) =>
        { if (e.Data != null) Console.WriteLine($"[tun2socks:err] {e.Data}"); };

        _tun2socksProcess.Start();
        _tun2socksProcess.BeginOutputReadLine();
        _tun2socksProcess.BeginErrorReadLine();

        Console.WriteLine($"[tun2socks] запущен pid={_tun2socksProcess.Id}");
    }

    private static void RunCommand(string cmd, string args)
    {    
        if (OperatingSystem.IsLinux() && !cmd.StartsWith("/"))
        {
            string[] searchPaths = { "/sbin", "/usr/sbin", "/bin", "/usr/bin" };
            foreach (var dir in searchPaths)
            {
                string full = Path.Combine(dir, cmd);
                if (File.Exists(full)) { cmd = full; break; }
            }
        }
        
        Console.WriteLine($"[cmd] {cmd} {args}");
        try
        {
            var psi = new ProcessStartInfo(cmd, args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true,
            };
            using var p = Process.Start(psi)!;
            p.WaitForExit(5000);
            string err = p.StandardError.ReadToEnd();
            if (!string.IsNullOrWhiteSpace(err))
                Console.WriteLine($"[cmd:err] {err.Trim()}");
        }
        catch (Exception e)
        {
            Console.WriteLine($"[cmd:fail] {cmd} {args}: {e.Message}");
        }
    }
}