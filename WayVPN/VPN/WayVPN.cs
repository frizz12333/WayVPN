namespace WayVPN.VPN;

public class WayVPN
{
    public StatusVPN Status { get; set; } = StatusVPN.Disconnected;
    public Vpn Vpn { get; } = new Vpn();
}