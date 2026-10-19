using OpenCvSharp;

namespace DrawingGame.Vision;

/// <summary>Ajustes de los filtros previos a la detección.</summary>
/// <param name="TemporalPct">Promedio entre fotogramas (0-90 %). 0 = desactivado.</param>
/// <param name="SpatialLevel">Filtro bilateral (0-10). 0 = desactivado.</param>
/// <param name="WhiteBalance">Balance de blancos automático (el papel pasa a ser blanco).</param>
/// <param name="IlluminationPct">Corrección de sombras / luz desigual (0-100 %).</param>
/// <param name="ClaheLevel">Contraste local CLAHE (0-10). 0 = desactivado.</param>
/// <param name="ColorBoostPct">Realce de saturación (0-100 %).</param>
public record FilterSettings(int TemporalPct, int SpatialLevel, bool WhiteBalance,
                             int IlluminationPct, int ClaheLevel, int ColorBoostPct);

/// <summary>
/// Genera la imagen filtrada que se pasa al detector: menos ruido, menos dependiente de la luz
/// y con los colores más marcados. Tiene estado (promedio temporal, ganancias del balance de blancos).
///
/// Orden: 1) promedio temporal  2) filtro bilateral  3) balance de blancos (white-patch)
///        4) corrección de iluminación + CLAHE sobre L (Lab)  5) realce de saturación (HSV).
/// </summary>
public sealed class ImageFilter : IDisposable
{
    private const double MotionThreshold = 25;   // cambio por píxel a partir del cual no se promedia (movimiento)
    private const double GainSmoothing = 0.9;    // suavizado de las ganancias del balance de blancos

    private Mat? _acc;                           // acumulador temporal (32FC3)
    private readonly double[] _gain = { 1, 1, 1 };
    private bool _gainInit;
    private CLAHE? _clahe;
    private double _claheClip = -1;

    /// <summary>Olvida el estado (al cambiar de tamaño de imagen, de área, etc.).</summary>
    public void Reset()
    {
        _acc?.Dispose();
        _acc = null;
        _gainInit = false;
    }

    /// <summary>Devuelve una imagen nueva filtrada (BGR, 8 bits). El que llama debe liberarla.</summary>
    public Mat Apply(Mat src, FilterSettings s)
    {
        Mat cur = Temporal(src, s.TemporalPct);
        cur = Chain(cur, m => Spatial(m, s.SpatialLevel));
        cur = Chain(cur, m => s.WhiteBalance ? WhiteBalance(m) : null);
        cur = Chain(cur, m => Lighting(m, s.IlluminationPct, s.ClaheLevel));
        cur = Chain(cur, m => ColorBoost(m, s.ColorBoostPct));
        return cur;
    }

    /// <summary>Aplica un paso; si devuelve una imagen nueva libera la anterior, si devuelve null la conserva.</summary>
    private static Mat Chain(Mat input, Func<Mat, Mat?> step)
    {
        var output = step(input);
        if (output == null) return input;
        input.Dispose();
        return output;
    }

    // ---- 1) Promedio temporal: reduce el ruido de sensor sin desenfocar bordes -------------------
    // Media móvil exponencial por píxel. Donde el píxel cambia mucho (algo se mueve) se toma el valor
    // nuevo directamente para no dejar «fantasmas».
    private Mat Temporal(Mat src, int pct)
    {
        if (pct <= 0)
        {
            _acc?.Dispose();
            _acc = null;
            return src.Clone();
        }

        double alpha = Math.Max(0.05, 1.0 - pct / 100.0);
        using var src32 = new Mat();
        src.ConvertTo(src32, MatType.CV_32FC3);

        if (_acc == null || _acc.Size() != src.Size())
        {
            _acc?.Dispose();
            _acc = src32.Clone();
        }
        else
        {
            using var diff = new Mat();
            Cv2.Absdiff(src32, _acc, diff);
            Cv2.Split(diff, out Mat[] ch);
            using var maxDiff = new Mat();
            Cv2.Max(ch[0], ch[1], maxDiff);
            Cv2.Max(maxDiff, ch[2], maxDiff);
            foreach (var c in ch) c.Dispose();

            using var moved32 = new Mat();
            Cv2.Threshold(maxDiff, moved32, MotionThreshold, 255, ThresholdTypes.Binary);
            using var moved = new Mat();
            moved32.ConvertTo(moved, MatType.CV_8UC1);

            Cv2.AccumulateWeighted(src32, _acc, alpha, null);
            src32.CopyTo(_acc, moved);
        }

        var result = new Mat();
        _acc.ConvertTo(result, MatType.CV_8UC3);
        return result;
    }

    // ---- 2) Filtro bilateral: suaviza el ruido conservando los bordes de los trazos -------------
    private static Mat? Spatial(Mat m, int level)
    {
        if (level <= 0) return null;
        var dst = new Mat();
        Cv2.BilateralFilter(m, dst, 5, level * 7.5, 5);
        return dst;
    }

