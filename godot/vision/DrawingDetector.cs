using OpenCvSharp;

namespace DrawingGame.Vision;

/// <summary>
/// Una forma detectada: contorno exterior + huecos interiores, como listas de vértices
/// en coordenadas de imagen (píxeles).
/// </summary>
public record DetectedShape(OpenCvSharp.Point[] Outline, List<OpenCvSharp.Point[]> Holes)
{
    public int VertexCount => Outline.Length + Holes.Sum(h => h.Length);
}

/// <summary>
/// Detecta trazos negros sobre fondo blanco y los convierte en polígonos simplificados.
/// </summary>
public static class DrawingDetector
{
    /// <param name="frame">Fotograma BGR (no se modifica).</param>
    /// <param name="threshold">Umbral de gris (0-255). Lo más oscuro que esto se considera trazo.</param>
    /// <param name="contrast">Contraste mínimo respecto al entorno para trazos finos. 0 = desactivado.</param>
    /// <param name="minSize">Tamaño mínimo (aprox. en píxeles) de una forma; descarta manchas y ruido.</param>
    /// <param name="epsilon">Tolerancia de simplificación en píxeles (Douglas-Peucker). Mayor = menos vértices.</param>
    public static List<DetectedShape> Detect(Mat frame, int threshold, int contrast, int minSize, double epsilon)
    {
        using var gray = new Mat();
        Cv2.CvtColor(frame, gray, ColorConversionCodes.BGR2GRAY);
        Cv2.GaussianBlur(gray, gray, new OpenCvSharp.Size(3, 3), 0);

        // 1) Zonas oscuras de forma absoluta (trazos gruesos / rellenos): oscuro -> 255
        using var mask = new Mat();
        Cv2.Threshold(gray, mask, threshold, 255, ThresholdTypes.BinaryInv);

        // 2) Trazos finos por contraste local (black-hat)
        if (contrast > 0)
        {
            using var kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new OpenCvSharp.Size(15, 15));
            using var blackHat = new Mat();
            Cv2.MorphologyEx(gray, blackHat, MorphTypes.BlackHat, kernel);

            using var thin = new Mat();
            Cv2.Threshold(blackHat, thin, contrast, 255, ThresholdTypes.Binary);
            Cv2.BitwiseOr(mask, thin, mask);
        }

        // 3) Contornos con jerarquía (exteriores + huecos) -> polígonos simplificados
        Cv2.FindContours(mask, out OpenCvSharp.Point[][] contours, out HierarchyIndex[] hierarchy,
            RetrievalModes.CComp, ContourApproximationModes.ApproxSimple);

        var shapes = new List<DetectedShape>();
        for (int i = 0; i < contours.Length; i++)
        {
            if (hierarchy[i].Parent != -1) continue; // los huecos se recogen desde su contorno padre

            // En trazos finos el área es pequeña, así que también se usa perímetro/2
            double size = Math.Max(Cv2.ContourArea(contours[i]), Cv2.ArcLength(contours[i], true) / 2);
            if (size < minSize) continue;

            var outline = Cv2.ApproxPolyDP(contours[i], epsilon, true);
            if (outline.Length < 3) continue;

            var holes = new List<OpenCvSharp.Point[]>();
            for (int h = hierarchy[i].Child; h != -1; h = hierarchy[h].Next)
            {
                if (Cv2.ContourArea(contours[h]) < minSize) continue;
                var hole = Cv2.ApproxPolyDP(contours[h], epsilon, true);
                if (hole.Length >= 3) holes.Add(hole);
            }

            shapes.Add(new DetectedShape(outline, holes));
        }
        return shapes;
    }

    /// <summary>Dibuja las formas sobre el fotograma: relleno verde translúcido, contorno y vértices.</summary>
    public static void Paint(Mat frame, List<DetectedShape> shapes)
    {
        if (shapes.Count == 0) return;

        var all = new List<OpenCvSharp.Point[]>();
        foreach (var s in shapes)
        {
            all.Add(s.Outline);
            all.AddRange(s.Holes);
        }

        // Relleno translúcido (con huecos)
        using var overlay = frame.Clone();
        Cv2.FillPoly(overlay, all, new Scalar(0, 200, 0));
        Cv2.AddWeighted(overlay, 0.45, frame, 0.55, 0, frame);

        // Contorno
        Cv2.Polylines(frame, all, true, new Scalar(0, 255, 0), 2);

        // Vértices
        foreach (var poly in all)
            foreach (var p in poly)
                Cv2.Circle(frame, p, 3, new Scalar(0, 255, 255), -1);
    }
}
