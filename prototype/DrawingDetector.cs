using OpenCvSharp;

namespace DrawinGamePrototype;

/// <summary>Color de tinta reconocido.</summary>
public enum InkColor { Black, Red, Yellow, Green, Blue }

/// <summary>
/// Una forma detectada: contorno exterior + huecos (vértices en píxeles de la imagen),
/// su familia de color y el color medio real de sus píxeles (BGR).
/// </summary>
public record DetectedShape(OpenCvSharp.Point[] Outline, List<OpenCvSharp.Point[]> Holes, InkColor Ink, Scalar Color)
{
    public int VertexCount => Outline.Length + Holes.Sum(h => h.Length);
}

/// <summary>Ajustes de la detección.</summary>
/// <param name="InkDifference">Diferencia de color (Lab, ~0-100) con el papel local a partir de la cual algo es tinta.</param>
/// <param name="ColorDifference">Diferencia de color (a,b de Lab) entre la tinta y el papel a partir de la cual una figura es "de color" y no negra (típico 6-10).</param>
/// <param name="MinSize">Tamaño mínimo de una forma (descarta ruido).</param>
/// <param name="Epsilon">Tolerancia de simplificación del polígono (px).</param>
/// <param name="AutoSensitivity">Si es true, InkDifference se ignora y se estima del ruido del papel en cada fotograma.</param>
/// <param name="AutoColor">Si es true, ColorDifference se ignora y se estima del ruido de color del papel.</param>
/// <param name="InferShapes">Si es true, las manchas sólidas se sustituyen por la figura probable (rectángulo girado o polígono convexo) y se ignoran huecos pequeños.</param>
public record DetectionSettings(int InkDifference, int ColorDifference, int MinSize, double Epsilon,
                                bool AutoSensitivity = false, bool AutoColor = false, bool InferShapes = false);

/// <summary>
/// Detecta trazos sobre papel claro en la imagen YA FILTRADA (ver ImageFilter) y los convierte
/// en polígonos con su color.
/// </summary>
public static class DrawingDetector
{
    // Solo 5 colores. Cada matiz (H de OpenCV: 0-179) se asigna al más cercano entre rojo (0/180), amarillo (30),
    // verde (60) y azul (120): naranja → amarillo, cian → verde/azul, morado y rosa → azul/rojo.
    private static readonly (InkColor Ink, int From, int To)[] HueBands =
    {
        (InkColor.Red, 0, 14),
        (InkColor.Yellow, 15, 44),
        (InkColor.Green, 45, 89),
        (InkColor.Blue, 90, 149),
        (InkColor.Red, 150, 179)
    };

    private static InkColor ClassOfHue(int hue)
    {
        foreach (var band in HueBands)
            if (hue >= band.From && hue <= band.To) return band.Ink;
        return InkColor.Red;
    }

    public static string Name(InkColor ink) => ink switch
    {
        InkColor.Black => "negro",
        InkColor.Red => "rojo",
        InkColor.Yellow => "amarillo",
        InkColor.Green => "verde",
        InkColor.Blue => "azul",
        _ => ink.ToString()
    };

    private const double HighRatio = 1.8;
    // Un hueco es real (papel visto a través de la figura) solo si su diferencia media con el papel es menor que
    // esta fracción del umbral de tinta. Las vetas claras dentro de un trazo de rotulador siguen siendo "casi tinta".
    private const double HoleDistanceRatio = 0.5;   // umbral alto de la histéresis = bajo × 1.8

