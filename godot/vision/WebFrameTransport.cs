using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace DrawingGame.Vision;

/// <summary>
/// Transporte web: el móvil abre https://ip:puerto/?t=código en el navegador, que carga <see cref="WebCameraPage"/>,
/// abre la cámara y manda cada imagen como mensaje binario JPEG por WebSocket (wss://ip:puerto/ws?t=código).
/// Los navegadores solo dejan usar la cámara en HTTPS, por eso hay un certificado autofirmado (se guarda en 'certPath'):
/// la primera vez el móvil muestra un aviso de seguridad que hay que aceptar.
/// Servidor propio (TcpListener + SslStream + WebSocket.CreateFromStream), sin ASP.NET. No toca ningún objeto de Godot.
/// </summary>
public sealed class WebFrameTransport : IRemoteTransport
{
    public const int DefaultPort = 48751;
    private const int MaxFrameBytes = 16 * 1024 * 1024;
    private const string WebSocketGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

    public string Title => "Sin app (navegador)";
    public string Hint => "Escanéalo con la cámara del móvil, abre el enlace y acepta el aviso de seguridad.";
    public string? Error { get; private set; }
    public int Port { get; private set; }

    private readonly RemoteFrameReceiver _receiver;
    private readonly string _token;
    private readonly int _preferredPort;
    private readonly string _certPath;
    private X509Certificate2? _certificate;
    private TcpListener? _listener;
    private Thread? _acceptThread;
    private volatile bool _running;

    private readonly object _lock = new();
    private WebSocket? _active;      // móvil activo (uno solo; uno nuevo sustituye al anterior)

    public WebFrameTransport(RemoteFrameReceiver receiver, string token, int preferredPort, string certPath)
    {
        _receiver = receiver;
        _token = token;
        _preferredPort = preferredPort;
        _certPath = certPath;
    }

    public string BuildUri(IReadOnlyList<string> hosts) =>
        $"https://{(hosts.Count > 0 ? hosts[0] : "?")}:{Port}/?t={_token}";

    public string Describe(IReadOnlyList<string> hosts) => BuildUri(hosts);

