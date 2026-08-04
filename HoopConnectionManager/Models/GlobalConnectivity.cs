namespace HoopConnectionManager.Models;

public enum GlobalConnectivityState
{
    Online,
    HoopDisconnected,
    NoNetwork,
    AuthenticationExpired,
    GatewayUnavailable
}

public sealed record GlobalConnectivity(GlobalConnectivityState State, string Detail);
