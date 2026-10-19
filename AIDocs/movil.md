# Cámara del móvil (app Android + servidor en Godot)

Permite usar la cámara de un móvil Android en lugar de la webcam del PC. **Escrito a ciegas (sin compilar ni probar)**: compilar y pegar errores.

## Decisión
- App **nativa Android en Kotlin**, mínima (`MobileApp/Android`). iOS (app) descartado de momento: exige Mac + Xcode y cuenta de desarrollador.
- **Opción web** además de la app, para quien no quiera instalar nada (y para iPhone): Godot sirve una página que abre la cámara en el navegador (`WebFrameTransport`). Inconveniente: HTTPS con certificado autofirmado, así que el móvil muestra un aviso de seguridad que hay que aceptar. No sirve con la pantalla apagada (el navegador pausa la página).
- Sin servidor HTTP/WebSocket: **TCP plano propio** (más simple y rápido, sin certificados). Solo en red local.

## Flujo
1. Godot arranca en **SourceSelect** («Cámara del PC» / «Cámara del móvil»).
2. Móvil → **RemoteWait**: `TcpFrameTransport` escucha en el puerto 48750 (si está ocupado, uno libre) y se muestra un **QR** con `drawingame://connect?h=<ip1>,<ip2>&p=<puerto>&t=<código>`. Las IPs son las de adaptadores con puerta de enlace (`LocalNetwork.Addresses`).
3. En el móvil: «Escanear QR» (lector de Google, `play-services-code-scanner`) o la cámara normal (el enlace `drawingame://` abre la app). La app prueba cada IP, se identifica y empieza a enviar.
4. Al conectarse el móvil, Godot pasa solo a **AreaSelect** (esquinas). Botón «Girar imagen 90°» para dejar el folio apaisado (borra las esquinas).
5. Si el móvil se desconecta, Godot sigue y avisa «Móvil desconectado…»; la app reconecta sola cada ~2 s.

## Protocolo (TCP)
- móvil → PC al conectar: `"DRWG"`, versión `1`, longitud del código (1 byte), código UTF-8.
- PC → móvil: `'K'` aceptado / `'N'` rechazado (código incorrecto). Luego el PC no envía nada (el móvil detecta el corte con un `read` bloqueante).
- móvil → PC, por imagen: longitud (4 bytes big-endian) = 1 + tamaño del JPEG; giro (1 byte, cuartos de vuelta **horarios** para dejarla derecha, de `imageInfo.rotationDegrees`); JPEG.
- Código de acceso (`RemoteToken`) y puerto (`RemotePort`) se guardan en `user://config.json`, así el botón «Reconectar con el último PC» de la app sigue valiendo mientras no cambie la IP.
- Un móvil nuevo autenticado sustituye al anterior.

## Godot (arquitectura)
Pensado para añadir fuentes y formas de conexión sin tocar `Main`/`CameraService`:
- `ICameraSource` (`TryGetFrame`, `Connected`, `Error`): de dónde salen las imágenes. `CameraService.Start(ICameraSource)` solo conoce esto. Implementaciones: `WebcamSource` (webcam, se abre en el hilo de la cámara) y `RemoteFrameReceiver` (móvil).
- `RemoteFrameReceiver`: guarda la última imagen, decodifica el JPEG (`Cv2.ImDecode`) y la gira (`giro del móvil + AppConfig.RemoteRotation`). Cuenta los móviles conectados (`ClientConnected`/`ClientDisconnected`). No sabe nada de redes.
- `IRemoteTransport` (`Title`, `Hint`, `Error`, `Port`, `Start`, `BuildUri`, `Describe`): una forma de conectar el móvil. Entrega JPEG con `receiver.Submit(jpeg, giro)`. Implementaciones: `TcpFrameTransport` (app Android) y `WebFrameTransport` (navegador). La pantalla del QR muestra **un QR por transporte** de la lista `Main._transports`, con debajo la dirección/enlace escrito (`Describe`) por si no se puede escanear.
- **Añadir una forma nueva**: clase que implemente `IRemoteTransport` y añadirla en `Main.EnsureRemote`. Nada más.
- `Main`: estados `SourceSelect` y `RemoteWait`; `ChooseSource` (cambiar de origen borra las esquinas guardadas), `EnsureRemote`/`StopRemote`, `StartCamera`/`StopCamera`/`StopWebcam`, `BackToSource`, `RotateRemote`. El receptor y los transportes existen solo mientras el origen es el móvil.
- `LocalNetwork.Addresses()`: IPs IPv4 del PC (primero las de adaptadores con puerta de enlace).
- QR con el paquete NuGet **QRCoder** (se dibuja a mano en un `ImageTexture`, sin System.Drawing).
- Windows pedirá permiso de **cortafuegos** la primera vez: permitir en redes privadas. El PC y el móvil deben estar en la misma red (ojo con «aislamiento de clientes» en WiFi de invitados).

