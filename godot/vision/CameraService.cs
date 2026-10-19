using System.Runtime.InteropServices;
using OpenCvSharp;

namespace DrawingGame.Vision;

/// <summary>
/// Lee la webcam en un hilo propio, endereza el área elegida, detecta y estabiliza las formas
/// y deja los resultados listos para que el hilo principal de Godot los recoja.
/// No toca ningún objeto de Godot.
/// </summary>
public sealed class CameraService : IDisposable
{
    private const int MinSize = 150;   // tamaño mínimo de una figura (px de la imagen enderezada de 900 de ancho): ignora polvo y motas

    // Parámetros (se pueden cambiar en caliente desde el hilo principal)
    public volatile int Simplify = 2;
    public volatile int Stability = 4;
    public volatile int Sensitivity = 0;         // sensibilidad manual (0 = automática)
    public volatile int ColorMin = 0;            // diferencia de color mínima manual (0 = automática)
    public volatile int MinThickness = 7;        // grosor mínimo de un trazo (px de la imagen enderezada): lo más fino se ignora (arañazos, polvo)
    public volatile bool ShowDetection = true;   // dibujar (o no) las formas detectadas sobre la imagen

    public string? Error { get; private set; }
    public int FrameWidth => _frameW;
    public int FrameHeight => _frameH;

    private Thread? _thread;
    private volatile bool _running;
    private volatile int _frameW;
    private volatile int _frameH;

    private readonly object _lock = new();
    private Point2f[]? _area;                       // 4 esquinas ordenadas, o null = imagen completa
    private bool _calibrating;
    private bool _resetStabilizer;
    private Point2f[] _calibPoints = Array.Empty<Point2f>();

    private byte[]? _preview;                        // última imagen RGB
    private int _previewW, _previewH;
    private bool _previewNew;

    private int[] _erasedIds = Array.Empty<int>();   // figuras (por Id) cuyo dibujo se borra de la imagen mostrada

    private List<DetectedShape> _shapes = new();
    private int _shapesW, _shapesH;
    private long _version;

    // ------------------------------------------------------------ API (hilo principal)

    /// <summary>
    /// Arranca con una fuente de imágenes (webcam, móvil...). El servicio no la cierra: lo hace quien la creó, tras llamar a Stop().
    /// Si ya está en marcha, no hace nada.
    /// </summary>
    public void Start(ICameraSource source)
    {
        if (_running && _thread?.IsAlive == true) return;
        Error = null;
        _running = true;
        _thread = new Thread(() => Loop(source)) { IsBackground = true, Name = "CameraService" };
        _thread.Start();
    }

    public void Stop()
    {
        _running = false;
        _thread?.Join(2000);
        _thread = null;
    }

    /// <summary>Esquinas del área (cualquier orden) o null para usar la imagen completa.</summary>
    public void SetArea(Point2f[]? corners)
    {
        lock (_lock)
        {
            _area = corners == null ? null : AreaWarp.Order(corners);
            _resetStabilizer = true;
        }
    }

    /// <summary>Modo calibración: muestra la imagen completa con las esquinas dibujadas y no detecta.</summary>
    public void SetCalibration(bool on, Point2f[] points)
    {
        lock (_lock)
        {
            _calibrating = on;
            _calibPoints = points;
        }
    }

    /// <summary>Figuras que se borran de la imagen mostrada (se rellena con el tono del papel de alrededor). No afecta a la detección.</summary>
    public void SetErasedIds(int[] ids)
    {
        lock (_lock) _erasedIds = ids;
    }

    public bool TryGetPreview(out byte[] data, out int width, out int height)
    {
        lock (_lock)
        {
            if (_preview == null || !_previewNew)
            {
                data = Array.Empty<byte>();
                width = height = 0;
                return false;
            }
            data = _preview;
            width = _previewW;
            height = _previewH;
            _previewNew = false;
            return true;
        }
    }

    /// <summary>Devuelve las formas si han cambiado desde 'lastVersion'.</summary>
    public bool TryGetShapes(ref long lastVersion, out List<DetectedShape> shapes, out int width, out int height)
    {
        lock (_lock)
        {
            if (_version == lastVersion)
            {
                shapes = _shapes;
                width = height = 0;
                return false;
            }
            lastVersion = _version;
            shapes = _shapes;
            width = _shapesW;
            height = _shapesH;
            return true;
        }
    }

    public void Dispose() => Stop();

    // ------------------------------------------------------------ Hilo de captura

