using System.Text.Json;
using DrawingGame.Vision;
using Godot;

public enum AppState
{
	CameraSelect,   // 1. elegir cámara
	AreaSelect,     // 2. marcar las 4 esquinas
	Tuning,         // 3. ajustar umbrales viendo lo detectado
	Game            // 4. jugar
}

/// <summary>Ajustes guardados entre sesiones (user://config.json).</summary>
public class AppConfig
{
	public int CameraIndex { get; set; } = -1;
	public float[]? Area { get; set; }
	public int Threshold { get; set; } = 100;
	public int Contrast { get; set; } = 40;
	public int Simplify { get; set; } = 3;
	public int Stability { get; set; } = 4;
	public bool ShowDetection { get; set; } = true;
}

/// <summary>
/// Controlador principal. Flujo: Cámara -> Área (4 puntos) -> Ajustes -> Juego.
/// Desde el juego, Esc abre un menú para volver a cualquier paso anterior.
/// La interfaz está en MainUi.cs.
/// </summary>
public partial class Main : Node2D
{
	private const string ConfigFile = "user://config.json";
	private const float GrabRadius = 15f;           // radio (px de pantalla) para agarrar una esquina
	private const float TuningPanelWidth = 340f;    // ancho del panel de ajustes
	private const float AreaTopBar = 48f;           // zona reservada arriba / abajo en la pantalla de área
	private const float AreaBottomBar = 68f;

	private readonly CameraService _camera = new();
	private AppConfig _config = new();
	private AppState _state = AppState.CameraSelect;
	private bool _menuOpen;

	// Elementos de la escena
	private CharacterBody2D? _player;
	private Node2D? _floor;
	private Vector2 _spawn;
	private readonly Node2D _level = new() { Name = "Level" };
	private readonly TextureRect _preview = new();
	private ImageTexture? _texture;
	private long _version;
	private int _imageW, _imageH;
	private string? _lastError;

	// Búsqueda de cámaras
	private Task<List<CameraSnapshot>>? _scanTask;

	// Esquinas del área (en píxeles de la imagen de cámara)
	private readonly List<Vector2> _points = new();
	private int _drag = -1;

	// ------------------------------------------------------------ Ciclo de vida

	public override void _Ready()
	{
		_player = GetNodeOrNull<CharacterBody2D>("Player");
		_floor = GetNodeOrNull<Node2D>("Floor");
		if (_player != null) _spawn = _player.Position;

		AddChild(_level);

		// Imagen de la cámara por detrás del juego
		var backLayer = new CanvasLayer { Layer = -1 };
		_preview.MouseFilter = Control.MouseFilterEnum.Ignore;
		_preview.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		_preview.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
		backLayer.AddChild(_preview);
		AddChild(backLayer);

		BuildUiRoot();
		LoadConfig();
		ApplyParams();
		GoTo(AppState.CameraSelect);
	}

	public override void _ExitTree() => _camera.Dispose();

	public override void _Process(double delta)
	{
		if (_camera.Error != _lastError)
		{
			_lastError = _camera.Error;
			if (_lastError != null) GD.PrintErr(_lastError);
			ShowError(_lastError);
		}

		if (_camera.TryGetPreview(out var data, out int w, out int h))
			ShowPreview(data, w, h);

		if (_state == AppState.CameraSelect && _scanTask is { IsCompleted: true })
			OnScanDone();

		if (_state == AppState.Game && !_menuOpen
			&& _camera.TryGetShapes(ref _version, out var shapes, out int sw, out int sh))
			RebuildLevel(shapes, sw, sh);

		// Si el personaje cae fuera de la pantalla, vuelve al inicio
		if (_state == AppState.Game && _player != null
			&& _player.Position.Y > GetViewportRect().Size.Y + 300)
		{
			_player.Position = _spawn;
			_player.Velocity = Vector2.Zero;
		}
	}

	// ------------------------------------------------------------ Cambios de pantalla

