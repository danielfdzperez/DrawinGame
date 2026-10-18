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
    private const int MinSize = 20;

    // Parámetros (se pueden cambiar en caliente desde el hilo principal)
    public volatile int Threshold = 100;
    public volatile int Contrast = 40;
    public volatile int Simplify = 3;
    public volatile int Stability = 4;
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

    private List<DetectedShape> _shapes = new();
    private int _shapesW, _shapesH;
    private long _version;

    // ------------------------------------------------------------ API (hilo principal)

    public void Start(int cameraIndex)
    {
        if (_running && _thread?.IsAlive == true) return;
        Error = null;
        _running = true;
        _thread = new Thread(() => Loop(cameraIndex)) { IsBackground = true, Name = "CameraService" };
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

    private void Loop(int cameraIndex)
    {
        try
        {
            using var cap = new VideoCapture(cameraIndex, VideoCaptureAPIs.DSHOW);
            if (!cap.IsOpened())
            {
                Error = $"No se pudo abrir la cámara {cameraIndex}.";
                return;
            }

            using var frame = new Mat();
            var stabilizer = new ShapeStabilizer();
            bool lastFlat = false;

            while (_running)
            {
                if (!cap.Read(frame) || frame.Empty())
                {
                    Thread.Sleep(10);
                    continue;
                }
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
                            lastFlat = isFlat;
                        }

                        var detected = DrawingDetector.Detect(work, Threshold, Contrast, MinSize, Simplify);
                        var shapes = stabilizer.Update(detected, Stability);

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

                        if (ShowDetection)
                            DrawingDetector.Paint(work, shapes); // superposición verde de depuración
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