    /// <summary>
    /// Detecta figuras. <paramref name="mask"/> es la imagen en blanco y negro de lo que se considera tinta
    /// (solo componentes aceptadas); el llamador debe liberarla.
    /// </summary>
    public static List<DetectedShape> Detect(Mat filtered, DetectionSettings s, out Mat mask, out double usedDifference, out double usedColor)
    {
        var shapes = new List<DetectedShape>();
        mask = new Mat(filtered.Size(), MatType.CV_8UC1, Scalar.All(0));

        // Se trabaja en Lab (L = luminosidad, a/b = color): la distancia entre dos colores es casi euclídea.
        using var lab8 = new Mat();
        Cv2.CvtColor(filtered, lab8, ColorConversionCodes.BGR2Lab);
        using var lab = new Mat();
        lab8.ConvertTo(lab, MatType.CV_32FC3);

        // Tinta = lo que se diferencia del "papel" local (negro, lápiz gris y colores pálidos por igual,
        // y aguanta degradados de iluminación porque el papel se estima por zonas)
        using var paper = EstimatePaper(lab8);
        using var diff = new Mat();
        Cv2.Subtract(lab, paper, diff);   // (dL, da, db) respecto al papel
        Magnitudes(diff, out Mat distance, out Mat abDistance);
        using var distanceDisposer = distance;
        using var abDisposer = abDistance;
        Cv2.GaussianBlur(distance, distance, new OpenCvSharp.Size(0, 0), 1.0);
        Cv2.GaussianBlur(abDistance, abDistance, new OpenCvSharp.Size(0, 0), 1.0);

        double low = s.AutoSensitivity ? EstimateNoiseThreshold(distance, 8, 40) : s.InkDifference;
        usedDifference = low;
        double colorThreshold = s.AutoColor ? EstimateNoiseThreshold(abDistance, 6, 30) : s.ColorDifference;
        usedColor = colorThreshold;
        using var lowMask = Binarize(distance, low);
        using var highMask = Binarize(distance, low * HighRatio);

        // Cerrar tapa agujeros de 1 px dentro de un trazo
        using var k3 = Cv2.GetStructuringElement(MorphShapes.Ellipse, new OpenCvSharp.Size(3, 3));
        Cv2.MorphologyEx(lowMask, lowMask, MorphTypes.Close, k3);

        // Imagen en blanco y negro: la tinta aceptada. Es la "imagen × máscara": de aquí salen todas las figuras.
        BuildInkMask(lowMask, highMask, distance, low * HoleDistanceRatio, s, mask);

        // Cada píxel de tinta se clasifica en uno de los 5 colores y cada región de un color es una figura
        using var labels = ClassifyInk(filtered, mask, diff, colorThreshold);
        AddClassShapes(labels, filtered, distance, low * HoleDistanceRatio, s, shapes);

        // Garantía final: una figura no puede tener un hueco donde la imagen blanca y negra no lo tiene
        Mat finalMask = mask;   // copia local: un parámetro "out" no se puede usar dentro de una lambda
        for (int i = 0; i < shapes.Count; i++)
        {
            var kept = shapes[i].Holes.Where(h => IsBlackInMask(finalMask, h)).ToList();
            if (kept.Count != shapes[i].Holes.Count)
                shapes[i] = shapes[i] with { Holes = kept };
        }
        return shapes;
    }

    /// <summary>
    /// Umbral automático para un mapa de distancias al papel: el papel es la mayoría de la imagen, así que la mediana de la distancia es su "nivel"
    /// y la MAD (desviación absoluta mediana, robusta a la tinta) da su ruido: umbral = mediana + 4.5·σ.
    /// (Con papel real: mediana 5, σ≈3 → ~18, igual que el valor que el usuario ajustó a mano.)
    /// </summary>
    private static double EstimateNoiseThreshold(Mat distance, double min, double max)
    {
        using var d8 = new Mat();
        distance.ConvertTo(d8, MatType.CV_8UC1, 4.0); // 4 niveles por unidad de distancia
        d8.GetArray(out byte[] data);
        if (data.Length == 0) return min;

        var hist = new int[256];
        foreach (byte v in data) hist[v]++;
        int median = HistogramMedian(hist, data.Length);

        var dev = new int[256];
        for (int v = 0; v < 256; v++) dev[Math.Abs(v - median)] += hist[v];
        int mad = HistogramMedian(dev, data.Length);

        double sigma = 1.4826 * mad / 4.0;
        return Math.Clamp(median / 4.0 + 4.5 * sigma, min, max);
    }

    private static int HistogramMedian(int[] hist, int total)
    {
        int acc = 0;
        for (int i = 0; i < hist.Length; i++)
        {
            acc += hist[i];
            if (acc * 2 >= total) return i;
        }
        return hist.Length - 1;
    }

    /// <summary>De una diferencia (dL,da,db): distancia total y distancia solo de color (da,db).</summary>
    private static void Magnitudes(Mat diff, out Mat total, out Mat ab)
    {
        Cv2.Split(diff, out Mat[] c);
        ab = new Mat();
        Cv2.Magnitude(c[1], c[2], ab);
        total = new Mat();
        Cv2.Magnitude(ab, c[0], total);
        foreach (var m in c) m.Dispose();
    }

