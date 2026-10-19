using OpenCvSharp;
using OpenCvSharp.Extensions;

namespace DrawinGamePrototype;

public class MainForm : Form
{
    private const int MaxCamerasToProbe = 10;
    private const int MinSize = 150;
    private const double GrabRadiusPx = 15; // radio (en píxeles de pantalla) para agarrar una esquina

    // ---- Barra superior
    private readonly ComboBox _cameraCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
    private readonly Button _refreshButton = new() { Text = "Buscar cámaras", AutoSize = true };
    private readonly Button _toggleButton = new() { Text = "Iniciar", AutoSize = true, Enabled = false };
    private readonly Button _areaButton = new() { Text = "Seleccionar área", AutoSize = true };
    private readonly Button _clearAreaButton = new() { Text = "Borrar área", AutoSize = true };
    private readonly CheckBox _detectCheck = new() { Text = "Detectar dibujo", AutoSize = true, Checked = true };
    private readonly Label _statusLabel = new() { AutoSize = true, Text = "Buscando cámaras...", Padding = new Padding(0, 6, 0, 0) };

    // ---- Las dos vistas
    private readonly PictureBox _picFilter = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Black };
    private readonly PictureBox _picMask = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Black };
    private readonly PictureBox _picUser = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Black };

    // ---- Ajustes (se leen desde el hilo de captura)
    private volatile bool _detectEnabled = true;
    private volatile FilterSettings _filters = new(TemporalPct: 0, SpatialLevel: 10, WhiteBalance: true,
                                                   IlluminationPct: 0, ClaheLevel: 4, ColorBoostPct: 100);
    private volatile DetectionSettings _detection = new(InkDifference: 19, ColorDifference: 7,
                                                        MinSize: MinSize, Epsilon: 2, AutoSensitivity: true, AutoColor: true, InferShapes: true);
    private volatile int _stability = 4;

    // ---- Área seleccionada por el usuario (esquinas en coordenadas de la imagen de la cámara)
    private readonly object _areaLock = new();
    private readonly List<Point2f> _areaPoints = new();
    private volatile bool _editArea;
    private volatile int _frameW;
    private volatile int _frameH;
    private int _dragIndex = -1;

    private CancellationTokenSource? _cts;
    private Task? _captureTask;
    private string _cameraName = "";

    public MainForm()
    {
        Text = "DrawinGame - Prototipo de cámara";
        ClientSize = new System.Drawing.Size(1500, 760);
        MinimumSize = new System.Drawing.Size(900, 500);

        // Barra superior
        var topBar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8), WrapContents = true };
        topBar.Controls.Add(new Label { Text = "Cámara:", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
        topBar.Controls.Add(_cameraCombo);
        topBar.Controls.Add(_refreshButton);
        topBar.Controls.Add(_toggleButton);
        topBar.Controls.Add(_areaButton);
        topBar.Controls.Add(_clearAreaButton);
        topBar.Controls.Add(_detectCheck);
        topBar.Controls.Add(_statusLabel);

        // Dos pantallas simultáneas, cada una con su título
        var views = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 2 };
        for (int c = 0; c < 3; c++) views.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));
        views.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        views.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        views.Controls.Add(MakeTitle("Lo que ve el algoritmo (imagen filtrada)"), 0, 0);
        views.Controls.Add(MakeTitle("Máscara (blanco = tinta que ve el detector)"), 1, 0);
        views.Controls.Add(MakeTitle("Lo que ve el usuario (imagen normal + figuras)"), 2, 0);
        views.Controls.Add(_picFilter, 0, 1);
        views.Controls.Add(_picMask, 1, 1);
        views.Controls.Add(_picUser, 2, 1);

        // Panel lateral de ajustes
        var side = new Panel { Dock = DockStyle.Right, Width = 300, AutoScroll = true, Padding = new Padding(8) };
        var sideFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false
        };
        side.Controls.Add(sideFlow);
        BuildSettings(sideFlow);

        // El orden importa: lo añadido después se coloca primero en el acoplado
        Controls.Add(views);
        Controls.Add(side);
        Controls.Add(topBar);

        _refreshButton.Click += async (_, _) => await RefreshCamerasAsync();
        _toggleButton.Click += (_, _) => { if (_cts == null) StartCapture(); else StopCapture(); };
        _areaButton.Click += (_, _) => OnAreaButton();
        _clearAreaButton.Click += (_, _) => ClearArea();
        _detectCheck.CheckedChanged += (_, _) => _detectEnabled = _detectCheck.Checked;

        _picUser.MouseDown += OnPictureMouseDown;
        _picUser.MouseMove += OnPictureMouseMove;
        _picUser.MouseUp += (_, _) => _dragIndex = -1;

        Shown += async (_, _) => await RefreshCamerasAsync();
        FormClosing += (_, _) => StopCapture();
    }

    private static Label MakeTitle(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold),
        Padding = new Padding(4, 6, 0, 2)
    };

    // ---------------------------------------------------------------- Panel de ajustes

    private void BuildSettings(FlowLayoutPanel panel)
    {
        AddHeading(panel, "FILTROS (imagen que ve el algoritmo)");

        AddSlider(panel, "Ruido temporal %", 0, 90, _filters.TemporalPct,
            v => _filters = _filters with { TemporalPct = v });
        AddSlider(panel, "Ruido espacial (bilateral)", 0, 10, _filters.SpatialLevel,
            v => _filters = _filters with { SpatialLevel = v });

        var wb = new CheckBox { Text = "Balance de blancos", AutoSize = true, Checked = _filters.WhiteBalance };
        wb.CheckedChanged += (_, _) => _filters = _filters with { WhiteBalance = wb.Checked };
        panel.Controls.Add(wb);

        AddSlider(panel, "Corregir sombras / luz %", 0, 100, _filters.IlluminationPct,
            v => _filters = _filters with { IlluminationPct = v });
        AddSlider(panel, "Contraste local (CLAHE)", 0, 10, _filters.ClaheLevel,
            v => _filters = _filters with { ClaheLevel = v });
        AddSlider(panel, "Realce de color %", 0, 100, _filters.ColorBoostPct,
            v => _filters = _filters with { ColorBoostPct = v });

        AddHeading(panel, "DETECCIÓN");

        var auto = new CheckBox { Text = "Sensibilidad automática", AutoSize = true, Checked = _detection.AutoSensitivity };
        auto.CheckedChanged += (_, _) => _detection = _detection with { AutoSensitivity = auto.Checked };
        panel.Controls.Add(auto);
        AddSlider(panel, "Sensibilidad manual (dif. con el papel)", 4, 60, _detection.InkDifference,
            v => _detection = _detection with { InkDifference = v });
        var infer = new CheckBox { Text = "Inferir formas (rectángulo/convexa)", AutoSize = true, Checked = _detection.InferShapes };
        infer.CheckedChanged += (_, _) => _detection = _detection with { InferShapes = infer.Checked };
        panel.Controls.Add(infer);
        var autoColor = new CheckBox { Text = "Color automático", AutoSize = true, Checked = _detection.AutoColor };
        autoColor.CheckedChanged += (_, _) => _detection = _detection with { AutoColor = autoColor.Checked };
        panel.Controls.Add(autoColor);
        AddSlider(panel, "Color mínimo manual (dif. a,b)", 2, 40, _detection.ColorDifference,
            v => _detection = _detection with { ColorDifference = v });
        AddSlider(panel, "Simplificación (px)", 0, 20, (int)_detection.Epsilon,
            v => _detection = _detection with { Epsilon = v });
        AddSlider(panel, "Estabilidad (px)", 0, 15, _stability,
            v => _stability = v);
    }

    private static void AddHeading(FlowLayoutPanel panel, string text) =>
        panel.Controls.Add(new Label
        {
            Text = text,
            AutoSize = true,
            Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold),
            Padding = new Padding(0, 12, 0, 4)
        });

    private static void AddSlider(FlowLayoutPanel panel, string title, int min, int max, int value, Action<int> onChange)
    {
        var label = new Label { Text = $"{title}: {value}", AutoSize = true };
        var bar = new TrackBar { Minimum = min, Maximum = max, Value = value, TickStyle = TickStyle.None, Width = 270 };
        bar.ValueChanged += (_, _) =>
        {
            label.Text = $"{title}: {bar.Value}";
            onChange(bar.Value);
        };
        panel.Controls.Add(label);
        panel.Controls.Add(bar);
    }

    // ---------------------------------------------------------------- Área (4 puntos)

    private void OnAreaButton()
    {
        if (_cts == null)
        {
            _statusLabel.Text = "Pulsa Iniciar para ver la cámara y marcar el área.";
            return;
        }

        if (!_editArea)
        {
            _editArea = true;
            _areaButton.Text = "Aplicar área";
            return;
        }

        int count;
        lock (_areaLock) count = _areaPoints.Count;
        if (count < 4)
        {
            _statusLabel.Text = $"Faltan esquinas: {count}/4.";
            return;
        }
        _editArea = false;
        _areaButton.Text = "Seleccionar área";
    }

    private void ClearArea()
    {
        lock (_areaLock) _areaPoints.Clear();
        _editArea = false;
        _dragIndex = -1;
        _areaButton.Text = "Seleccionar área";
    }

    /// <summary>Calcula cómo se dibuja la imagen en el PictureBox (modo Zoom: centrada y escalada).</summary>
    private bool TryGetViewTransform(out double scale, out double offX, out double offY)
    {
        scale = offX = offY = 0;
        int iw = _frameW, ih = _frameH;
        if (iw <= 0 || ih <= 0) return false;

        double cw = _picUser.ClientSize.Width, ch = _picUser.ClientSize.Height;
        scale = Math.Min(cw / iw, ch / ih);
        if (scale <= 0) return false;
        offX = (cw - iw * scale) / 2;
        offY = (ch - ih * scale) / 2;
        return true;
    }

    private void OnPictureMouseDown(object? sender, MouseEventArgs e)
    {
        if (!_editArea || e.Button != MouseButtons.Left) return;
        if (!TryGetViewTransform(out double scale, out double offX, out double offY)) return;

        double x = (e.X - offX) / scale;
        double y = (e.Y - offY) / scale;
        if (x < 0 || y < 0 || x >= _frameW || y >= _frameH) return; // fuera de la imagen

        lock (_areaLock)
        {
            int nearest = -1;
            double best = GrabRadiusPx / scale;
            for (int i = 0; i < _areaPoints.Count; i++)
            {
                double d = Math.Sqrt(Math.Pow(_areaPoints[i].X - x, 2) + Math.Pow(_areaPoints[i].Y - y, 2));
                if (d <= best) { best = d; nearest = i; }
            }

            if (nearest >= 0)
            {
                _dragIndex = nearest;
            }
            else if (_areaPoints.Count < 4)
            {
                _areaPoints.Add(new Point2f((float)x, (float)y));
                _dragIndex = _areaPoints.Count - 1;
            }
        }
    }

    private void OnPictureMouseMove(object? sender, MouseEventArgs e)
    {
        if (_dragIndex < 0 || !_editArea) return;
        if (!TryGetViewTransform(out double scale, out double offX, out double offY)) return;

        double x = Math.Clamp((e.X - offX) / scale, 0, _frameW - 1);
        double y = Math.Clamp((e.Y - offY) / scale, 0, _frameH - 1);

        lock (_areaLock)
        {
            if (_dragIndex < _areaPoints.Count)
                _areaPoints[_dragIndex] = new Point2f((float)x, (float)y);
        }
    }

    private static void DrawAreaOverlay(Mat image, Point2f[] points)
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

    private static void DrawBanner(Mat image, string text)
    {
        // Hershey solo admite ASCII: los mensajes del banner van sin acentos
        var pos = new OpenCvSharp.Point(10, 30);
        Cv2.PutText(image, text, pos, HersheyFonts.HersheySimplex, 0.7, new Scalar(0, 0, 0), 4);
        Cv2.PutText(image, text, pos, HersheyFonts.HersheySimplex, 0.7, new Scalar(0, 255, 255), 2);
    }

    // ---------------------------------------------------------------- Cámaras

    private async Task RefreshCamerasAsync()
    {
        StopCapture();
        _refreshButton.Enabled = false;
        _toggleButton.Enabled = false;
        _statusLabel.Text = "Buscando cámaras...";

        var found = await Task.Run(() =>
        {
            var list = new List<int>();
            for (int i = 0; i < MaxCamerasToProbe; i++)
            {
                try
                {
                    using var cap = new VideoCapture(i, VideoCaptureAPIs.DSHOW);
                    if (cap.IsOpened()) list.Add(i);
                }
                catch { /* índice no válido */ }
            }
            return list;
        });

        _cameraCombo.Items.Clear();
        foreach (var idx in found)
            _cameraCombo.Items.Add(new CameraItem(idx));

        if (_cameraCombo.Items.Count > 0)
        {
            _cameraCombo.SelectedIndex = 0;
            _toggleButton.Enabled = true;
            _statusLabel.Text = $"{found.Count} cámara(s) encontrada(s).";
        }
        else
        {
            _statusLabel.Text = "No se encontró ninguna cámara.";
        }
        _refreshButton.Enabled = true;
    }

    private void StartCapture()
    {
        if (_cameraCombo.SelectedItem is not CameraItem camera) return;

        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        int index = camera.Index;
        _cameraName = camera.ToString();

        _toggleButton.Text = "Detener";
        _cameraCombo.Enabled = false;
        _refreshButton.Enabled = false;
        _statusLabel.Text = $"Leyendo {_cameraName}...";

        _captureTask = Task.Run(() => CaptureLoop(index, token), token);
    }

    private void StopCapture()
    {
        if (_cts == null) return;

        _cts.Cancel();
        try { _captureTask?.Wait(2000); } catch { /* cancelado */ }
        _cts.Dispose();
        _cts = null;
        _captureTask = null;

        _toggleButton.Text = "Iniciar";
        _cameraCombo.Enabled = true;
        _refreshButton.Enabled = true;
        _editArea = false;
        _dragIndex = -1;
        _areaButton.Text = "Seleccionar área";
        SetImages(null, null, null);
    }

    // ---------------------------------------------------------------- Bucle de captura

    private void CaptureLoop(int index, CancellationToken token)
    {
        try
        {
            using var cap = new VideoCapture(index, VideoCaptureAPIs.DSHOW);
            if (!cap.IsOpened())
            {
                BeginInvokeSafe(() => _statusLabel.Text = "No se pudo abrir la cámara.");
                return;
            }

            using var frame = new Mat();
            using var filter = new ImageFilter();
            var stabilizer = new ShapeStabilizer();
            string lastInfo = "";
            bool lastFlat = false;

            while (!token.IsCancellationRequested)
            {
                if (!cap.Read(frame) || frame.Empty())
                {
                    Thread.Sleep(10);
                    continue;
                }
                _frameW = frame.Width;
                _frameH = frame.Height;

                Point2f[] points;
                lock (_areaLock) points = _areaPoints.ToArray();

                bool edit = _editArea;
                Mat? flat = null;
                Mat? mask = null;
                string? banner = null;

                if (!edit && points.Length == 4)
                    flat = AreaWarp.Warp(frame, AreaWarp.Order(points)); // área aplicada: se endereza y se ignora el resto

                try
                {
                    Mat work = flat ?? frame;   // imagen normal (la que verá el usuario)
                    bool areaApplied = flat != null;

                    // Al cambiar entre imagen completa y área aplanada, cambian las coordenadas
                    if (areaApplied != lastFlat)
                    {
                        stabilizer.Reset();
                        filter.Reset();
                        lastFlat = areaApplied;
                    }

                    // 1) Imagen filtrada: es la que se le pasa al detector
                    using var filtered = filter.Apply(work, _filters);

                    // 2) Detección y estabilización (sobre la imagen filtrada)
                    string info;
                    if (edit)
                    {
                        stabilizer.Reset();
                        DrawAreaOverlay(work, points);
                        banner = points.Length < 4
                            ? $"Haz clic en las 4 esquinas del area ({points.Length}/4)"
                            : "Arrastra las esquinas para ajustar. Pulsa 'Aplicar area'";
                        info = banner;
                    }
                    else if (_detectEnabled)
                    {
                        var detected = DrawingDetector.Detect(filtered, _detection, out mask, out double usedDiff, out double usedColor);
                        var shapes = stabilizer.Update(detected, _stability);

                        // 3) Imagen normal + figuras con su color
                        DrawingDetector.Paint(work, shapes);
                        info = $"{_cameraName}: {Describe(shapes)}" + (areaApplied ? " [área]" : "")
                               + (_detection.AutoSensitivity ? $" · sens. auto {usedDiff:0}" : "")
                               + (_detection.AutoColor ? $" · color auto {usedColor:0.0}" : "");
                    }
                    else
                    {
                        stabilizer.Reset();
                        info = $"Leyendo {_cameraName}...";
                    }

                    if (banner != null) DrawBanner(work, banner);

                    if (info != lastInfo)
                    {
                        lastInfo = info;
                        BeginInvokeSafe(() => _statusLabel.Text = info);
                    }

                    // Sin detección (edición del área / desactivada) la máscara se muestra en negro
                    mask ??= new Mat(filtered.Size(), MatType.CV_8UC1, Scalar.All(0));
                    SetImages(BitmapConverter.ToBitmap(filtered), BitmapConverter.ToBitmap(mask), BitmapConverter.ToBitmap(work));
                }
                finally
                {
                    mask?.Dispose();
                    flat?.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            BeginInvokeSafe(() => _statusLabel.Text = "Error: " + ex.Message);
        }
    }

    private static string Describe(List<DetectedShape> shapes)
    {
        if (shapes.Count == 0) return "0 formas";
        string byColor = string.Join(", ", shapes.GroupBy(s => s.Ink)
            .Select(g => $"{g.Count()} {DrawingDetector.Name(g.Key)}"));
        return $"{shapes.Count} forma(s) ({byColor}), {shapes.Sum(s => s.VertexCount)} vértices";
    }

    private void SetImages(Bitmap? filtered, Bitmap? mask, Bitmap? user)
    {
        if (IsDisposed || !IsHandleCreated)
        {
            filtered?.Dispose();
            mask?.Dispose();
            user?.Dispose();
            return;
        }
        try
        {
            BeginInvoke(() =>
            {
                var oldFilter = _picFilter.Image;
                var oldMask = _picMask.Image;
                var oldUser = _picUser.Image;
                _picFilter.Image = filtered;
                _picMask.Image = mask;
                _picUser.Image = user;
                oldFilter?.Dispose();
                oldMask?.Dispose();
                oldUser?.Dispose();
            });
        }
        catch (InvalidOperationException)
        {
            filtered?.Dispose();
            mask?.Dispose();
            user?.Dispose();
        }
    }

    private void BeginInvokeSafe(Action action)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try { BeginInvoke(action); } catch (InvalidOperationException) { }
    }

    private record CameraItem(int Index)
    {
        public override string ToString() => $"Cámara {Index}";
    }
}
