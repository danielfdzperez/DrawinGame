using OpenCvSharp;

namespace DrawingGame.Vision;

/// <summary>
/// De dónde salen las imágenes: una webcam del PC, un móvil conectado... El <see cref="CameraService"/> solo conoce esta interfaz.
/// Quien crea la fuente es quien la cierra (después de parar el servicio).
/// </summary>
public interface ICameraSource : IDisposable
{
    /// <summary>La fuente está lista y entregando imágenes (webcam abierta, móvil conectado...).</summary>
    bool Connected { get; }

    /// <summary>Error que impide seguir usando la fuente (null si no hay). Si no es null, el servicio se detiene.</summary>
    string? Error { get; }

    /// <summary>Espera hasta 'timeoutMs' una imagen nueva (BGR) y la copia en 'destination'. false si no llegó ninguna.</summary>
    bool TryGetFrame(Mat destination, int timeoutMs);
}
