using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading.Tasks;

namespace WayVPN.VPN;

public class Vpn
{
    private const string ServerHost  = "";
    private const int    ServerPort  = 1;
    private const string UserId      = "";
    private const string PublicKey   = "";
    private const string ShortId     = "";
    private const string ServerName  = "vk.ru";
    private const string Fingerprint = "chrome";
    public  const int    Socks5Port  = 10808;

    private static string? _binDir;
    private static string? _dataDir;

    // Android устанавливает эти делегаты в AndroidVpnPlatform
    public static Func<string, string, Task<bool>>? ProcessStarter { get; set; }
    public static Action? ProcessStopper { get; set; }

    public static void SetBinDir(string path)
    {
        _binDir = path;
        Console.WriteLine($"[VPN] BinDir установлен: {path}");
    }

    private static string GetBinDir()
    {
        if (!string.IsNullOrEmpty(_binDir))
            return _binDir;
        return Path.Combine(AppContext.BaseDirectory, "xray-bin");
    }

    public static string GetXrayPath()
    {
        if (ProcessStarter != null) // Android
            return Path.Combine(GetBinDir(), "libxray.so");

        // Desktop — определяем подпапку по платформе
        string rid = GetRuntimeId();
        string name = OperatingSystem.IsWindows() ? "xray.exe" : "xray";
        return Path.Combine(AppContext.BaseDirectory, "xray-bin", rid, name);
    }

    public static string GetTun2SocksPath()
    {
        if (ProcessStarter != null) // Android
            return Path.Combine(GetBinDir(), "libtun2socks.so");

        string rid = GetRuntimeId();
        string name = OperatingSystem.IsWindows() ? "tun2socks.exe" : "tun2socks";
        return Path.Combine(AppContext.BaseDirectory, "xray-bin", rid, name);
    }

    private static string GetRuntimeId()
    {
        bool isWindows = OperatingSystem.IsWindows();
        bool isArm     = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture
                         == System.Runtime.InteropServices.Architecture.Arm64;

        if (isWindows) return isArm ? "win-arm64" : "win-x64";
        return isArm ? "linux-arm64" : "linux-x64";
    }

    public static void SetDataDir(string path)
    {
        _dataDir = path;
        Console.WriteLine($"[VPN] DataDir установлен: {path}");
    }

    private static string GetDataDir()
    {
        if (!string.IsNullOrEmpty(_dataDir))
            return _dataDir;
        return GetBinDir(); // Desktop fallback
    }

    private Process? _xrayProcess;
    private string?  _configPath;

    public bool IsConnected => _xrayProcess is { HasExited: false };

    public async Task<bool> StartConnection()
    {
        try
        {
            string xrayPath = GetXrayPath();
            Console.WriteLine($"[VPN] xray путь: {xrayPath}");

            if (!File.Exists(xrayPath))
                throw new FileNotFoundException($"xray не найден: {xrayPath}");

            _configPath = WriteXrayConfig();

            // Android — запуск делегирован в AndroidVpnPlatform
            if (ProcessStarter != null)
                return await ProcessStarter(xrayPath, _configPath);

            // Desktop
            _xrayProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName               = xrayPath,
                    Arguments              = $"run -c \"{_configPath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    UseShellExecute        = false,
                    CreateNoWindow         = true,
                }
            };
            _xrayProcess.OutputDataReceived += (_, e) =>
            { if (e.Data != null) Console.WriteLine($"[xray] {e.Data}"); };
            _xrayProcess.ErrorDataReceived += (_, e) =>
            { if (e.Data != null) Console.WriteLine($"[xray:err] {e.Data}"); };

            _xrayProcess.Start();
            _xrayProcess.BeginOutputReadLine();
            _xrayProcess.BeginErrorReadLine();

            await WaitForPortAsync("127.0.0.1", Socks5Port);
            Console.WriteLine($"[VPN] SOCKS5 готов на 127.0.0.1:{Socks5Port}");
            return true;
        }
        catch (Exception e)
        {
            Console.WriteLine($"[VPN] Ошибка запуска: {e.Message}");
            return false;
        }
    }

    public bool StopConnection()
    {
        try
        {
            ProcessStopper?.Invoke();

            if (_xrayProcess is { HasExited: false })
            {
                _xrayProcess.Kill();
                _xrayProcess.WaitForExit(3000);
            }

            if (_configPath != null && File.Exists(_configPath))
                File.Delete(_configPath);

            Console.WriteLine("[VPN] xray остановлен");
            return true;
        }
        catch (Exception e)
        {
            Console.WriteLine($"[VPN] Ошибка остановки: {e.Message}");
            return false;
        }
    }

    private static string WriteXrayConfig()
    {
        var config = new
        {
            log = new { loglevel = "warning" },
            inbounds = new[]
            {
                new
                {
                    tag      = "socks-in",
                    listen   = "127.0.0.1",
                    port     = Socks5Port,
                    protocol = "socks",
                    settings = new { auth = "noauth", udp = true }
                }
            },
            outbounds = new[]
            {
                new
                {
                    tag      = "vless-out",
                    protocol = "vless",
                    settings = new
                    {
                        vnext = new[]
                        {
                            new
                            {
                                address = ServerHost,
                                port    = ServerPort,
                                users   = new[]
                                {
                                    new { id = UserId, encryption = "none", flow = "xtls-rprx-vision" }
                                }
                            }
                        }
                    },
                    streamSettings = new
                    {
                        network         = "tcp",
                        security        = "reality",
                        realitySettings = new
                        {
                            serverName  = ServerName,
                            fingerprint = Fingerprint,
                            publicKey   = PublicKey,
                            shortId     = ShortId
                        }
                    }
                }
            }
        };

        string path = Path.Combine(GetDataDir(), "xray_config.json");
        File.WriteAllText(path, JsonSerializer.Serialize(config,
            new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"[VPN] Конфиг: {path}");
        return path;
    }

    public static async Task WaitForPortAsync(string host, int port, int timeoutMs = 8000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var tcp = new TcpClient();
                await tcp.ConnectAsync(host, port);
                return;
            }
            catch { await Task.Delay(200); }
        }
        throw new TimeoutException($"Порт {host}:{port} не открылся за {timeoutMs}мс");
    }
}