    /// <summary>Distancia euclídea por píxel entre dos imágenes Lab de 3 canales (CV_32F).</summary>
    private static Mat Distance(Mat a, Mat b)
    {
        using var diff = new Mat();
        Cv2.Subtract(a, b, diff);
        Cv2.Split(diff, out Mat[] c);
        using var m01 = new Mat();
        Cv2.Magnitude(c[0], c[1], m01);
        var result = new Mat();
        Cv2.Magnitude(m01, c[2], result);
        foreach (var m in c) m.Dispose();
        return result;
    }

    private static Mat Binarize(Mat float32, double threshold)
    {
        using var t = new Mat();
        Cv2.Threshold(float32, t, threshold, 255, ThresholdTypes.Binary);
        var result = new Mat();
        t.ConvertTo(result, MatType.CV_8UC1);
        return result;
    }

    /// <summary>
    /// Estima el color del papel en cada punto (CV_32FC3, tamaño completo): mediana grande a 1/4 de resolución
    /// y, después, media ponderada que ignora los píxeles que ya se ven como tinta.
    /// </summary>
    private static Mat EstimatePaper(Mat lab8)
    {
        using var small8 = new Mat();
        Cv2.Resize(lab8, small8, new OpenCvSharp.Size(), 0.25, 0.25, InterpolationFlags.Area);
        using var small = new Mat();
        small8.ConvertTo(small, MatType.CV_32FC3);

        Cv2.Split(small8, out Mat[] planes);
        foreach (var p in planes) Cv2.MedianBlur(p, p, 41);
        using var median8 = new Mat();
        Cv2.Merge(planes, median8);
        foreach (var p in planes) p.Dispose();

        var paper = new Mat();
        median8.ConvertTo(paper, MatType.CV_32FC3);

        using var k5 = Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(5, 5));
        for (int iteration = 0; iteration < 2; iteration++)
        {
            using var dist = Distance(small, paper);
            Cv2.GaussianBlur(dist, dist, new OpenCvSharp.Size(0, 0), 1.0);
            using var isInk = Binarize(dist, 10);
            Cv2.Dilate(isInk, isInk, k5);

            using var notInk8 = new Mat();
            Cv2.BitwiseNot(isInk, notInk8);
            using var weight = new Mat();
            notInk8.ConvertTo(weight, MatType.CV_32F, 1.0 / 255.0);

            var next = NormalizedBlur(small, weight, 12.0);
            paper.Dispose();
            paper = next;
        }

