using System.Text.Json;
using DrawingGame.Vision;
using Godot;

public enum AppState
{
	SourceSelect,   // 0. elegir el origen de la imagen: cámara del PC o del móvil
	CameraSelect,   // 1. elegir cámara del PC
	RemoteWait,     // 1. (móvil) mostrar el QR y esperar a que el móvil se conecte
	AreaSelect,     // 2. marcar las 4 esquinas
	Tuning,         // 3. ajustar umbrales viendo lo detectado
	Game            // 4. jugar
}

/// <summary>Ajustes guardados entre sesiones (user://config.json).</summary>
public class AppConfig
{
	public int CameraIndex { get; set; } = -1;
	public float[]? Area { get; set; }
	public int Simplify { get; set; } = 2;
	public int Stability { get; set; } = 4;
	public int Sensitivity { get; set; } = 0;   // 0 = automática
	public int ColorMin { get; set; } = 0;      // 0 = automático
	public int MinThickness { get; set; } = 7;  // grosor mínimo de un trazo (px); lo más fino se ignora. 1 = sin límite
	public bool ShowDetection { get; set; } = true;

	/// <summary>Origen de la imagen: "Pc" (webcam) o "Phone" (app del móvil).</summary>
	public string Source { get; set; } = "Pc";
	public int RemotePort { get; set; } = TcpFrameTransport.DefaultPort;
	public int WebPort { get; set; } = WebFrameTransport.DefaultPort;
	public string RemoteToken { get; set; } = "";
	/// <summary>Giro extra de la imagen del móvil, en cuartos de vuelta horarios (0-3).</summary>
	public int RemoteRotation { get; set; } = 0;

	/// <summary>true = la estrella es el propio dibujo amarillo y se borra de la imagen al cogerla; false = se pone una estrella dibujada.</summary>
	public bool StarsAsDrawing { get; set; } = true;
}

/// <summary>
/// Controlador principal. Flujo: Cámara -> Área (4 puntos) -> Ajustes -> Juego.
/// Desde el juego, Esc abre un menú para volver a cualquier paso anterior.
/// La interfaz está en MainUi.cs.
/// </summary>
public partial class Main : Node2D
{
	/// <summary>
	/// Qué significa cada color (o si el color no importa). Se elige en el Inspector de Godot arrastrando un .tres de res://rules/.
	/// Si se deja vacío se usa res://rules/platformer.tres, y si tampoco existe, las reglas por defecto del código.
	/// </summary>
	[Export] public ColorRuleSet? Rules { get; set; }

	private const string ConfigFile = "user://config.json";
	private const float GrabRadius = 15f;           // radio (px de pantalla) para agarrar una esquina
	private const float TuningPanelWidth = 340f;    // ancho FIJO del panel de ajustes (la imagen se coloca a su izquierda)
	private const float TuningCollapsedWidth = 48f; // ancho de la barra que queda cuando el panel está oculto
	private const float TuningHintWidth = TuningPanelWidth - 56f;   // ancho del texto de ayuda (panel - márgenes - scroll)
	private const float AreaTopBar = 48f;           // zona reservada arriba / abajo en la pantalla de área
	private const float AreaBottomBar = 68f;

	private readonly CameraService _camera = new();
	private RemoteFrameReceiver? _remote;                       // imágenes que llegan del móvil (solo si el origen es el móvil)
	private readonly List<IRemoteTransport> _transports = new(); // formas de conectar el móvil (app por TCP, web...)
	private WebcamSource? _webcam;                              // solo si el origen es la webcam del PC
	private bool _remoteSeenConnected;
	private AppConfig _config = new();
	private AppState _state = AppState.SourceSelect;
	private bool _menuOpen;

	// Elementos de la escena
	private Player? _player;
	private Node2D? _floor;
	private Vector2 _spawn;
	private readonly LevelManager _level = new() { Name = "Level" };
	private IGameMode _mode = null!;      // reglas de la partida (cómo se gana, qué muestra el HUD)
	private bool _won;
	private bool _placePlayer;
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
		_player = GetNodeOrNull<Player>("Player");
		_floor = GetNodeOrNull<Node2D>("FLoor") ?? GetNodeOrNull<Node2D>("Floor");
		if (_player != null)
		{
			_spawn = _player.Position;
			_player.Died += RespawnPlayer;
		}

