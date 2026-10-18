# Entorno y dependencias

## Usuario
- Windows, España (Europe/Madrid). Carpeta del proyecto: `F:\Proyectos\DrawinGame`.
- Nuevo en Godot. Sabe compilar y ejecutar con `dotnet run`.

## Herramientas
- **.NET SDK 8** o superior (`dotnet --version`).
- **OpenCvSharp4** `4.10.0.20241108` + `OpenCvSharp4.Extensions` + `OpenCvSharp4.runtime.win` (paquetes NuGet).
- **Godot 4.7.2 edición .NET (mono)**, ya descargado en `Godot_v4.7.2/` dentro del repo (ignorado por git). Comprobar qué versión del SDK de .NET exige.
- Cámara: se abre con `VideoCapture(índice, VideoCaptureAPIs.DSHOW)` (DirectShow, Windows).

## Entorno de Claude (limitaciones)
- Claude trabaja en un contenedor en la nube con .NET 8 SDK instalable, pero **NuGet está bloqueado** (`api.nuget.org`, 403), así que **no puede restaurar paquetes ni compilar** el proyecto.
- Tampoco hay webcam ni Windows. Todo se compila y prueba en el PC del usuario, que pega los errores.
- Para que Claude pudiera compilar habría que permitir `api.nuget.org` y `nuget.org` en la lista de dominios del entorno.
- Los archivos del proyecto se editan en el PC del usuario mediante el puente del dispositivo (`device_bash`); `rm` está bloqueado por defecto y requiere permiso explícito.

## Trampas conocidas del código
- `ImplicitUsings` + WinForms importa `System.Drawing` globalmente: `Size`, `Point` y `Rect` son **ambiguos** con OpenCvSharp. Calificar siempre: `OpenCvSharp.Size`, `OpenCvSharp.Point`, `System.Drawing.Size`.
- `Cv2.Accumulate` exige el argumento `mask` (pasar `null`).
- `Cv2.PutText` (Hershey) solo admite ASCII: los textos dibujados en la imagen van sin acentos ni ñ.
- Git: `.gitignore` cubre Godot, C#/.NET, Python (residual) e IDEs. `LICENSE` aparece modificado por saltos de línea (CRLF), no por contenido.