        var full = new Mat();
        Cv2.Resize(paper, full, lab8.Size(), 0, 0, InterpolationFlags.Linear);
        paper.Dispose();
        return full;
    }

    /// <summary>Media gaussiana de la imagen usando solo los píxeles con peso (convolución normalizada).</summary>
    private static Mat NormalizedBlur(Mat image3, Mat weight, double sigma)
    {
        using var den = new Mat();
        Cv2.GaussianBlur(weight, den, new OpenCvSharp.Size(0, 0), sigma);
        Cv2.Add(den, Scalar.All(1e-3), den);

        Cv2.Split(image3, out Mat[] c);
        for (int i = 0; i < 3; i++)
        {
            Cv2.Multiply(c[i], weight, c[i]);
            Cv2.GaussianBlur(c[i], c[i], new OpenCvSharp.Size(0, 0), sigma);
            Cv2.Divide(c[i], den, c[i]);
        }
        var result = new Mat();
        Cv2.Merge(c, result);
        foreach (var m in c) m.Dispose();
        return result;
    }

    /// <summary>
    /// Imagen en blanco y negro de la tinta aceptada: cada componente conectada (con algún píxel del umbral alto y tamaño suficiente),
    /// rellena, menos sus huecos reales (papel visto a través de la figura).
    /// </summary>
    private static void BuildInkMask(Mat lowMask, Mat highMask, Mat distance, double holeLimit, DetectionSettings s, Mat displayMask)
    {
        Cv2.FindContours(lowMask, out OpenCvSharp.Point[][] contours, out HierarchyIndex[] hierarchy,
            RetrievalModes.CComp, ContourApproximationModes.ApproxSimple);

        for (int i = 0; i < contours.Length; i++)
        {
            if (hierarchy[i].Parent != -1) continue; // los huecos se recogen desde su contorno padre

            // En trazos finos el área es pequeña, así que también se usa perímetro/2
            double size = Math.Max(Cv2.ContourArea(contours[i]), Cv2.ArcLength(contours[i], true) / 2);
            if (size < s.MinSize) continue;
            if (!HasSeed(highMask, contours[i])) continue;

            Cv2.FillPoly(displayMask, new[] { contours[i] }, Scalar.All(255));
            for (int h = hierarchy[i].Child; h != -1; h = hierarchy[h].Next)
                if (Cv2.ContourArea(contours[h]) >= Math.Max(s.MinSize, 80) && IsPaperHole(distance, contours[h], holeLimit))
                    Cv2.FillPoly(displayMask, new[] { contours[h] }, Scalar.All(0)); // huecos diminutos o con tinta floja dentro = ruido, no hueco
        }
    }

    /// <summary>
    /// Convierte un contorno en la figura final. Con InferShapes, una mancha sólida (casi convexa y no un trazo)
    /// se sustituye por su figura probable: rectángulo girado si lo rellena casi del todo, si no su envolvente
    /// convexa simplificada; los huecos pequeños (ruido interior) se ignoran. Los trazos (letras) quedan como están.
    /// </summary>
    private static DetectedShape? BuildShape(OpenCvSharp.Point[] contour, List<OpenCvSharp.Point[]> holeContours,
                                             InkColor ink, Scalar color, DetectionSettings s)
    {
        double area = Cv2.ContourArea(contour);

        // Huecos reales: no diminutos respecto a la figura
        var bigHoles = holeContours.Where(hc => Cv2.ContourArea(hc) >= 0.08 * area).ToList();

        if (s.InferShapes && area >= 400)
        {
            var hull = Cv2.ConvexHull(contour);
            double hullArea = Cv2.ContourArea(hull);
            double solidity = hullArea > 0 ? area / hullArea : 0;

            if (solidity >= 0.88 && bigHoles.Count == 0)
            {
                var box = Cv2.MinAreaRect(contour);
                double boxArea = (double)box.Size.Width * box.Size.Height;
                OpenCvSharp.Point[] outline;
                if (boxArea > 0 && area / boxArea >= 0.85)
                    outline = box.Points().Select(p => new OpenCvSharp.Point((int)Math.Round(p.X), (int)Math.Round(p.Y))).ToArray();
                else
                    outline = Cv2.ApproxPolyDP(hull, Math.Max(s.Epsilon, 3) * 1.5, true);

                if (outline.Length >= 3)
                    return new DetectedShape(outline, new List<OpenCvSharp.Point[]>(), ink, color);
            }
        }

        var raw = Cv2.ApproxPolyDP(contour, s.Epsilon, true);
        if (raw.Length < 3) return null;

        var holes = new List<OpenCvSharp.Point[]>();
        foreach (var hc in bigHoles)
        {
            var hole = Cv2.ApproxPolyDP(hc, s.Epsilon, true);
            if (hole.Length >= 3) holes.Add(hole);
        }
        return new DetectedShape(raw, holes, ink, color);
    }

    /// <summary>true si el interior del polígono es (casi todo) negro en la máscara.</summary>
    private static bool IsBlackInMask(Mat mask, OpenCvSharp.Point[] polygon)
    {
        var rect = Cv2.BoundingRect(polygon).Intersect(new Rect(0, 0, mask.Width, mask.Height));
        if (rect.Width <= 0 || rect.Height <= 0) return false;

        var shift = new OpenCvSharp.Point(-rect.X, -rect.Y);
        using var inside = new Mat(rect.Size, MatType.CV_8UC1, Scalar.All(0));
        Cv2.FillPoly(inside, new[] { polygon.Select(p => p + shift).ToArray() }, Scalar.All(255));
        using var roi = new Mat(mask, rect);
        return Cv2.Mean(roi, inside).Val0 < 51;   // < 20 % de blanco
    }

    private static bool IsPaperHole(Mat distance, OpenCvSharp.Point[] hole, double limit)
    {
        var rect = Cv2.BoundingRect(hole).Intersect(new Rect(0, 0, distance.Width, distance.Height));
        if (rect.Width <= 0 || rect.Height <= 0) return false;

        var shift = new OpenCvSharp.Point(-rect.X, -rect.Y);
        using var mask = new Mat(rect.Size, MatType.CV_8UC1, Scalar.All(0));
        Cv2.FillPoly(mask, new[] { hole.Select(p => p + shift).ToArray() }, Scalar.All(255));
        using var roi = new Mat(distance, rect);
        return Cv2.Mean(roi, mask).Val0 < limit;
    }

    private static bool HasSeed(Mat highMask, OpenCvSharp.Point[] contour)
    {
        var rect = Cv2.BoundingRect(contour).Intersect(new Rect(0, 0, highMask.Width, highMask.Height));
        if (rect.Width <= 0 || rect.Height <= 0) return false;

        var shift = new OpenCvSharp.Point(-rect.X, -rect.Y);
        using var roi = new Mat(rect.Size, MatType.CV_8UC1, Scalar.All(0));
        Cv2.FillPoly(roi, new[] { contour.Select(p => p + shift).ToArray() }, Scalar.All(255));
        using var seeds = new Mat(highMask, rect);
        Cv2.BitwiseAnd(roi, seeds, roi);
        return Cv2.CountNonZero(roi) > 0;
    }

    /// <summary>Negro si el color apenas se diferencia del papel en (a,b); si no, familia según el matiz.</summary>
    private static InkColor Classify(Scalar bgr, double colorDiff, double colorThreshold)
    {
        if (colorDiff < colorThreshold) return InkColor.Black;

        using var px = new Mat(1, 1, MatType.CV_8UC3, new Scalar(bgr.Val0, bgr.Val1, bgr.Val2));
        using var hsv = new Mat();
        Cv2.CvtColor(px, hsv, ColorConversionCodes.BGR2HSV);
        return ClassOfHue(hsv.At<Vec3b>(0, 0).Item0);
    }

    /// <summary>Máscara (tamaño del rectángulo del contorno) de la forma ∩ classMask: contorno relleno menos huecos.</summary>
    private static Mat? RoiMask(Mat classMask, Rect rect, OpenCvSharp.Point[] outline, List<OpenCvSharp.Point[]> holes)
    {
        if (rect.Width <= 0 || rect.Height <= 0) return null;

        var shift = new OpenCvSharp.Point(-rect.X, -rect.Y);
        OpenCvSharp.Point[] Shift(OpenCvSharp.Point[] pts) => pts.Select(p => p + shift).ToArray();

        var roiMask = new Mat(rect.Size, MatType.CV_8UC1, Scalar.All(0));
        Cv2.FillPoly(roiMask, new[] { Shift(outline) }, Scalar.All(255));
        foreach (var hole in holes)
            Cv2.FillPoly(roiMask, new[] { Shift(hole) }, Scalar.All(0));

        using var classRoi = new Mat(classMask, rect);
        Cv2.BitwiseAnd(roiMask, classRoi, roiMask);
        return roiMask;
    }

    /// <summary>Media de la imagen en los píxeles de la forma que pertenecen a la máscara.</summary>
    private static Scalar MeanColor(Mat image, Mat classMask, OpenCvSharp.Point[] outline, List<OpenCvSharp.Point[]> holes)
    {
        var rect = Cv2.BoundingRect(outline).Intersect(new Rect(0, 0, image.Width, image.Height));
        using var roiMask = RoiMask(classMask, rect, outline, holes);
        if (roiMask == null || Cv2.CountNonZero(roiMask) == 0) return Scalar.All(128);

        using var imageRoi = new Mat(image, rect);
        return Cv2.Mean(imageRoi, roiMask);
    }

    // ------------------------------------------------------------------ Color por píxel

    private const double ColorRatio = 0.45;   // parte mínima del vector de diferencia con el papel que debe ser color (y no solo oscuridad)
    private const double ModeSigma = 3.0;     // radio del filtro de moda que limpia la etiqueta de color
    private const int ThinOpenSize = 25;      // lo más fino que esto, pegado a otro color, es borde/sombra de ese color
    private const double ThinContact = 0.25;  // fracción del perímetro en contacto con otro color para considerarlo borde suyo

    /// <summary>
    /// Imagen × máscara: cada píxel de tinta se clasifica en uno de los 5 colores y no la figura entera.
    /// Un píxel es "de color" si su color (a,b) se aleja claramente del papel, o si la DIRECCIÓN de su diferencia con el papel
    /// es sobre todo de color (un borde difuminado de un trazo azul sigue siendo azul aunque sea más claro).
    /// Después se limpia con un filtro de moda y los bordes finos pegados a otro color (el lado oscuro de un lápiz azul,
    /// la sombra de un rotulador) se asignan a ese color. Devuelve CV_8UC1: 0..4 = InkColor, 255 = no es tinta. El llamador la libera.
    /// </summary>
    private static Mat ClassifyInk(Mat image, Mat inkMask, Mat diff, double colorThreshold)
    {
        int w = image.Width, h = image.Height;
        double strongChroma = 2.5 * colorThreshold;   // con tanto color ya no hay duda
        double minChroma = 1.7 * colorThreshold;      // mínimo para el criterio por dirección

        using var diffSmooth = new Mat();
        Cv2.GaussianBlur(diff, diffSmooth, new OpenCvSharp.Size(0, 0), 1.0);
        diffSmooth.GetArray(out Vec3f[] d);

        using var smooth = new Mat();
        Cv2.GaussianBlur(image, smooth, new OpenCvSharp.Size(0, 0), 1.0);
        using var hsvMat = new Mat();
        Cv2.CvtColor(smooth, hsvMat, ColorConversionCodes.BGR2HSV);
        hsvMat.GetArray(out Vec3b[] hsv);

        inkMask.GetArray(out byte[] ink);
        var data = new byte[w * h];
        for (int i = 0; i < data.Length; i++)
        {
            if (ink[i] == 0) { data[i] = 255; continue; }
            double dl = d[i].Item0 / 2.55;   // la L de OpenCV va de 0 a 255
            double chroma = Math.Sqrt((double)d[i].Item1 * d[i].Item1 + (double)d[i].Item2 * d[i].Item2);
            double total = Math.Sqrt(chroma * chroma + dl * dl) + 1e-3;
            bool isColor = chroma >= strongChroma || (chroma >= minChroma && chroma / total >= ColorRatio);
            data[i] = isColor ? (byte)ClassOfHue(hsv[i].Item0) : (byte)0;
        }

        // Filtro de moda: cada píxel toma la etiqueta más frecuente a su alrededor
        var bestScore = new float[w * h];
        var bestLabel = new byte[w * h];
        var plane = new float[w * h];
        for (int k = 0; k < 5; k++)
        {
            for (int i = 0; i < plane.Length; i++) plane[i] = data[i] == k ? 1f : 0f;
            using var m = new Mat(h, w, MatType.CV_32FC1);
            m.SetArray(plane);
            Cv2.GaussianBlur(m, m, new OpenCvSharp.Size(0, 0), ModeSigma);
            m.GetArray(out float[] blurred);
            for (int i = 0; i < blurred.Length; i++)
                if (blurred[i] > bestScore[i]) { bestScore[i] = blurred[i]; bestLabel[i] = (byte)k; }
        }
        for (int i = 0; i < data.Length; i++)
            if (data[i] != 255) data[i] = bestLabel[i];

        var labels = new Mat(h, w, MatType.CV_8UC1);
        labels.SetArray(data);

        // Bordes finos pegados a otro color: se quedan con ese color
        using var kOpen = Cv2.GetStructuringElement(MorphShapes.Ellipse, new OpenCvSharp.Size(ThinOpenSize, ThinOpenSize));
        using var k3 = Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(3, 3));
        var bounds = new Rect(0, 0, w, h);
        for (int pass = 0; pass < 2; pass++)
        {
            for (int c = 0; c < 5; c++)
            {
                using var classMask = new Mat();
                Cv2.Compare(labels, Scalar.All(c), classMask, CmpType.EQ);
                if (Cv2.CountNonZero(classMask) == 0) continue;

                using var body = new Mat();
                Cv2.MorphologyEx(classMask, body, MorphTypes.Open, kOpen);
                using var thin = new Mat();
                Cv2.Subtract(classMask, body, thin);   // lo que no cabe en el "cuerpo" del color: partes finas

                using var comp = new Mat();
                using var stats = new Mat();
                using var centroids = new Mat();
                int n = Cv2.ConnectedComponentsWithStats(thin, comp, stats, centroids, PixelConnectivity.Connectivity8);
                for (int i = 1; i < n; i++)
                {
                    if (stats.At<int>(i, 4) < 30) continue;   // 4 = área
                    var box = new Rect(stats.At<int>(i, 0) - 2, stats.At<int>(i, 1) - 2,
                                       stats.At<int>(i, 2) + 4, stats.At<int>(i, 3) + 4).Intersect(bounds);
                    if (box.Width <= 0 || box.Height <= 0) continue;

                    using var compRoi = new Mat(comp, box);
                    using var labelsRoi = new Mat(labels, box);
                    using var piece = new Mat();
                    Cv2.Compare(compRoi, Scalar.All(i), piece, CmpType.EQ);
                    using var ring = new Mat();
                    Cv2.Dilate(piece, ring, k3);
                    Cv2.Subtract(ring, piece, ring);
                    int ringCount = Cv2.CountNonZero(ring);
                    if (ringCount == 0) continue;

                    int bestColor = -1, bestCount = 0, contact = 0;
                    for (int k = 0; k < 5; k++)
                    {
                        if (k == c) continue;
                        using var other = new Mat();
                        Cv2.Compare(labelsRoi, Scalar.All(k), other, CmpType.EQ);
                        Cv2.BitwiseAnd(other, ring, other);
                        int count = Cv2.CountNonZero(other);
                        contact += count;
                        if (count > bestCount) { bestCount = count; bestColor = k; }
                    }
                    if (bestColor >= 0 && contact >= ThinContact * ringCount)
                        labelsRoi.SetTo(Scalar.All(bestColor), piece);
                }
            }
        }
        return labels;
    }

    /// <summary>Cada región conectada de un color es una figura.</summary>
    private static void AddClassShapes(Mat labels, Mat image, Mat distance, double holeLimit, DetectionSettings s, List<DetectedShape> result)
    {
        for (int k = 0; k < 5; k++)
        {
            using var classMask = new Mat();
            Cv2.Compare(labels, Scalar.All(k), classMask, CmpType.EQ);
            if (Cv2.CountNonZero(classMask) == 0) continue;
            AddPolygonsFromMask(classMask, (InkColor)k, image, distance, holeLimit, s, result);
        }
    }

    /// <summary>Convierte una máscara de un color (tamaño de la imagen) en figuras; el color de cada una es la media de sus píxeles.</summary>
    private static void AddPolygonsFromMask(Mat mask, InkColor ink, Mat image, Mat distance, double holeLimit,
                                            DetectionSettings s, List<DetectedShape> result)
    {
        using var work = mask.Clone();
        Cv2.FindContours(work, out OpenCvSharp.Point[][] contours, out HierarchyIndex[] hierarchy,
            RetrievalModes.CComp, ContourApproximationModes.ApproxSimple);

        for (int i = 0; i < contours.Length; i++)
        {
            if (hierarchy[i].Parent != -1) continue;
            double size = Math.Max(Cv2.ContourArea(contours[i]), Cv2.ArcLength(contours[i], true) / 2);
            if (size < s.MinSize) continue;

            var holeContours = new List<OpenCvSharp.Point[]>();
            for (int hIdx = hierarchy[i].Child; hIdx != -1; hIdx = hierarchy[hIdx].Next)
                if (Cv2.ContourArea(contours[hIdx]) >= Math.Max(s.MinSize, 80) && IsPaperHole(distance, contours[hIdx], holeLimit))
                    holeContours.Add(contours[hIdx]);

            Scalar color = MeanColor(image, mask, contours[i], holeContours);
            var shape = BuildShape(contours[i], holeContours, ink, color, s);
            if (shape != null) result.Add(shape);
        }
    }

    /// <summary>Dibuja las formas sobre el fotograma, cada una en su color, con contorno y vértices.</summary>
    public static void Paint(Mat frame, List<DetectedShape> shapes)
    {
        if (shapes.Count == 0) return;

        // Relleno translúcido en el color detectado (los huecos quedan libres)
        using var overlay = frame.Clone();
        foreach (var shape in shapes)
            Cv2.FillPoly(overlay, Rings(shape), shape.Color);
        Cv2.AddWeighted(overlay, 0.7, frame, 0.3, 0, frame);

        foreach (var shape in shapes)
        {
            var rings = Rings(shape);
            var edge = new Scalar(shape.Color.Val0 * 0.6, shape.Color.Val1 * 0.6, shape.Color.Val2 * 0.6);
            Cv2.Polylines(frame, rings, true, edge, 2);

            foreach (var ring in rings)
                foreach (var p in ring)
                {
                    Cv2.Circle(frame, p, 3, new Scalar(255, 255, 255), -1);
                    Cv2.Circle(frame, p, 3, new Scalar(0, 0, 0), 1);
                }
        }
    }

    private static List<OpenCvSharp.Point[]> Rings(DetectedShape shape)
    {
        var rings = new List<OpenCvSharp.Point[]> { shape.Outline };
        rings.AddRange(shape.Holes);
        return rings;
    }
}
