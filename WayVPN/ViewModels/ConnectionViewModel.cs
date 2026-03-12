using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using WayVPN.VPN;

namespace WayVPN.ViewModels;

public partial class ConnectionViewModel : ViewModelBase, INotifyPropertyChanged
{
    private readonly VPN.WayVPN  _wayVpn;
    private readonly IVpnPlatform _platform;

    public ConnectionViewModel(VPN.WayVPN wayVpn, IVpnPlatform platform)
    {
        _wayVpn   = wayVpn;
        _platform = platform;
    }

    private readonly Dictionary<byte, string> _colors = new()
    {
        { 0, "#FF4538" },
        { 1, "#3DFF51" }
    };

    private string _connectionButtonColor = "#FF4538";
    public string ConnectionButtonColor
    {
        get => _connectionButtonColor;
        set { _connectionButtonColor = value; OnPropertyChanged(); }
    }

    private string _connectionButtonText = "НЕ ПОДКЛЮЧЕН";
    public string ConnectionButtonText
    {
        get => _connectionButtonText;
        set { _connectionButtonText = value; OnPropertyChanged(); }
    }

    [RelayCommand]
    public async void StartConnection()
    {
        if (_wayVpn.Status == StatusVPN.Disconnected)
        {
            _colors.TryGetValue(1, out var color);
            ConnectionButtonText  = "ПОДКЛЮЧЕНИЕ...";
            ConnectionButtonColor = color;
            _wayVpn.Status        = StatusVPN.Connecting;

            await _platform.StartAsync(
                onStarted: () =>
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        _wayVpn.Status        = StatusVPN.Connected;
                        ConnectionButtonText  = "ПОДКЛЮЧЕН";
                        _colors.TryGetValue(1, out var c);
                        ConnectionButtonColor = c;
                    });
                },
                onFailed: () =>
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        _colors.TryGetValue(0, out var c);
                        _wayVpn.Status        = StatusVPN.Error;
                        ConnectionButtonText  = "ОШИБКА!";
                        ConnectionButtonColor = c;
                    });
                });
        }
        else if (_wayVpn.Status == StatusVPN.Connected)
        {
            _colors.TryGetValue(0, out var color);
            ConnectionButtonText  = "ОТКЛЮЧЕНИЕ...";
            ConnectionButtonColor = color;

            await Task.Run(() => _platform.Stop());

            _wayVpn.Status        = StatusVPN.Disconnected;
            ConnectionButtonText  = "НЕ ПОДКЛЮЧЕН";
            ConnectionButtonColor = color;
        }
        else if (_wayVpn.Status == StatusVPN.Error)
        {
            // Сбрасываем ошибку чтобы можно было попробовать снова
            _wayVpn.Status        = StatusVPN.Disconnected;
            ConnectionButtonText  = "НЕ ПОДКЛЮЧЕН";
            _colors.TryGetValue(0, out var color);
            ConnectionButtonColor = color;
        }
        else if (_wayVpn.Status is StatusVPN.Disconnecting or StatusVPN.Connecting)
        {
            // В процессе — игнорируем
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}