using System.Runtime.InteropServices;
using OpenCvSharp;

namespace DrawingGame.Vision;

/// <summary>Una foto de lo que ve una cámara (miniatura RGB).</summary>
public record CameraSnapshot(int Index, byte[] Rgb, int Width, int Height);

/// <summary>Busca las cámaras disponibles y saca una miniatura de cada una. Bloqueante: llamar desde un hilo.</summary>
public static class CameraProbe
{
    private const int ThumbWidth = 320;

    public static List<CameraSnapshot> Scan(int maxIndex = 10)
    {
        var result = new List<CameraSnapshot>();

        for (int i = 0; i < maxIndex; i++)
        {
            try
            {
                using var cap = new VideoCapture(i, VideoCaptureAPIs.DSHOW);
                if (!cap.IsOpened()) continue;

                // Los primeros fotogramas suelen salir negros (autoexposición): se descartan
                using var frame = new Mat();
                using var best = new Mat();
                for (int k = 0; k < 15; k++)
                {
                    if (cap.Read(frame) && !frame.Empty()) frame.CopyTo(best);
                    else Thread.Sleep(30);
                }
                if (best.Empty()) continue;

                double scale = (double)ThumbWidth / best.Width;
                using var small = new Mat();
                Cv2.Resize(best, small, new OpenCvSharp.Size(ThumbWidth, Math.Max(1, (int)(best.Height * scale))));

                using var rgb = new Mat();
                Cv2.CvtColor(small, rgb, ColorConversionCodes.BGR2RGB);
                int len = (int)(rgb.Total() * rgb.ElemSize());
                var bytes = new byte[len];
                Marshal.Copy(rgb.Data, bytes, 0, len);

                result.Add(new CameraSnapshot(i, bytes, rgb.Width, rgb.Height));
            }
            catch
            {
                // índice no válido o cámara ocupada: se ignora
            }
        }
        return result;
    }
}
