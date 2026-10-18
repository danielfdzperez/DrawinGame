using OpenCvSharp;

namespace DrawingGame.Vision;

/// <summary>
/// Utilidades para el área seleccionada por el usuario (4 esquinas):
/// ordenar las esquinas y enderezar el área con corrección de perspectiva.
/// </summary>
public static class AreaWarp
{
    // Tamaño del área aplanada (proporción A4)
    private const int LongSide = 900;
    private const int ShortSide = 636;

    /// <summary>Ordena las esquinas como: arriba-izq, arriba-der, abajo-der, abajo-izq.</summary>
    public static Point2f[] Order(Point2f[] p)
    {
        var tl = p.OrderBy(a => a.X + a.Y).First();
        var br = p.OrderByDescending(a => a.X + a.Y).First();
        var tr = p.OrderBy(a => a.Y - a.X).First();
        var bl = p.OrderByDescending(a => a.Y - a.X).First();
        return new[] { tl, tr, br, bl };
    }

    /// <summary>
    /// Recorta y endereza el área definida por 4 esquinas ya ordenadas (ver <see cref="Order"/>).
    /// 'shrink' (0 = nada) acerca las esquinas al centro, por si se quiere evitar el borde.
    /// </summary>
    public static Mat Warp(Mat frame, Point2f[] quad, double shrink = 0)
    {
        var center = new Point2f(quad.Average(p => p.X), quad.Average(p => p.Y));
        var src = quad.Select(p => new Point2f(
            p.X + (center.X - p.X) * (float)shrink,
            p.Y + (center.Y - p.Y) * (float)shrink)).ToArray();

        double widthLen = (Dist(quad[0], quad[1]) + Dist(quad[3], quad[2])) / 2;
        double heightLen = (Dist(quad[0], quad[3]) + Dist(quad[1], quad[2])) / 2;
        bool landscape = widthLen >= heightLen;
        int w = landscape ? LongSide : ShortSide;
        int h = landscape ? ShortSide : LongSide;

        var dst = new[]
        {
            new Point2f(0, 0), new Point2f(w - 1, 0), new Point2f(w - 1, h - 1), new Point2f(0, h - 1)
        };

        using var m = Cv2.GetPerspectiveTransform(src, dst);
        var result = new Mat();
        Cv2.WarpPerspective(frame, result, m, new OpenCvSharp.Size(w, h));
        return result;
    }

    private static double Dist(Point2f a, Point2f b)
    {
        double dx = a.X - b.X, dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
