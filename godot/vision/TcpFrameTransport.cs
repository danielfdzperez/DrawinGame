using System.Net;
using System.Net.Sockets;
using System.Text;

namespace DrawingGame.Vision;

/// <summary>
/// Transporte para la app Android (MobileApp/Android): servidor TCP propio.
/// Protocolo (ver AIDocs/movil.md):
///  - móvil -> PC al conectar: "DRWG", versión (1), longitud del código (1 byte), código UTF-8
///  - PC -> móvil: 'K' (aceptado) o 'N' (rechazado)
///  - móvil -> PC, por imagen: longitud (4 bytes big-endian) = 1 + tamaño JPEG, giro (1 byte, cuartos de vuelta horarios), JPEG
/// Corre en hilos propios y no toca ningún objeto de Godot.
/// </summary>
public sealed class TcpFrameTransport : IRemoteTransport
{
    public const int DefaultPort = 48750;
    private const int MaxFrameBytes = 16 * 1024 * 1024;

    public string Title => "Con la app (Android)";
    public string Hint => "Abre «DrawinGame Cámara» y pulsa «Escanear QR».";
    public string? Error { get; private set; }
    public int Port { get; private set; }

    private readonly RemoteFrameReceiver _receiver;
    private readonly string _token;
    private readonly int _preferredPort;
    private TcpListener? _listener;
    private Thread? _acceptThread;
    private volatile bool _running;

    private readonly object _lock = new();
    private TcpClient? _client;      // móvil activo (uno solo; uno nuevo sustituye al anterior)

    public TcpFrameTransport(RemoteFrameReceiver receiver, string token, int preferredPort = DefaultPort)
    {
        _receiver = receiver;
        _token = token;
        _preferredPort = preferredPort;
    }

    public string BuildUri(IReadOnlyList<string> hosts) =>
        $"drawingame://connect?h={string.Join(",", hosts)}&p={Port}&t={_token}";

    public string Describe(IReadOnlyList<string> hosts) =>
        $"Dirección: {string.Join(" · ", hosts)}   Puerto: {Port}   Código: {_token}";

    public void Start()
    {
        if (_running) return;
        try
        {
            _listener = new TcpListener(IPAddress.Any, _preferredPort);
            _listener.Start();
        }
        catch (SocketException)
        {
            // Puerto ocupado: se deja que el sistema elija uno libre
            try
            {
                _listener = new TcpListener(IPAddress.Any, 0);
                _listener.Start();
            }
            catch (Exception ex)
            {
                Error = "No se pudo abrir el servidor para el móvil: " + ex.Message;
                return;
            }
        }

        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _running = true;
        _acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "TcpFrameAccept" };
        _acceptThread.Start();
    }

    public void Dispose()
    {
        _running = false;
        try { _listener?.Stop(); } catch { /* ya cerrado */ }
        lock (_lock)
        {
            try { _client?.Close(); } catch { /* ya cerrado */ }
            _client = null;
        }
        _acceptThread?.Join(1000);
    }

    private void AcceptLoop()
    {
        while (_running)
        {
            TcpClient client;
            try
            {
                client = _listener!.AcceptTcpClient();
            }
            catch
            {
                break;   // servidor cerrado
            }
            new Thread(() => Serve(client)) { IsBackground = true, Name = "TcpFrameClient" }.Start();
        }
    }

    private void Serve(TcpClient client)
    {
        bool registered = false;
        try
        {
            client.NoDelay = true;
            client.ReceiveTimeout = 5000;      // para la identificación
            using var stream = client.GetStream();

            var head = new byte[6];
            ReadExact(stream, head, head.Length);
            if (head[0] != 'D' || head[1] != 'R' || head[2] != 'W' || head[3] != 'G' || head[4] != 1)
            {
                Reply(stream, 'N');
                return;
            }
            var tokenBytes = new byte[head[5]];
            ReadExact(stream, tokenBytes, tokenBytes.Length);
            if (Encoding.UTF8.GetString(tokenBytes) != _token)
            {
                Reply(stream, 'N');
                return;
            }
            Reply(stream, 'K');

            // Un móvil nuevo sustituye al anterior
            lock (_lock)
            {
                try { _client?.Close(); } catch { /* ya cerrado */ }
                _client = client;
            }
            _receiver.ClientConnected((client.Client.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? "");
            registered = true;
            client.ReceiveTimeout = 10000;     // si el móvil deja de enviar 10 s, se da por perdido

            var lenBuf = new byte[4];
            var rotBuf = new byte[1];
            while (_running)
            {
                ReadExact(stream, lenBuf, 4);
                int len = (lenBuf[0] << 24) | (lenBuf[1] << 16) | (lenBuf[2] << 8) | lenBuf[3];
                if (len < 2 || len > MaxFrameBytes) break;

                ReadExact(stream, rotBuf, 1);
                var jpeg = new byte[len - 1];
                ReadExact(stream, jpeg, jpeg.Length);

                _receiver.Submit(jpeg, rotBuf[0] & 3);
            }
        }
        catch
        {
            // conexión cortada o datos inválidos: se cierra
        }
        finally
        {
            lock (_lock)
            {
                if (ReferenceEquals(_client, client)) _client = null;
            }
            if (registered) _receiver.ClientDisconnected();
            try { client.Close(); } catch { /* ya cerrado */ }
        }
    }

    private static void Reply(Stream stream, char answer)
    {
        stream.WriteByte((byte)answer);
        stream.Flush();
    }

    private static void ReadExact(Stream stream, byte[] buffer, int count)
    {
        int offset = 0;
        while (offset < count)
        {
            int n = stream.Read(buffer, offset, count - offset);
            if (n <= 0) throw new EndOfStreamException();
            offset += n;
        }
    }
}
