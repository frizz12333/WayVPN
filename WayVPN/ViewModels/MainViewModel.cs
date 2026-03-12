using System.ComponentModel;
using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.Input;
using WayVPN.VPN;

namespace WayVPN.ViewModels;

public partial class MainViewModel : ViewModelBase, INotifyPropertyChanged
{
    private readonly VPN.WayVPN   _wayVpn;
    private readonly IVpnPlatform _platform;
    private object? _currentView;

    private ConnectionViewModel? _cachedConnectionViewModel;
    private ServerViewModel?     _cachedServerViewModel;
    private SettingsViewModel?   _cachedSettingsViewModel;

    private const float ButtonOpacityMin = 0.6f;

    private float _serverButtonOpacity   = ButtonOpacityMin;
    private float _wayButtonOpacity      = 1f;
    private float _settingsButtonOpacity = ButtonOpacityMin;

    public float ServerButtonOpacity
    {
        get => _serverButtonOpacity;
        set { _serverButtonOpacity = value; OnPropertyChanged(); }
    }

    public float WayButtonOpacity
    {
        get => _wayButtonOpacity;
        set { _wayButtonOpacity = value; OnPropertyChanged(); }
    }

    public float SettingsButtonOpacity
    {
        get => _settingsButtonOpacity;
        set { _settingsButtonOpacity = value; OnPropertyChanged(); }
    }

    public object? CurrentView
    {
        get => _currentView;
        set
        {
            if (!Equals(_currentView, value))
            {
                _currentView = value;
                OnPropertyChanged();
            }
        }
    }

    // platform передаётся снаружи — Android или Desktop
    public MainViewModel(IVpnPlatform platform)
    {
        _wayVpn   = new VPN.WayVPN();
        _platform = platform;
        CurrentView = new ConnectionViewModel(_wayVpn, _platform);
    }

    [RelayCommand]
    public void ShowConnection()
    {
        _cachedConnectionViewModel ??= new ConnectionViewModel(_wayVpn, _platform);
        CurrentView = _cachedConnectionViewModel;

        WayButtonOpacity      = 1f;
        ServerButtonOpacity   = ButtonOpacityMin;
        SettingsButtonOpacity = ButtonOpacityMin;
    }

    [RelayCommand]
    public void ShowServer()
    {
        _cachedServerViewModel ??= new ServerViewModel();
        CurrentView = _cachedServerViewModel;

        ServerButtonOpacity   = 1f;
        WayButtonOpacity      = ButtonOpacityMin;
        SettingsButtonOpacity = ButtonOpacityMin;
    }

    [RelayCommand]
    public void ShowSettings()
    {
        _cachedSettingsViewModel ??= new SettingsViewModel();
        CurrentView = _cachedSettingsViewModel;

        SettingsButtonOpacity = 1f;
        ServerButtonOpacity   = ButtonOpacityMin;
        WayButtonOpacity      = ButtonOpacityMin;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}