## Transporte web (`WebFrameTransport`, `WebCameraPage`)
- QR con `https://<ip>:48751/?t=<código>` (solo la primera IP, la de la red real). El puerto por defecto es 48751 (`AppConfig.WebPort`); **no usar 80/443** (IIS u otros servidores web del PC).
- Servidor propio: `TcpListener` + `SslStream` + `WebSocket.CreateFromStream`, sin ASP.NET. Rutas: `/` → página (HTML en `WebCameraPage.Html`); `/ws?t=<código>` → WebSocket (403 si el código no coincide); resto 404.
- Certificado autofirmado RSA-2048 (5 años, con las IPs del PC en el SAN), guardado en `user://remote-cert.pfx` (sin contraseña, solo en este PC). Se recarga desde el PFX porque SChannel (Windows) no acepta la clave efímera recién creada.
- La página (JS): `getUserMedia` (cámara trasera, 540/720/1080p), cada imagen a ~15/s con `canvas.toBlob('image/jpeg', 0.8)` y `ws.send`; salta imágenes si `bufferedAmount` > 1 MB; pide `wakeLock` de pantalla; reconecta sola. Cada mensaje binario = un JPEG ya derecho (giro 0).
- Si no llega ninguna imagen en 15 s, el PC da la conexión por perdida.

## App Android (`MobileApp/Android`)
- Kotlin, minSdk 26, targetSdk 35, Gradle 8.9 / AGP 8.7.3 / Kotlin 2.0.21, CameraX 1.4.1.
- `MainActivity`: interfaz por código (escanear QR, reconectar, detener, calidad 540p/720p/1080p, «Pantalla negra», «Mantener pantalla encendida»); pide permisos de cámara y notificaciones; atiende el enlace `drawingame://connect`.
- `StreamService`: servicio **en primer plano** (`foregroundServiceType="camera"`) que lleva CameraX (`ImageAnalysis` RGBA, sin vista previa), comprime a JPEG (~15 imágenes/s) y envía por `Connection`. Con **wake lock parcial y wifi lock** sigue funcionando con la pantalla apagada o la app cerrada; notificación con «Detener».
- `Connection`: reconexión automática, prueba cada IP del QR.
- Calidad: 540p = 960×540 q80; 720p = 1280×720 q80; 1080p = 1920×1080 q85.

## Dudas por comprobar en el móvil
- **Pantalla apagada**: debería seguir enviando gracias al servicio en primer plano. Algunos fabricantes (Xiaomi, Huawei, Samsung…) matan servicios o pausan la cámara con la pantalla apagada: si pasa, desactivar el ahorro de batería para la app (Ajustes > Batería) o usar «Pantalla negra» (brillo al mínimo, la pantalla sigue encendida).
- Leer el QR con la cámara normal: depende del móvil/Google Lens; los enlaces con esquema propio no siempre se ofrecen para abrir.
- Autoenfoque: CameraX usa el continuo; si molesta a la detección, valorar enfoque fijo.
- La resolución pedida a CameraX se interpreta en orientación del sensor (apaisada); si sale otra, se ajusta al más cercano.
