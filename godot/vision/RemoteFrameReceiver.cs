using OpenCvSharp;

namespace DrawingGame.Vision;

/// <summary>
/// Fuente de imagen "móvil": guarda la última imagen que entregan los transportes (<see cref="IRemoteTransport"/>).
/// Sabe decodificar el JPEG y girarlo; no sabe nada de redes.
/// </summary>
public sealed class RemoteFrameReceiver : ICameraSource
{
    /// <summary>Giro extra de la imagen, en cuartos de vuelta horarios (0-3). Se aplica a las imágenes siguientes.</summary>
    public volatile int UserRotation;

    public bool Connected
    {
        get { lock (_lock) return _clients > 0; }
    }

    /// <summary>Dirección del último móvil conectado (o cadena vacía).</summary>
    public string ClientAddress
    {
        get { lock (_lock) return _clients > 0 ? _address : ""; }
    }

    // La conexión de un móvil no es un error de la fuente: los errores de arranque los da cada transporte
    public string? Error => null;

    private readonly object _lock = new();
    private int _clients;
    private string _address = "";
    private Mat? _latest;
    private bool _hasNew;

    /// <summary>Un transporte avisa de que un móvil se ha conectado y autenticado.</summary>
    public void ClientConnected(string address)
    {
        lock (_lock)
        {
            _clients++;
            _address = address;
        }
    }

    /// <summary>Un transporte avisa de que ese móvil ya no está.</summary>
    public void ClientDisconnected()
    {
        lock (_lock) _clients = Math.Max(0, _clients - 1);
    }

    /// <summary>
    /// Entrega una imagen JPEG. 'phoneQuarterTurns' = cuartos de vuelta horarios que indica el móvil para dejarla derecha.
    /// </summary>
    public void Submit(byte[] jpeg, int phoneQuarterTurns)
    {
        Mat image = Cv2.ImDecode(jpeg, ImreadModes.Color);
        if (image.Empty())
        {
            image.Dispose();
            return;
        }

        int turns = (phoneQuarterTurns + UserRotation) & 3;
        if (turns != 0)
        {
            var rotated = new Mat();
            Cv2.Rotate(image, rotated, turns == 1 ? RotateFlags.Rotate90Clockwise
                                     : turns == 2 ? RotateFlags.Rotate180
                                     : RotateFlags.Rotate90Counterclockwise);
            image.Dispose();
            image = rotated;
        }

        lock (_lock)
        {
            _latest?.Dispose();
            _latest = image;
            _hasNew = true;
            Monitor.PulseAll(_lock);
        }
    }

    public bool TryGetFrame(Mat destination, int timeoutMs)
    {
        lock (_lock)
        {
            if (!_hasNew) Monitor.Wait(_lock, timeoutMs);
            if (!_hasNew || _latest == null) return false;
            _latest.CopyTo(destination);
            _hasNew = false;
            return true;
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _latest?.Dispose();
            _latest = null;
            _hasNew = false;
            Monitor.PulseAll(_lock);
        }
    }
}
