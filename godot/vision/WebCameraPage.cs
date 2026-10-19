namespace DrawingGame.Vision;

/// <summary>Página que sirve <see cref="WebFrameTransport"/> al navegador del móvil: abre la cámara y manda JPEG por WebSocket.</summary>
public static class WebCameraPage
{
    public const string Html = """
<!doctype html>
<html lang="es">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>DrawinGame · Cámara</title>
<style>
body{font-family:system-ui,sans-serif;background:#111;color:#eee;margin:0;padding:20px}
h1{font-size:22px;margin:0 0 8px}
button{font-size:18px;padding:14px;width:100%;margin-top:12px;border:0;border-radius:8px;background:#2d7ff9;color:#fff}
button.stop{background:#555}
select{font-size:16px;padding:8px;width:100%;margin-top:6px}
video{width:100%;margin-top:12px;border-radius:8px;background:#000}
#status{margin-top:12px;font-size:17px}
small{color:#aaa}
</style>
</head>
<body>
<h1>DrawinGame · Cámara</h1>
<small>Mantén esta pestaña abierta y la pantalla encendida mientras juegas.</small>
<label><br>Calidad
<select id="quality">
<option value="540">Baja (540p)</option>
<option value="720" selected>Media (720p)</option>
<option value="1080">Alta (1080p)</option>
</select></label>
<button id="go">Iniciar cámara</button>
<button id="stop" class="stop" style="display:none">Detener</button>
<div id="status"></div>
<video id="preview" muted playsinline autoplay></video>
<script>
const token = new URLSearchParams(location.search).get('t') || '';
const $ = id => document.getElementById(id);
const statusEl = $('status'), video = $('preview'), canvas = document.createElement('canvas');
let stream = null, ws = null, timer = null, running = false, busy = false, wakeLock = null, reconnectTimer = null;
const FPS = 15;
function say(t) { statusEl.textContent = t; }

async function start() {
  if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia) {
    say('Este navegador no deja usar la cámara en esta conexión. Abre el enlace con https y acepta el aviso de seguridad.');
    return;
  }
  if (!token) { say('Falta el código de acceso: escanea otra vez el QR de Godot.'); return; }
  const h = parseInt($('quality').value, 10);
  try {
    stream = await navigator.mediaDevices.getUserMedia({
      audio: false,
      video: { facingMode: { ideal: 'environment' }, width: { ideal: Math.round(h * 16 / 9) }, height: { ideal: h } }
    });
  } catch (e) {
    say('No se pudo abrir la cámara: ' + e.name);
    return;
  }
  video.srcObject = stream;
  running = true;
  $('go').style.display = 'none';
  $('stop').style.display = 'block';
  $('quality').disabled = true;
  requestWake();
  connect();
}

function connect() {
  if (!running) return;
  say('Conectando con el PC…');
  ws = new WebSocket('wss://' + location.host + '/ws?t=' + encodeURIComponent(token));
  ws.onopen = () => {
    say('Conectado. Enviando imagen…');
    clearInterval(timer);
    timer = setInterval(sendFrame, 1000 / FPS);
  };
  ws.onclose = () => {
    clearInterval(timer);
    if (running) {
      say('Sin conexión con el PC. Reintentando…');
      reconnectTimer = setTimeout(connect, 2000);
    }
  };
  ws.onerror = () => {};
}

function sendFrame() {
  if (busy || !ws || ws.readyState !== 1 || ws.bufferedAmount > 1000000) return;
  if (!video.videoWidth) return;
  busy = true;
  canvas.width = video.videoWidth;
  canvas.height = video.videoHeight;
  canvas.getContext('2d').drawImage(video, 0, 0);
  canvas.toBlob(b => {
    if (b && ws && ws.readyState === 1) ws.send(b);
    busy = false;
  }, 'image/jpeg', 0.8);
}

function stop() {
  running = false;
  clearInterval(timer);
  clearTimeout(reconnectTimer);
  if (ws) ws.close();
  if (stream) stream.getTracks().forEach(t => t.stop());
  stream = null;
  if (wakeLock) { wakeLock.release().catch(() => {}); wakeLock = null; }
  video.srcObject = null;
  $('go').style.display = 'block';
  $('stop').style.display = 'none';
  $('quality').disabled = false;
  say('Detenido.');
}

async function requestWake() {
  try { if ('wakeLock' in navigator) wakeLock = await navigator.wakeLock.request('screen'); } catch (e) {}
}
document.addEventListener('visibilitychange', () => {
  if (running && document.visibilityState === 'visible') requestWake();
});
$('go').onclick = start;
$('stop').onclick = stop;
</script>
</body>
</html>
""";
}
