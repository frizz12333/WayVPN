using System;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace WayVPN.Views;

public partial class MainView : UserControl
{
    private readonly DispatcherTimer _timer;
    
    private readonly LinearGradientBrush _brush;
    
    private short _red = 90;
    private short _green = 36;
    private bool _positive = true;   
    
    private short _redSecond = 90;
    private short _greenSecond = 36;
    
    public MainView()
    {
        InitializeComponent();
        
        _brush = (LinearGradientBrush)this.FindResource("GradientBrush");
        
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(50) // 20 FPS
        };
        _timer.Tick += OnTimerTick;
        _timer.Start();
    }
    
    private void OnTimerTick(object? sender, EventArgs e)
    {
        if (_red >= 126 || _green >= 84)
        {
            _positive = false;
        }
        else if (_red <= 63 || _green <= 0)
        {
            _positive = true;
        }

        if (_positive)
        {
            _red += 3;
            _green += 3;

            _redSecond -= 3;
            _greenSecond -= 3;
        }
        else
        {
            _red -= 3;
            _green -= 3;

            _redSecond += 3;
            _greenSecond += 3;
        }
        
        var color = Color.FromRgb((byte)_red, (byte)_green,  255);
        var colorSecond = Color.FromRgb((byte)_redSecond, (byte)_greenSecond,  255);
        
        _brush.GradientStops[1].Color = color;
        _brush.GradientStops[0].Color = colorSecond;
        
    }
}