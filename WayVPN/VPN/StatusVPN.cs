namespace WayVPN.VPN;

public enum StatusVPN:byte
{
    Connecting=0,
    Connected=1,
    Error=2,
    Disconnected=3,
    Disconnecting=4,
}