	private void GoTo(AppState next)
	{
		// Sin las 4 esquinas no se puede ajustar ni jugar
		if ((next == AppState.Tuning || next == AppState.Game) && _points.Count != 4)
			next = AppState.AreaSelect;

		// Al salir de la pantalla de área se guardan las esquinas
		if (_state == AppState.AreaSelect && next != AppState.AreaSelect)
			StoreArea();

		_state = next;
		_drag = -1;
		_imageW = _imageH = 0;
		_menuOpen = false;
		ClearScreen();

		_preview.Visible = next != AppState.CameraSelect;
		_preview.Modulate = next == AppState.Game ? new Color(1, 1, 1, 0.6f) : Colors.White;
		SetPlayerActive(next == AppState.Game);

		switch (next)
		{
			case AppState.CameraSelect:
				_camera.Stop();
				StartScan();
				BuildCameraScreen(null);
				break;

			case AppState.AreaSelect:
				_camera.Start(_config.CameraIndex);
				_camera.SetCalibration(true, PointsToCv());
				BuildAreaScreen();
				break;

			case AppState.Tuning:
				_camera.Start(_config.CameraIndex);
				_camera.SetArea(PointsToCv());
				_camera.SetCalibration(false, Array.Empty<OpenCvSharp.Point2f>());
				_camera.ShowDetection = true;      // aquí siempre se ve lo detectado
				BuildTuningScreen();
				break;

			case AppState.Game:
				_camera.Start(_config.CameraIndex);
				_camera.SetArea(PointsToCv());
				_camera.SetCalibration(false, Array.Empty<OpenCvSharp.Point2f>());
				_camera.ShowDetection = _config.ShowDetection;
				_version = -1;                     // fuerza a construir el nivel con lo último detectado
				BuildGameScreen();
				break;
		}

		SaveConfig();
	}

	private void SetPlayerActive(bool active)
	{
		if (_player != null)
		{
			_player.Visible = active;
			_player.ProcessMode = active ? ProcessModeEnum.Inherit : ProcessModeEnum.Disabled;
		}
		if (_floor != null) _floor.Visible = active;
	}

	private void ToggleMenu()
	{
		_menuOpen = !_menuOpen;
		SetPlayerActive(!_menuOpen);
		ShowMenu(_menuOpen);
	}

	private void ChooseCamera(int index)
	{
		_config.CameraIndex = index;
		_camera.Stop();
		GoTo(AppState.AreaSelect);
	}

	private void ApplyParams()
	{
		_camera.Threshold = _config.Threshold;
		_camera.Contrast = _config.Contrast;
		_camera.Simplify = _config.Simplify;
		_camera.Stability = _config.Stability;
	}

	private void SetShowDetection(bool on)
	{
		_config.ShowDetection = on;
		if (_state == AppState.Game) _camera.ShowDetection = on;
		UpdateHud();
	}

	// ------------------------------------------------------------ Búsqueda de cámaras

	private void StartScan()
	{
		_scanTask = Task.Run(() => CameraProbe.Scan());
	}

	private void OnScanDone()
	{
		List<CameraSnapshot> snapshots;
		try { snapshots = _scanTask!.Result; }
		catch (Exception ex)
		{
			GD.PrintErr("Error buscando cámaras: " + ex.Message);
			snapshots = new List<CameraSnapshot>();
		}
		_scanTask = null;
		BuildCameraScreen(snapshots);
	}

	// ------------------------------------------------------------ Imagen de fondo

	private void ShowPreview(byte[] data, int w, int h)
	{
		if (_state == AppState.CameraSelect) return;

		var image = Image.CreateFromData(w, h, false, Image.Format.Rgb8, data);
		if (_texture != null && _texture.GetWidth() == w && _texture.GetHeight() == h)
		{
			_texture.Update(image);
		}
		else
		{
			_texture = ImageTexture.CreateFromImage(image);
			_preview.Texture = _texture;
		}

		_imageW = w;
		_imageH = h;
		var rect = PreviewRect(w, h);
		_preview.Position = rect.Position;
		_preview.Size = rect.Size;
	}

	/// <summary>Rectángulo de pantalla donde se dibuja la imagen en cada pantalla.</summary>
	private Rect2 PreviewRect(int imgW, int imgH)
	{
		var view = GetViewportRect().Size;
		switch (_state)
		{
			case AppState.AreaSelect:
				return new Rect2(0, AreaTopBar, view.X, view.Y - AreaTopBar - AreaBottomBar);
			case AppState.Tuning:
				return new Rect2(0, 0, view.X - TuningPanelWidth, view.Y);
			case AppState.Game:
				var size = new Vector2(imgW, imgH);   // tamaño real: coincide con el nivel
				return new Rect2(((view - size) / 2).Round(), size);
			default:
				return new Rect2(Vector2.Zero, view);
		}
	}

	// ------------------------------------------------------------ Nivel (colisiones)

	private void RebuildLevel(List<DetectedShape> shapes, int width, int height)
	{
		foreach (var child in _level.GetChildren())
			child.QueueFree();

		_level.Position = ((GetViewportRect().Size - new Vector2(width, height)) / 2).Round();

		foreach (var shape in shapes)
		{
			var body = new StaticBody2D();
			body.AddChild(MakeCollision(shape.Outline));
			foreach (var hole in shape.Holes)
				body.AddChild(MakeCollision(hole));
			_level.AddChild(body);
		}
	}

