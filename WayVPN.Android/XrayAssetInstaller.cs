using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Android.Content;
using Android.Content.PM;
using WayVPN.VPN;

namespace WayVPN.Android;

public static class XrayAssetInstaller
{
    public static async Task EnsureInstalledAsync(Context context)
    {
        if (IsUpToDate(context))
        {
            Console.WriteLine("[Installer] Бинарники актуальны");
            return;
        }

        var (xrayAbi, tun2socksAbi) = DetectAbi();
        Console.WriteLine($"[Installer] ABI: xray={xrayAbi}, tun2socks={tun2socksAbi}");

        await CopyBinaryAsync(context, $"bin/xray-{xrayAbi}/xray",      Vpn.GetXrayPath());
        await CopyBinaryAsync(context, $"bin/tun2socks-{tun2socksAbi}", Vpn.GetTun2SocksPath());


        SaveInstalledVersion(context);
    }

    private static async Task CopyBinaryAsync(Context context, string assetName, string destPath)
    {
        Console.WriteLine($"[Installer] {assetName} → {destPath}");

        var assets = context.Assets
            ?? throw new Exception("Assets недоступны");

        await using var input  = assets.Open(assetName)
            ?? throw new FileNotFoundException($"Asset не найден: {assetName}");
        await using var output = File.Create(destPath);
        await input.CopyToAsync(output);
        output.Close();


        Console.WriteLine($"[Installer] ✓ {Path.GetFileName(destPath)}");
    }

    // xray:      github.com/XTLS/Xray-core       → Xray-android-{abi}.zip
    // tun2socks: github.com/xjasonlyu/tun2socks  → tun2socks-linux-{arch}.zip
    private static (string xrayAbi, string tun2socksAbi) DetectAbi()
    {
        string[] abis = global::Android.OS.Build.SupportedAbis?.ToArray() ?? Array.Empty<string>();
        foreach (string abi in abis)
        {
            switch (abi)
            {
                case "arm64-v8a":   return ("arm64-v8a",   "linux-arm64");
                case "armeabi-v7a": return ("armeabi-v7a", "linux-armv7");
                case "x86_64":      return ("x86_64",      "linux-amd64");
            }
        }
        throw new PlatformNotSupportedException(
            $"Нет бинарника для ABI: {string.Join(", ", abis)}");
    }

    private static bool IsUpToDate(Context context)
    {
        if (!File.Exists(Vpn.GetXrayPath()))      return false;
        if (!File.Exists(Vpn.GetTun2SocksPath())) return false;

        try
        {
            var prefs   = context.GetSharedPreferences("installer", FileCreationMode.Private);
            long saved   = prefs?.GetLong("version", -1) ?? -1;
            long current = GetVersionCode(context);
            return saved == current;
        }
        catch { return false; }
    }

    private static void SaveInstalledVersion(Context context)
    {
        context.GetSharedPreferences("installer", FileCreationMode.Private)?
            .Edit()?.PutLong("version", GetVersionCode(context))?.Apply();
    }

    private static long GetVersionCode(Context context) =>
        context.PackageManager?
            .GetPackageInfo(context.PackageName!, (PackageInfoFlags)0)?
            .LongVersionCode ?? 0;
}