    public void Start()
    {
        if (_running) return;
        try
        {
            _certificate = LoadOrCreateCertificate(_certPath);
        }
        catch (Exception ex)
        {
            Error = "No se pudo crear el certificado para la web: " + ex.Message;
            return;
        }

        try
        {
            _listener = new TcpListener(IPAddress.Any, _preferredPort);
            _listener.Start();
        }
        catch (SocketException)
        {
            try
            {
                _listener = new TcpListener(IPAddress.Any, 0);
                _listener.Start();
            }
            catch (Exception ex)
            {
                Error = "No se pudo abrir el servidor web: " + ex.Message;
                return;
            }
        }

        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _running = true;
        _acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "WebFrameAccept" };
        _acceptThread.Start();
    }

    public void Dispose()
    {
        _running = false;
        try { _listener?.Stop(); } catch { /* ya cerrado */ }
        lock (_lock)
        {
            try { _active?.Abort(); } catch { /* ya cerrado */ }
            _active = null;
        }
        _acceptThread?.Join(1000);
        _certificate?.Dispose();
    }

    // ------------------------------------------------------------ Certificado

    private static X509Certificate2 LoadOrCreateCertificate(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var existing = new X509Certificate2(File.ReadAllBytes(path), (string?)null, X509KeyStorageFlags.Exportable);
                if (existing.NotAfter > DateTime.Now.AddDays(30)) return existing;
                existing.Dispose();
            }
        }
        catch
        {
            // archivo dañado: se crea otro
        }

        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=DrawinGame", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("drawingame.local");
        foreach (var ip in LocalNetwork.Addresses()) san.AddIpAddress(IPAddress.Parse(ip));
        request.CertificateExtensions.Add(san.Build());
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, false));   // autenticación de servidor

        using var created = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(5));
        byte[] pfx = created.Export(X509ContentType.Pfx);
        try { File.WriteAllBytes(path, pfx); } catch { /* se creará otro la próxima vez */ }

        // Volver a cargarlo desde el PFX: SChannel (Windows) no acepta la clave "efímera" recién creada
        return new X509Certificate2(pfx, (string?)null, X509KeyStorageFlags.Exportable);
    }

    // ------------------------------------------------------------ Hilos

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
            new Thread(() => Serve(client)) { IsBackground = true, Name = "WebFrameClient" }.Start();
        }
    }

    private void Serve(TcpClient client)
    {
        WebSocket? ws = null;
        bool registered = false;
        try
        {
            client.NoDelay = true;
            client.ReceiveTimeout = 10000;
            client.SendTimeout = 10000;

            using var ssl = new SslStream(client.GetStream(), false);
            ssl.AuthenticateAsServer(_certificate!, false, System.Security.Authentication.SslProtocols.None, false);

            string? request = ReadHeaders(ssl);
            if (request == null) return;

            var lines = request.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
            var parts = lines[0].Split(' ');
            if (parts.Length < 2 || parts[0] != "GET")
            {
                Respond(ssl, "405 Method Not Allowed", "text/plain; charset=utf-8", "Método no permitido");
                return;
            }

            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 1; i < lines.Length; i++)
            {
                int colon = lines[i].IndexOf(':');
                if (colon > 0) headers[lines[i][..colon].Trim()] = lines[i][(colon + 1)..].Trim();
            }

            string target = parts[1];
            int q = target.IndexOf('?');
            string path = q >= 0 ? target[..q] : target;
            string query = q >= 0 ? target[(q + 1)..] : "";

            if (path == "/")
            {
                Respond(ssl, "200 OK", "text/html; charset=utf-8", WebCameraPage.Html);
                return;
            }

            if (path != "/ws" || !headers.TryGetValue("Sec-WebSocket-Key", out var key))
            {
                Respond(ssl, "404 Not Found", "text/plain; charset=utf-8", "No encontrado");
                return;
            }

            if (!query.Split('&').Contains("t=" + _token))
            {
                Respond(ssl, "403 Forbidden", "text/plain; charset=utf-8", "Código incorrecto");
                return;
            }

            // Subida a WebSocket
            string accept = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key + WebSocketGuid)));
            var reply = Encoding.ASCII.GetBytes(
                "HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\n" +
                $"Sec-WebSocket-Accept: {accept}\r\n\r\n");
            ssl.Write(reply, 0, reply.Length);
            ssl.Flush();

            ws = WebSocket.CreateFromStream(ssl, true, null, TimeSpan.FromSeconds(10));
            client.ReceiveTimeout = 0;
            client.SendTimeout = 0;

            lock (_lock)
            {
                try { _active?.Abort(); } catch { /* ya cerrado */ }
                _active = ws;
            }
            _receiver.ClientConnected((client.Client.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? "");
            registered = true;

            var buffer = new byte[64 * 1024];
            using var message = new MemoryStream();
            while (_running && ws.State == WebSocketState.Open)
            {
                message.SetLength(0);
                WebSocketReceiveResult result;
                do
                {
                    // Si el móvil deja de enviar 15 s se da por perdido
                    using var timeout = new CancellationTokenSource(15000);
                    result = ws.ReceiveAsync(new ArraySegment<byte>(buffer), timeout.Token).GetAwaiter().GetResult();
                    if (result.MessageType == WebSocketMessageType.Close) return;
                    if (message.Length + result.Count > MaxFrameBytes) return;
                    message.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                if (result.MessageType == WebSocketMessageType.Binary)
                    _receiver.Submit(message.ToArray(), 0);   // el navegador ya entrega la imagen derecha
            }
        }
        catch
        {
            // conexión cortada, aviso de certificado rechazado por el navegador o datos inválidos: se cierra
        }
        finally
        {
            lock (_lock)
            {
                if (ws != null && ReferenceEquals(_active, ws)) _active = null;
            }
            if (registered) _receiver.ClientDisconnected();
            try { ws?.Abort(); } catch { /* ya cerrado */ }
            try { client.Close(); } catch { /* ya cerrado */ }
        }
    }

    /// <summary>Lee la cabecera HTTP (hasta la línea en blanco). null si es demasiado larga o se corta.</summary>
    private static string? ReadHeaders(Stream stream)
    {
        var bytes = new List<byte>(512);
        var one = new byte[1];
        while (bytes.Count < 16384)
        {
            if (stream.Read(one, 0, 1) <= 0) return null;
            bytes.Add(one[0]);
            int n = bytes.Count;
            if (n >= 4 && bytes[n - 4] == '\r' && bytes[n - 3] == '\n' && bytes[n - 2] == '\r' && bytes[n - 1] == '\n')
                return Encoding.UTF8.GetString(bytes.ToArray());
        }
        return null;
    }

    private static void Respond(Stream stream, string status, string contentType, string body)
    {
        var content = Encoding.UTF8.GetBytes(body);
        var head = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {status}\r\nContent-Type: {contentType}\r\nContent-Length: {content.Length}\r\n" +
            "Cache-Control: no-store\r\nConnection: close\r\n\r\n");
        stream.Write(head, 0, head.Length);
        stream.Write(content, 0, content.Length);
        stream.Flush();
    }
}