    // ---- 3) Balance de blancos (white-patch): el papel se asume blanco --------------------------
    // Cada canal se escala para que su valor más alto (del papel, ya suavizado) llegue a 255.
    // Compensa luces cálidas/frías. Las ganancias se suavizan en el tiempo para que no parpadeen.
    private Mat WhiteBalance(Mat m)
    {
        double scale = 160.0 / m.Width;
        using var small = new Mat();
        Cv2.Resize(m, small, new OpenCvSharp.Size(160, Math.Max(1, (int)(m.Height * scale))));
        Cv2.GaussianBlur(small, small, new OpenCvSharp.Size(9, 9), 0);
        Cv2.Split(small, out Mat[] smallCh);

        for (int i = 0; i < 3; i++)
        {
            Cv2.MinMaxLoc(smallCh[i], out _, out double max);
            smallCh[i].Dispose();
            double target = max > 20 ? Math.Clamp(255.0 / max, 1.0, 2.5) : 1.0;
            _gain[i] = _gainInit ? _gain[i] * GainSmoothing + target * (1 - GainSmoothing) : target;
        }
        _gainInit = true;

        Cv2.Split(m, out Mat[] ch);
        for (int i = 0; i < 3; i++)
        {
            var scaled = new Mat();
            ch[i].ConvertTo(scaled, MatType.CV_8UC1, _gain[i]);
            ch[i].Dispose();
            ch[i] = scaled;
        }
        var dst = new Mat();
        Cv2.Merge(ch, dst);
        foreach (var c in ch) c.Dispose();
        return dst;
    }

    // ---- 4) Iluminación: sombras y luz desigual (+ contraste local CLAHE) -------------------------
    // En el canal de luminosidad L (espacio Lab, que separa luz de color):
    //  a) se estima la iluminación del papel: dilatación grande (borra los trazos) + desenfoque;
    //  b) se divide L entre esa estimación (idea de Retinex): el papel queda uniforme y las sombras desaparecen;
    //  c) opcionalmente CLAHE (ecualización adaptativa con límite de contraste) para realzar el contraste local.
    private Mat? Lighting(Mat m, int illumPct, int claheLevel)
    {
        if (illumPct <= 0 && claheLevel <= 0) return null;

        using var lab = new Mat();
        Cv2.CvtColor(m, lab, ColorConversionCodes.BGR2Lab);
        Cv2.Split(lab, out Mat[] ch);
        Mat l = ch[0];

        if (illumPct > 0)
        {
            double k = illumPct / 100.0;

            using var small = new Mat();
            Cv2.Resize(l, small, new OpenCvSharp.Size(Math.Max(1, l.Width / 4), Math.Max(1, l.Height / 4)));
            using var kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new OpenCvSharp.Size(15, 15));
            Cv2.Dilate(small, small, kernel);
            Cv2.GaussianBlur(small, small, new OpenCvSharp.Size(21, 21), 0);

            using var illum = new Mat();
            Cv2.Resize(small, illum, l.Size(), 0, 0, InterpolationFlags.Linear);

            using var l32 = new Mat();
            using var i32 = new Mat();
            l.ConvertTo(l32, MatType.CV_32F);
            illum.ConvertTo(i32, MatType.CV_32F);
            using var floor = new Mat(i32.Size(), MatType.CV_32FC1, new Scalar(30));
            Cv2.Max(i32, floor, i32);                 // evita dividir por casi cero en zonas muy oscuras

            using var ratio = new Mat();
            Cv2.Divide(l32, i32, ratio);
            using var normalized = new Mat();
            ratio.ConvertTo(normalized, MatType.CV_8U, 255);   // satura a 255: el papel queda blanco

            var blended = new Mat();
            Cv2.AddWeighted(l, 1 - k, normalized, k, 0, blended);
            l.Dispose();
            l = blended;
        }

        if (claheLevel > 0)
        {
            double clip = claheLevel * 0.5;
            if (_clahe == null || Math.Abs(_claheClip - clip) > 1e-6)
            {
                _clahe?.Dispose();
                _clahe = Cv2.CreateCLAHE(clip, new OpenCvSharp.Size(8, 8));
                _claheClip = clip;
            }
            var enhanced = new Mat();
            _clahe.Apply(l, enhanced);
            l.Dispose();
            l = enhanced;
        }

        ch[0] = l;
        using var merged = new Mat();
        Cv2.Merge(ch, merged);
        foreach (var c in ch) c.Dispose();

        var dst = new Mat();
        Cv2.CvtColor(merged, dst, ColorConversionCodes.Lab2BGR);
        return dst;
    }

    // ---- 5) Realce de color: multiplica la saturación (HSV) -------------------------------------
    private static Mat? ColorBoost(Mat m, int pct)
    {
        if (pct <= 0) return null;
        double gain = 1.0 + pct / 50.0;   // 0 % -> x1, 100 % -> x3

        using var hsv = new Mat();
        Cv2.CvtColor(m, hsv, ColorConversionCodes.BGR2HSV);
        Cv2.Split(hsv, out Mat[] ch);

        var boosted = new Mat();
        ch[1].ConvertTo(boosted, MatType.CV_8UC1, gain);
        ch[1].Dispose();
        ch[1] = boosted;

        using var merged = new Mat();
        Cv2.Merge(ch, merged);
        foreach (var c in ch) c.Dispose();

        var dst = new Mat();
        Cv2.CvtColor(merged, dst, ColorConversionCodes.HSV2BGR);
        return dst;
    }

    public void Dispose()
    {
        _acc?.Dispose();
        _clahe?.Dispose();
    }
}