    private void Loop(ICameraSource source)
    {
        try
        {

            using var frame = new Mat();
            using var filter = new ImageFilter();
            var stabilizer = new ShapeStabilizer();
            // Valores de filtro validados en el prototipo
            var filters = new FilterSettings(TemporalPct: 0, SpatialLevel: 10, WhiteBalance: true,
                                             IlluminationPct: 0, ClaheLevel: 4, ColorBoostPct: 100);
            bool lastFlat = false;

            while (_running)
            {
                // Espera la siguiente imagen de la fuente (sin ella, no hay nada que procesar)
                bool got = source.TryGetFrame(frame, 200);
                if (source.Error != null)
                {
                    Error = source.Error;
                    return;
                }
                if (!got || frame.Empty()) continue;
                _frameW = frame.Width;
                _frameH = frame.Height;

                Point2f[]? area;
                bool calibrating;
                Point2f[] points;
                lock (_lock)
                {
                    if (_resetStabilizer)
                    {
                        stabilizer.Reset();
                        _resetStabilizer = false;
                    }
                    area = _area;
                    calibrating = _calibrating;
                    points = _calibPoints;
                }

                Mat? flat = null;
                try
                {
                    Mat work = frame;

                    if (calibrating)
                    {
                        DrawCalibration(frame, points);
                        stabilizer.Reset();
                    }
                    else
                    {
                        if (area != null)
                        {
                            flat = AreaWarp.Warp(frame, area);
                            work = flat;
                        }

                        bool isFlat = area != null;
                        if (isFlat != lastFlat)
                        {
                            stabilizer.Reset();
                            filter.Reset();
                            lastFlat = isFlat;
                        }

                        // Imagen filtrada -> detector (máscara + color por píxel + figuras) -> estabilizador (con Id)
                        using var filtered = filter.Apply(work, filters);
                        int sens = Sensitivity, colorMin = ColorMin;
                        var settings = new DetectionSettings(
                            InkDifference: sens > 0 ? sens : 19, ColorDifference: colorMin > 0 ? colorMin : 7,
                            MinSize: MinSize, Epsilon: Simplify,
                            AutoSensitivity: sens == 0, AutoColor: colorMin == 0, InferShapes: true,
                            MinThickness: MinThickness);
                        var detected = DrawingDetector.Detect(filtered, settings, out Mat mask, out _, out _);
                        mask.Dispose();
                        var shapes = stabilizer.Update(detected, Math.Max(1, Stability));   // con 0 no habría Ids estables

                        lock (_lock)
                        {
                            if (!SameShapes(_shapes, shapes))
                            {
                                _shapes = shapes;
                                _shapesW = work.Width;
                                _shapesH = work.Height;
                                _version++;
                            }
                        }

                        // Solo en la imagen mostrada (la detección ya se hizo): borrar lo que el juego da por recogido
                        int[] erased;
                        lock (_lock) erased = _erasedIds;
                        var visible = shapes;
                        if (erased.Length > 0)
                        {
                            foreach (var shape in shapes)
                                if (erased.Contains(shape.Id)) Erase(work, shape);
                            visible = shapes.Where(shape => !erased.Contains(shape.Id)).ToList();
                        }

                        if (ShowDetection)
                            DrawingDetector.Paint(work, visible); // figuras con el color de su familia
                    }

                    PublishPreview(work);
                }
                finally
                {
                    flat?.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            Error = "Error en la cámara: " + ex.Message;
        }
        finally
        {
            _running = false;
        }
    }

    /// <summary>Rellena la zona de la figura con el tono del papel que la rodea (inpainting en una ventana pequeña).</summary>
    private static void Erase(Mat image, DetectedShape shape)
    {
        var box = Cv2.BoundingRect(shape.Outline);
        box = new Rect(box.X - 20, box.Y - 20, box.Width + 40, box.Height + 40).Intersect(new Rect(0, 0, image.Width, image.Height));
        if (box.Width <= 0 || box.Height <= 0) return;

        using var roi = new Mat(image, box);
        using var mask = new Mat(box.Size, MatType.CV_8UC1, Scalar.All(0));
        var shift = new OpenCvSharp.Point(-box.X, -box.Y);
        Cv2.FillPoly(mask, new[] { shape.Outline.Select(p => p + shift).ToArray() }, Scalar.All(255));
        using var grow = Cv2.GetStructuringElement(MorphShapes.Ellipse, new OpenCvSharp.Size(9, 9));
        Cv2.Dilate(mask, mask, grow);   // incluye el borde difuminado del trazo

        using var result = new Mat();
        Cv2.Inpaint(roi, mask, result, 5, InpaintMethod.Telea);
        result.CopyTo(roi);
    }

    private void PublishPreview(Mat bgr)
    {
        using var rgb = new Mat();
        Cv2.CvtColor(bgr, rgb, ColorConversionCodes.BGR2RGB);
        int len = (int)(rgb.Total() * rgb.ElemSize());
        var bytes = new byte[len];
        Marshal.Copy(rgb.Data, bytes, 0, len);

        lock (_lock)
        {
            _preview = bytes;
            _previewW = rgb.Width;
            _previewH = rgb.Height;
            _previewNew = true;
        }
    }

    /// <summary>Las formas estables son las mismas referencias mientras no hay cambios reales.</summary>
    private static bool SameShapes(List<DetectedShape> a, List<DetectedShape> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
            if (!ReferenceEquals(a[i], b[i])) return false;
        return true;
    }

    private static void DrawCalibration(Mat image, Point2f[] points)
    {
        var pts = points.Length == 4 ? AreaWarp.Order(points) : points;
        var poly = pts.Select(p => new OpenCvSharp.Point((int)p.X, (int)p.Y)).ToArray();

        if (poly.Length >= 2)
            Cv2.Polylines(image, new[] { poly }, poly.Length == 4, new Scalar(0, 255, 255), 2);
        foreach (var p in poly)
        {
            Cv2.Circle(image, p, 8, new Scalar(0, 255, 255), -1);
            Cv2.Circle(image, p, 8, new Scalar(0, 0, 0), 1);
        }
    }
}