	private static CollisionPolygon2D MakeCollision(OpenCvSharp.Point[] ring)
	{
		var pts = new Vector2[ring.Length];
		for (int i = 0; i < ring.Length; i++)
			pts[i] = new Vector2(ring[i].X, ring[i].Y);

		// Segments = colisión solo en las aristas: válido para huecos, cóncavas y trazos finos
		return new CollisionPolygon2D
		{
			Polygon = pts,
			BuildMode = CollisionPolygon2D.BuildModeEnum.Segments
		};
	}

	// ------------------------------------------------------------ Entrada

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is InputEventKey key && key.Pressed && !key.Echo)
		{
			if (_state == AppState.Game)
			{
				if (key.Keycode == Key.Escape) { ToggleMenu(); return; }
				if (key.Keycode == Key.V && !_menuOpen) { SetShowDetection(!_config.ShowDetection); return; }
			}
		}

		if (_state != AppState.AreaSelect) return;

		if (@event is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
		{
			if (!mb.Pressed) { _drag = -1; return; }
			if (!TryToImage(mb.Position, out var img, out float scale)) return;

			// ¿Cerca de una esquina existente? -> arrastrarla
			int nearest = -1;
			float best = GrabRadius / scale;
			for (int i = 0; i < _points.Count; i++)
			{
				float d = _points[i].DistanceTo(img);
				if (d <= best) { best = d; nearest = i; }
			}

			if (nearest >= 0)
			{
				_drag = nearest;
			}
			else if (_points.Count < 4)
			{
				_points.Add(img);
				_drag = _points.Count - 1;
			}
			OnPointsChanged();
		}
		else if (@event is InputEventMouseMotion mm && _drag >= 0 && _drag < _points.Count)
		{
			if (TryToImage(mm.Position, out var img, out _))
			{
				_points[_drag] = img;
				OnPointsChanged();
			}
		}
	}

	private void ClearPoints()
	{
		_points.Clear();
		_drag = -1;
		OnPointsChanged();
	}

	private void OnPointsChanged()
	{
		_camera.SetCalibration(true, PointsToCv());
		UpdateAreaInfo();
	}

	private bool TryToImage(Vector2 screen, out Vector2 image, out float scale)
	{
		image = Vector2.Zero;
		scale = 1f;
		if (_imageW <= 0 || _imageH <= 0) return false;

		var rect = PreviewRect(_imageW, _imageH);
		scale = Mathf.Min(rect.Size.X / _imageW, rect.Size.Y / _imageH);
		if (scale <= 0) return false;

		var offset = rect.Position + (rect.Size - new Vector2(_imageW, _imageH) * scale) / 2;
		image = (screen - offset) / scale;
		return image.X >= 0 && image.Y >= 0 && image.X < _imageW && image.Y < _imageH;
	}

	private OpenCvSharp.Point2f[] PointsToCv() =>
		_points.Select(p => new OpenCvSharp.Point2f(p.X, p.Y)).ToArray();

	// ------------------------------------------------------------ Guardar / cargar

	private void StoreArea()
	{
		_config.Area = _points.Count == 4
			? _points.SelectMany(p => new[] { p.X, p.Y }).ToArray()
			: null;
	}

	private void SaveConfig()
	{
		try
		{
			using var file = Godot.FileAccess.Open(ConfigFile, Godot.FileAccess.ModeFlags.Write);
			file?.StoreString(JsonSerializer.Serialize(_config, new JsonSerializerOptions { WriteIndented = true }));
		}
		catch (Exception ex)
		{
			GD.PrintErr("No se pudo guardar la configuración: " + ex.Message);
		}
	}

	private void LoadConfig()
	{
		if (!Godot.FileAccess.FileExists(ConfigFile)) return;
		try
		{
			using var file = Godot.FileAccess.Open(ConfigFile, Godot.FileAccess.ModeFlags.Read);
			var loaded = JsonSerializer.Deserialize<AppConfig>(file.GetAsText());
			if (loaded == null) return;
			_config = loaded;

			_points.Clear();
			if (_config.Area is { Length: 8 })
				for (int i = 0; i < 4; i++)
					_points.Add(new Vector2(_config.Area[i * 2], _config.Area[i * 2 + 1]));
		}
		catch (Exception ex)
		{
			GD.PrintErr("No se pudo leer la configuración: " + ex.Message);
		}
	}
}
