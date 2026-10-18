using OpenCvSharp;
using OpenCvSharp.Extensions;

namespace DrawinGamePrototype;

public class MainForm : Form
{
    private const int MaxCamerasToProbe = 10;
    private const int MinSize = 20;
    private const double GrabRadiusPx = 15; // radio (en píxeles de pantalla) para agarrar una esquina

    private readonly ComboBox _cameraCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
    private readonly Button _refreshButton = new() { Text = "Buscar cámaras", AutoSize = true };
    private readonly Button _toggleButton = new() { Text = "Iniciar", AutoSize = true, Enabled = false };
    private readonly Button _areaButton = new() { Text = "Seleccionar área", AutoSize = true };
    private readonly Button _clearAreaButton = new() { Text = "Borrar área", AutoSize = true };
    private readonly CheckBox _detectCheck = new() { Text = "Detectar dibujo", AutoSize = true, Checked = true };
    private readonly Label _thresholdLabel = new() { AutoSize = true, Padding = new Padding(0, 6, 0, 0) };
    private readonly TrackBar _thresholdBar = new() { Minimum = 0, Maximum = 255, Value = 100, TickStyle = TickStyle.None, Width = 140 };
    private readonly Label _contrastLabel = new() { AutoSize = true, Padding = new Padding(0, 6, 0, 0) };
    private readonly TrackBar _contrastBar = new() { Minimum = 0, Maximum = 100, Value = 40, TickStyle = TickStyle.None, Width = 140 };
    private readonly Label _simplifyLabel = new() { AutoSize = true, Padding = new Padding(0, 6, 0, 0) };
    private readonly TrackBar _simplifyBar = new() { Minimum = 0, Maximum = 20, Value = 3, TickStyle = TickStyle.None, Width = 140 };
    private readonly Label _stabilityLabel = new() { AutoSize = true, Padding = new Padding(0, 6, 0, 0) };
    private readonly TrackBar _stabilityBar = new() { Minimum = 0, Maximum = 15, Value = 4, TickStyle = TickStyle.None, Width = 140 };
    private readonly Label _statusLabel = new() { AutoSize = true, Text = "Buscando cámaras...", Padding = new Padding(0, 6, 0, 0) };
    private readonly PictureBox _picture = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Black };

    // Leídos desde el hilo de captura
    private volatile bool _detectEnabled = true;
    private volatile int _threshold = 100;
    private volatile int _contrast = 40;
    private volatile int _simplify = 3;
    private volatile int _stability = 4;

    // Área seleccionada por el usuario (esquinas en coordenadas de la imagen de la cámara)
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
        ClientSize = new System.Drawing.Size(1000, 640);
        MinimumSize = new System.Drawing.Size(500, 400);

        var topBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(8),
            WrapContents = true
        };
        topBar.Controls.Add(new Label { Text = "Cámara:", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
        topBar.Controls.Add(_cameraCombo);
        topBar.Controls.Add(_refreshButton);
        topBar.Controls.Add(_toggleButton);
        topBar.Controls.Add(_areaButton);
        topBar.Controls.Add(_clearAreaButton);
        topBar.Controls.Add(_detectCheck);
        topBar.Controls.Add(_thresholdLabel);
        topBar.Controls.Add(_thresholdBar);
        topBar.Controls.Add(_contrastLabel);
        topBar.Controls.Add(_contrastBar);
        topBar.Controls.Add(_simplifyLabel);
        topBar.Controls.Add(_simplifyBar);
        topBar.Controls.Add(_stabilityLabel);
        topBar.Controls.Add(_stabilityBar);
        topBar.Controls.Add(_statusLabel);

        Controls.Add(_picture);
        Controls.Add(topBar);

        UpdateThresholdLabel();
        UpdateContrastLabel();
        UpdateSimplifyLabel();
        UpdateStabilityLabel();

        _refreshButton.Click += async (_, _) => await RefreshCamerasAsync();
        _toggleButton.Click += (_, _) => { if (_cts == null) StartCapture(); else StopCapture(); };
        _areaButton.Click += (_, _) => OnAreaButton();
        _clearAreaButton.Click += (_, _) => ClearArea();
        _detectCheck.CheckedChanged += (_, _) => _detectEnabled = _detectCheck.Checked;
        _thresholdBar.ValueChanged += (_, _) => { _threshold = _thresholdBar.Value; UpdateThresholdLabel(); };
        _contrastBar.ValueChanged += (_, _) => { _contrast = _contrastBar.Value; UpdateContrastLabel(); };
        _simplifyBar.ValueChanged += (_, _) => { _simplify = _simplifyBar.Value; UpdateSimplifyLabel(); };
        _stabilityBar.ValueChanged += (_, _) => { _stability = _stabilityBar.Value; UpdateStabilityLabel(); };

        _picture.MouseDown += OnPictureMouseDown;
        _picture.MouseMove += OnPictureMouseMove;
        _picture.MouseUp += (_, _) => _dragIndex = -1;

        Shown += async (_, _) => await RefreshCamerasAsync();
        FormClosing += (_, _) => StopCapture();
    }

    private void UpdateStabilityLabel() => _stabilityLabel.Text = _stabilityBar.Value == 0
        ? "Estabilidad: off"
        : $"Estabilidad: {_stabilityBar.Value} px";

    private void UpdateSimplifyLabel() => _simplifyLabel.Text = $"Simplificación: {_simplifyBar.Value} px";

    private void UpdateContrastLabel() => _contrastLabel.Text = $"Contraste fino: {_contrastBar.Value}";

    private void UpdateThresholdLabel() => _thresholdLabel.Text = $"Umbral negro: {_thresholdBar.Value}";

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

        double cw = _picture.ClientSize.Width, ch = _picture.ClientSize.Height;
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
            // ¿Se ha pulsado cerca de una esquina existente? -> arrastrarla
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
                _dragIndex = _areaPoints.Count - 1; // también se puede ajustar sin soltar
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
        SetImage(null);
    }

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

                // Copia de las esquinas elegidas por el usuario
                Point2f[] points;
                lock (_areaLock) points = _areaPoints.ToArray();

                bool edit = _editArea;
                Mat? flat = null;
                string? banner = null;

                if (edit)
                {
                    // Modo selección: imagen completa con las esquinas dibujadas, sin detección
                    DrawAreaOverlay(frame, points);
                    banner = points.Length < 4
                        ? $"Haz clic en las 4 esquinas del area ({points.Length}/4)"
                        : "Arrastra las esquinas para ajustar. Pulsa 'Aplicar area'";
                }
                else if (points.Length == 4)
                {
                    // Área aplicada: se endereza y se ignora todo lo de fuera
                    flat = AreaWarp.Warp(frame, AreaWarp.Order(points));
                }

                try
                {
                    Mat work = flat ?? frame;
                    bool areaApplied = flat != null;
                    bool detect = _detectEnabled && !edit;

                    // Al cambiar entre imagen completa y área aplanada, las coordenadas cambian
                    if (areaApplied != lastFlat)
                    {
                        stabilizer.Reset();
                        lastFlat = areaApplied;
                    }

                    string info;
                    if (detect)
                    {
                        // Detectar formas y estabilizarlas
                        var detected = DrawingDetector.Detect(work, _threshold, _contrast, MinSize, _simplify);
                        var shapes = stabilizer.Update(detected, _stability);
                        DrawingDetector.Paint(work, shapes);
                        info = $"{_cameraName}: {shapes.Count} forma(s), {shapes.Sum(s => s.VertexCount)} vértices"
                               + (areaApplied ? " [área]" : "");
                    }
                    else
                    {
                        stabilizer.Reset();
                        info = banner ?? $"Leyendo {_cameraName}...";
                    }

                    if (banner != null) DrawBanner(work, banner);

                    if (info != lastInfo)
                    {
                        lastInfo = info;
                        BeginInvokeSafe(() => _statusLabel.Text = info);
                    }

                    SetImage(BitmapConverter.ToBitmap(work));
                }
                finally
                {
                    flat?.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            BeginInvokeSafe(() => _statusLabel.Text = "Error: " + ex.Message);
        }
    }

    private void SetImage(Bitmap? bmp)
    {
        if (IsDisposed || !IsHandleCreated)
        {
            bmp?.Dispose();
            return;
        }
        try
        {
            BeginInvoke(() =>
            {
                var old = _picture.Image;
                _picture.Image = bmp;
                old?.Dispose();
            });
        }
        catch (InvalidOperationException)
        {
            bmp?.Dispose();
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
