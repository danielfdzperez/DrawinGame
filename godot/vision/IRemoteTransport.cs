using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace DrawingGame.Vision;

/// <summary>
/// Una forma de conectar el móvil (app por TCP, página web...). Cada transporte recibe imágenes JPEG
/// y las entrega a un <see cref="RemoteFrameReceiver"/>; muestra su propio QR en la pantalla de conexión.
/// </summary>
public interface IRemoteTransport : IDisposable
{
    /// <summary>Título sobre su QR.</summary>
    string Title { get; }

    /// <summary>Texto corto de ayuda bajo el QR.</summary>
    string Hint { get; }

    /// <summary>Error al arrancar (null si no hay).</summary>
    string? Error { get; }

    /// <summary>Puerto en el que escucha (0 si no pudo arrancar).</summary>
    int Port { get; }

    void Start();

    /// <summary>Contenido del QR dadas las direcciones IP de este PC.</summary>
    string BuildUri(IReadOnlyList<string> hosts);

    /// <summary>Texto para quien no pueda escanear el QR (dirección y puerto, o el enlace).</summary>
    string Describe(IReadOnlyList<string> hosts);
}

public static class LocalNetwork
{
    /// <summary>
    /// Direcciones IPv4 del PC. Primero las de adaptadores con puerta de enlace (la red real);
    /// si no hay ninguna (p. ej. el PC hace de punto de acceso), las demás.
    /// </summary>
    public static List<string> Addresses()
    {
        var withGateway = new List<string>();
        var others = new List<string>();

        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up) continue;
            if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback || nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;

            var props = nic.GetIPProperties();
            bool hasGateway = props.GatewayAddresses.Any(g =>
                g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any));

            foreach (var unicast in props.UnicastAddresses)
            {
                if (unicast.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                var b = unicast.Address.GetAddressBytes();
                if (b[0] == 127 || (b[0] == 169 && b[1] == 254)) continue;
                (hasGateway ? withGateway : others).Add(unicast.Address.ToString());
            }
        }
        return withGateway.Count > 0 ? withGateway : others;
    }
}
