using Android.App;
using Android.Content.PM;
using Android.OS;
using Avalonia;
using Avalonia.Android;
using Java.Lang;
using Android.Util;
using Android.Content;
using WayVPN.Android;
using WayVPN.VPN;

namespace WayVPN.Android;

[Activity(
    Label = "WayVPN",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@drawable/icon",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity<App>
{
    // Статический конструктор выполняется при первой загрузке класса – очень рано
    static MainActivity()
    {
        try
        {
            JavaSystem.LoadLibrary("android");
            // Используем System.Console для отладки (лог может появиться в консоли ADB)
            System.Console.WriteLine("libandroid.so loaded successfully in static ctor");
        }
        catch (UnsatisfiedLinkError ex)
        {
            System.Console.WriteLine($"Failed to load libandroid.so in static ctor: {ex}");
        }
    }


    protected override void OnCreate(Bundle? savedInstanceState)
    {
        Vpn.SetBinDir(ApplicationInfo!.NativeLibraryDir!);
        Vpn.SetDataDir(GetDir("vpn", FileCreationMode.Private)!.AbsolutePath);
        
        var wayVpn = new VPN.WayVPN(); // или получите существующий
        App.PlatformOverride = new WayVPN.Android.AndroidVpnPlatform(this, wayVpn);
        
    
        base.OnCreate(savedInstanceState);
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        return base.CustomizeAppBuilder(builder)
            .LogToTrace(Avalonia.Logging.LogEventLevel.Verbose)
            .WithInterFont();
    }
    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        VpnStarter.OnActivityResult(this, requestCode, resultCode);
    }
}