		_mode = new StarCollectMode(_level);   // para otro tipo de juego: otra implementación de IGameMode
		_mode.Changed += OnModeChanged;
		_mode.Won += OnWon;
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
		GoTo(AppState.SourceSelect);
	}

	public override void _ExitTree()
	{
		_camera.Dispose();
		StopRemote();
		StopWebcam();
	}

	public override void _Process(double delta)
	{
		string? message = _camera.Error ?? _transports.Select(t => t.Error).FirstOrDefault(e => e != null);
		if (message == null && _remote != null && !_remote.Connected
			&& _state is AppState.AreaSelect or AppState.Tuning or AppState.Game)
			message = "Móvil desconectado. Esperando a que vuelva a conectarse...";
		if (message != _lastError)
		{
			_lastError = message;
			if (_lastError != null) GD.PrintErr(_lastError);
			ShowError(_lastError);
		}

		if (_state == AppState.RemoteWait && _remote != null)
		{
			if (!_remote.Connected) _remoteSeenConnected = false;
			else if (!_remoteSeenConnected)
			{
				GoTo(AppState.AreaSelect);      // el móvil acaba de conectarse: a marcar las esquinas
				return;
			}
			UpdateRemoteInfo();
		}

		if (_camera.TryGetPreview(out var data, out int w, out int h))
			ShowPreview(data, w, h);

		if (_state == AppState.CameraSelect && _scanTask is { IsCompleted: true })
			OnScanDone();

		if (_state == AppState.Game && !_menuOpen
			&& _camera.TryGetShapes(ref _version, out var shapes, out int sw, out int sh))
			UpdateLevel(shapes, sw, sh);

		// Si el personaje cae fuera de la pantalla, vuelve al inicio
		if (_state == AppState.Game && _player != null
			&& _player.Position.Y > GetViewportRect().Size.Y + 300)
		{
			RespawnPlayer();
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

		if (next != AppState.Game) _camera.SetErasedIds(Array.Empty<int>());   // fuera del juego se ve la imagen completa

		_state = next;
		_drag = -1;
		_imageW = _imageH = 0;
		_menuOpen = false;
		ClearScreen();

		_preview.Visible = next is not (AppState.SourceSelect or AppState.CameraSelect or AppState.RemoteWait);
		_preview.Modulate = next == AppState.Game ? new Color(1, 1, 1, 0.6f) : Colors.White;
		SetPlayerActive(next == AppState.Game);

		switch (next)
		{
			case AppState.SourceSelect:
				StopCamera();
				StopRemote();
				BuildSourceScreen();
				break;

			case AppState.CameraSelect:
				StopCamera();
				StopRemote();
				StartScan();
				BuildCameraScreen(null);
				break;

			case AppState.RemoteWait:
				StopCamera();
				EnsureRemote();
				BuildRemoteScreen();
				break;

			case AppState.AreaSelect:
				StartCamera();
				_camera.SetCalibration(true, PointsToCv());
				BuildAreaScreen();
				break;

			case AppState.Tuning:
				StartCamera();
				_camera.SetArea(PointsToCv());
				_camera.SetCalibration(false, Array.Empty<OpenCvSharp.Point2f>());
				_camera.ShowDetection = true;      // aquí siempre se ve lo detectado
				BuildTuningScreen();
				break;

			case AppState.Game:
				StartCamera();
				_camera.SetArea(PointsToCv());
				_camera.SetCalibration(false, Array.Empty<OpenCvSharp.Point2f>());
				_camera.ShowDetection = _config.ShowDetection;
				_version = -1;                     // fuerza a construir el nivel con lo último detectado
				_level.Clear();                    // partida nueva: sin objetos ni estrellas cogidas
				_won = false;
				_placePlayer = true;               // el jugador empieza en la figura azul (si hay)
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
		SetPlayerActive(!_menuOpen && !_won);
		ShowMenu(_menuOpen);
	}

	private void ChooseCamera(int index)
	{
		_config.CameraIndex = index;
		StopCamera();
		GoTo(AppState.AreaSelect);
	}

	private void ApplyParams()
	{
		_camera.Simplify = _config.Simplify;
		_camera.Stability = _config.Stability;
		_camera.Sensitivity = _config.Sensitivity;
		_camera.ColorMin = _config.ColorMin;
		_camera.MinThickness = _config.MinThickness;
		_level.SetClassifier(ResolveRules());
		_level.StarsAsDrawing = _config.StarsAsDrawing;
	}

	private void SetShowDetection(bool on)
	{
		_config.ShowDetection = on;
		if (_state == AppState.Game) _camera.ShowDetection = on;
		UpdateHud();
	}

	// ------------------------------------------------------------ Origen de la imagen (PC o móvil)

	private void ChooseSource(string source)
	{
		if (_config.Source != source)
		{
			// Otra cámara = otra imagen: las esquinas guardadas ya no valen
			_points.Clear();
			_config.Area = null;
			_config.Source = source;
		}
		GoTo(source == "Phone" ? AppState.RemoteWait : AppState.CameraSelect);
	}

	private void EnsureRemote()
	{
		if (_remote != null) return;
		if (string.IsNullOrEmpty(_config.RemoteToken))
			_config.RemoteToken = Guid.NewGuid().ToString("N")[..8];

		_remote = new RemoteFrameReceiver { UserRotation = _config.RemoteRotation };

		// Formas de conectar el móvil: para añadir otra (web...), crear el transporte y añadirlo aquí
		var tcp = new TcpFrameTransport(_remote, _config.RemoteToken, _config.RemotePort);
		tcp.Start();
		if (tcp.Port > 0) _config.RemotePort = tcp.Port;   // si el puerto estaba ocupado, se recuerda el nuevo
		_transports.Add(tcp);

		var web = new WebFrameTransport(_remote, _config.RemoteToken, _config.WebPort, ProjectSettings.GlobalizePath("user://remote-cert.pfx"));
		web.Start();
		if (web.Port > 0) _config.WebPort = web.Port;
		_transports.Add(web);
	}

	private void StopRemote()
	{
		foreach (var transport in _transports) transport.Dispose();
		_transports.Clear();
		_remote?.Dispose();
		_remote = null;
	}

	private void StopWebcam()
	{
		_webcam?.Dispose();
		_webcam = null;
	}

	/// <summary>Para el hilo de la cámara y libera la webcam (el móvil sigue conectado).</summary>
	private void StopCamera()
	{
		_camera.Stop();
		StopWebcam();
	}

	private void StartCamera()
	{
		if (_remote != null)
		{
			_camera.Start(_remote);
		}
		else
		{
			_webcam ??= new WebcamSource(_config.CameraIndex);
			_camera.Start(_webcam);
		}
	}

	/// <summary>Vuelve al paso de elegir/conectar la cámara del origen actual.</summary>
	private void BackToSource() => GoTo(_remote != null ? AppState.RemoteWait : AppState.CameraSelect);

	private void RotateRemote()
	{
		_config.RemoteRotation = (_config.RemoteRotation + 1) & 3;
		if (_remote != null) _remote.UserRotation = _config.RemoteRotation;
		ClearPoints();   // la imagen cambia de forma: hay que marcar las esquinas otra vez
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
		if (_state is AppState.CameraSelect or AppState.SourceSelect or AppState.RemoteWait) return;

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
				// Toda la imagen cabe a la izquierda del panel (o de su barra si está oculto): nada la tapa
				return new Rect2(0, 0, view.X - (_tuningPanelOpen ? TuningPanelWidth : TuningCollapsedWidth), view.Y);
			case AppState.Game:
				var size = new Vector2(imgW, imgH);   // tamaño real: coincide con el nivel
				return new Rect2(((view - size) / 2).Round(), size);
			default:
				return new Rect2(Vector2.Zero, view);
		}
	}

	// ------------------------------------------------------------ Nivel

	/// <summary>Pasa las figuras estables al gestor del nivel, que crea/actualiza/elimina los objetos de Godot.</summary>
	private void UpdateLevel(List<DetectedShape> shapes, int width, int height)
	{
		_level.Apply(shapes, width, height, GetViewportRect().Size);

		// Al empezar, el jugador aparece en la figura azul en cuanto se ve (o se queda en el punto por defecto)
		if (_placePlayer)
		{
			if (_level.HasSpawn)
			{
				RespawnPlayer();
				_placePlayer = false;
			}
			else if (shapes.Count > 0)
			{
				_placePlayer = false;
			}
		}
	}

	private void RespawnPlayer()
	{
		_player?.TeleportTo(_level.HasSpawn ? _level.SpawnPoint : _spawn);
	}

	/// <summary>Quién decide qué es cada color: la propiedad Rules (Inspector), si no res://rules/platformer.tres, si no el código.</summary>
	private ColorRuleSet ResolveRules()
	{
		if (Rules != null) return Rules;
		try
		{
			var loaded = GD.Load<ColorRuleSet>("res://rules/platformer.tres");
			if (loaded != null) return loaded;
		}
		catch (Exception e)
		{
			GD.PushWarning($"No se pudo cargar res://rules/platformer.tres: {e.Message}");
		}
		return ColorRuleSet.CreateDefault();
	}

	private void OnModeChanged()
	{
		UpdateHud();
		// Lo que el modo diga (p. ej. estrellas ya cogidas) se borra de la imagen mostrada
		_camera.SetErasedIds(_mode.HiddenShapeIds());
	}

	private void OnWon()
	{
		_won = true;
		SetPlayerActive(false);
		ShowWin(true);
	}

	private void RestartGame()
	{
		_won = false;
		ShowWin(false);
		RespawnPlayer();            // primero el jugador (y la física lo sabe) ...
		SetPlayerActive(true);
		_mode.Restart();            // ... y luego las estrellas, que se reactivan con retraso
	}

	// ------------------------------------------------------------ Entrada

	private void ToggleFullscreen()
	{
		bool full = DisplayServer.WindowGetMode() == DisplayServer.WindowMode.Fullscreen;
		DisplayServer.WindowSetMode(full ? DisplayServer.WindowMode.Maximized : DisplayServer.WindowMode.Fullscreen);
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is InputEventKey key && key.Pressed && !key.Echo)
		{
			// F11 (o Alt+Enter): pantalla completa <-> ventana (en cualquier pantalla de la aplicación)
			if (key.Keycode == Key.F11 || (key.AltPressed && key.Keycode == Key.Enter))
			{
				ToggleFullscreen();
				GetViewport().SetInputAsHandled();
				return;
			}
			if (_state == AppState.Tuning && key.Keycode == Key.H)
			{
				SetTuningPanel(!_tuningPanelOpen);   // ocultar/mostrar los ajustes para ver toda la imagen
				return;
			}
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
