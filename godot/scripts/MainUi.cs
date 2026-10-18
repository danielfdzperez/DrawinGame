using DrawingGame.Vision;
using Godot;

/// <summary>Interfaz de cada pantalla, construida por código.</summary>
public partial class Main
{
	private Control _uiRoot = null!;
	private Control? _screen;
	private Label _errorLabel = null!;
	private Label? _areaInfo;
	private Button? _areaContinue;
	private Label? _hud;
	private Control? _menuRoot;

	// ------------------------------------------------------------ Base

	private void BuildUiRoot()
	{
		var layer = new CanvasLayer { Layer = 10 };
		_uiRoot = new Control { Name = "Ui", MouseFilter = Control.MouseFilterEnum.Ignore };
		_uiRoot.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		layer.AddChild(_uiRoot);
		AddChild(layer);

		// Errores (por encima de todo)
		var errorLayer = new CanvasLayer { Layer = 20 };
		_errorLabel = MakeLabel("");
		_errorLabel.AddThemeColorOverride("font_color", new Color(1f, 0.4f, 0.4f));
		_errorLabel.AddThemeFontSizeOverride("font_size", 18);
		_errorLabel.Position = new Vector2(12, 90);
		errorLayer.AddChild(_errorLabel);
		AddChild(errorLayer);
	}

	private void ShowError(string? message) => _errorLabel.Text = message ?? "";

	private void ClearScreen()
	{
		_screen?.QueueFree();
		_screen = null;
		_areaInfo = null;
		_areaContinue = null;
		_hud = null;
		_menuRoot = null;
	}

	private void SetScreen(Control screen)
	{
		_screen = screen;
		_uiRoot.AddChild(screen);
	}

	// ------------------------------------------------------------ Piezas reutilizables

	private static Label MakeLabel(string text, int fontSize = 0)
	{
		var label = new Label { Text = text, MouseFilter = Control.MouseFilterEnum.Ignore };
		if (fontSize > 0) label.AddThemeFontSizeOverride("font_size", fontSize);
		return label;
	}

	private static Button MakeButton(string text, Action onPressed, float minWidth = 0)
	{
		var button = new Button { Text = text, CustomMinimumSize = new Vector2(minWidth, 36) };
		button.Pressed += onPressed;
		return button;
	}

	private static Control MakeSpacer() => new Control
	{
		SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
		SizeFlagsVertical = Control.SizeFlags.ExpandFill,
		MouseFilter = Control.MouseFilterEnum.Ignore
	};

	private static PanelContainer MakePanel(float alpha = 0.75f)
	{
		var panel = new PanelContainer();
		var style = new StyleBoxFlat { BgColor = new Color(0.08f, 0.09f, 0.11f, alpha) };
		style.SetContentMarginAll(10);
		panel.AddThemeStyleboxOverride("panel", style);
		return panel;
	}

	private static Control FullRect(Control c)
	{
		c.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		return c;
	}

	// ------------------------------------------------------------ 1. Elegir cámara

	private void BuildCameraScreen(List<CameraSnapshot>? snapshots)
	{
		ClearScreen();

		var margin = (MarginContainer)FullRect(new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore });
		foreach (var side in new[] { "left", "right", "top", "bottom" })
			margin.AddThemeConstantOverride("margin_" + side, 20);

		var box = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
		box.AddThemeConstantOverride("separation", 12);
		margin.AddChild(box);

		box.AddChild(MakeLabel("Paso 1 de 3 · Elige la cámara", 28));
		box.AddChild(MakeLabel("Esto es lo que ve cada cámara. Pulsa «Usar esta cámara» en la que apunta al folio."));

		if (snapshots == null)
		{
			box.AddChild(MakeLabel("Buscando cámaras..."));
		}
		else if (snapshots.Count == 0)
		{
			box.AddChild(MakeLabel("No se encontró ninguna cámara. Conéctala, cierra otras aplicaciones que la usen y pulsa «Actualizar»."));
		}
		else
		{
			var scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
			var flow = new HFlowContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
			flow.AddThemeConstantOverride("h_separation", 16);
			flow.AddThemeConstantOverride("v_separation", 16);
			scroll.AddChild(flow);
			box.AddChild(scroll);

			foreach (var snap in snapshots)
			{
				int index = snap.Index;
				var card = MakePanel(0.9f);
				var cardBox = new VBoxContainer();
				cardBox.AddThemeConstantOverride("separation", 8);
				card.AddChild(cardBox);

				var image = Image.CreateFromData(snap.Width, snap.Height, false, Image.Format.Rgb8, snap.Rgb);
				cardBox.AddChild(new TextureRect
				{
					Texture = ImageTexture.CreateFromImage(image),
					ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
					StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
					CustomMinimumSize = new Vector2(snap.Width, snap.Height)
				});

				string title = $"Cámara {index}" + (index == _config.CameraIndex ? "  (la última usada)" : "");
				cardBox.AddChild(MakeLabel(title, 18));
				cardBox.AddChild(MakeButton("Usar esta cámara", () => ChooseCamera(index)));
				flow.AddChild(card);
			}
		}

		if (snapshots == null)
			box.AddChild(MakeSpacer());

		var bottom = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
		var refresh = MakeButton("Actualizar", () => { StartScan(); BuildCameraScreen(null); }, 140);
		refresh.Disabled = snapshots == null;
		bottom.AddChild(refresh);
		box.AddChild(bottom);

		SetScreen(margin);
	}

	// ------------------------------------------------------------ 2. Esquinas del área

	private void BuildAreaScreen()
	{
		var root = (VBoxContainer)FullRect(new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore });

		var top = MakePanel();
		top.CustomMinimumSize = new Vector2(0, AreaTopBar);
		_areaInfo = MakeLabel("", 18);
		top.AddChild(_areaInfo);
		root.AddChild(top);

		root.AddChild(MakeSpacer());

		var bottom = MakePanel();
		bottom.CustomMinimumSize = new Vector2(0, AreaBottomBar);
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 12);
		row.AddChild(MakeButton("‹ Cámaras", () => GoTo(AppState.CameraSelect), 120));
		row.AddChild(MakeButton("Borrar puntos", ClearPoints, 140));
		row.AddChild(MakeSpacer());
		_areaContinue = MakeButton("Continuar ›", () => GoTo(AppState.Tuning), 160);
		row.AddChild(_areaContinue);
		bottom.AddChild(row);
		root.AddChild(bottom);

		SetScreen(root);
		UpdateAreaInfo();
	}

	private void UpdateAreaInfo()
	{
		if (_areaInfo != null)
			_areaInfo.Text = _points.Count < 4
				? $"Paso 2 de 3 · Haz clic en las 4 esquinas de la zona de juego ({_points.Count}/4)"
				: "Paso 2 de 3 · Arrastra las esquinas para ajustarlas y pulsa «Continuar»";
		if (_areaContinue != null)
			_areaContinue.Disabled = _points.Count != 4;
	}

	// ------------------------------------------------------------ 3. Ajustes de detección

	private void BuildTuningScreen()
	{
		var root = (HBoxContainer)FullRect(new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore });
		root.AddChild(MakeSpacer());

		var panel = MakePanel(0.92f);
		panel.CustomMinimumSize = new Vector2(TuningPanelWidth, 0);
		var box = new VBoxContainer();
		box.AddThemeConstantOverride("separation", 10);
		panel.AddChild(box);

		box.AddChild(MakeLabel("Paso 3 de 3 · Ajustes", 22));
		var help = MakeLabel("Lo verde es lo que se detecta. Ajusta hasta que cubra tu dibujo y nada más.");
		help.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		help.CustomMinimumSize = new Vector2(TuningPanelWidth - 30, 0);
		box.AddChild(help);

		AddSlider(box, "Umbral negro", 0, 255, _config.Threshold,
			"Qué tan oscuro es «negro». Súbelo si no detecta trazos gruesos; bájalo si detecta grises.",
			v => { _config.Threshold = v; ApplyParams(); });
		AddSlider(box, "Contraste fino", 0, 100, _config.Contrast,
			"Para trazos finos. Bájalo si no los detecta; súbelo si hay ruido. 0 = desactivado.",
			v => { _config.Contrast = v; ApplyParams(); });
		AddSlider(box, "Simplificación (px)", 0, 20, _config.Simplify,
			"Más alto = menos vértices y formas más angulosas.",
			v => { _config.Simplify = v; ApplyParams(); });
		AddSlider(box, "Estabilidad (px)", 0, 15, _config.Stability,
			"Más alto = menos temblor. 0 = desactivado.",
			v => { _config.Stability = v; ApplyParams(); });

		var check = new CheckBox { Text = "Ver las formas (verde) durante el juego", ButtonPressed = _config.ShowDetection };
		check.Toggled += on => _config.ShowDetection = on;
		box.AddChild(check);

		box.AddChild(MakeSpacer());

		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 10);
		row.AddChild(MakeButton("‹ Esquinas", () => GoTo(AppState.AreaSelect), 110));
		var go = MakeButton("Continuar al juego ›", () => GoTo(AppState.Game));
		go.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		row.AddChild(go);
		box.AddChild(row);

		root.AddChild(panel);
		SetScreen(root);
	}

	private static void AddSlider(Control parent, string title, int min, int max, int value, string hint, Action<int> onChange)
	{
		var label = MakeLabel($"{title}: {value}", 16);
		var slider = new HSlider
		{
			MinValue = min,
			MaxValue = max,
			Step = 1,
			Value = value,
			CustomMinimumSize = new Vector2(0, 24),
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
		};
		slider.ValueChanged += v =>
		{
			int i = (int)v;
			label.Text = $"{title}: {i}";
			onChange(i);
		};

		var hintLabel = MakeLabel(hint, 13);
		hintLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		hintLabel.CustomMinimumSize = new Vector2(TuningPanelWidth - 30, 0);
		hintLabel.Modulate = new Color(1, 1, 1, 0.65f);

		parent.AddChild(label);
		parent.AddChild(slider);
		parent.AddChild(hintLabel);
	}

	// ------------------------------------------------------------ 4. Juego

	private void BuildGameScreen()
	{
		var screen = (Control)FullRect(new Control { MouseFilter = Control.MouseFilterEnum.Ignore });

		// Barra superior: ayuda + botón de menú
		var margin = (MarginContainer)FullRect(new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore });
		foreach (var side in new[] { "left", "right", "top", "bottom" })
			margin.AddThemeConstantOverride("margin_" + side, 8);
		var bar = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };

		var hudPanel = MakePanel(0.55f);
		hudPanel.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
		hudPanel.MouseFilter = Control.MouseFilterEnum.Ignore;
		_hud = MakeLabel("");
		hudPanel.AddChild(_hud);
		bar.AddChild(hudPanel);
		bar.AddChild(MakeSpacer());

		var menuButton = MakeButton("Menú (Esc)", ToggleMenu, 120);
		menuButton.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
		bar.AddChild(menuButton);
		margin.AddChild(bar);
		screen.AddChild(margin);

		// Menú (oculto hasta que se abre)
		var center = (CenterContainer)FullRect(new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore });
		var menu = MakePanel(0.95f);
		var menuBox = new VBoxContainer();
		menuBox.AddThemeConstantOverride("separation", 10);
		menuBox.AddChild(MakeLabel("Menú", 24));
		menuBox.AddChild(MakeButton("Seguir jugando", ToggleMenu, 260));
		menuBox.AddChild(MakeButton("Ajustes de detección", () => GoTo(AppState.Tuning), 260));
		menuBox.AddChild(MakeButton("Esquinas del área", () => GoTo(AppState.AreaSelect), 260));
		menuBox.AddChild(MakeButton("Cambiar de cámara", () => GoTo(AppState.CameraSelect), 260));
		menu.AddChild(menuBox);
		center.AddChild(menu);
		center.Visible = false;
		screen.AddChild(center);
		_menuRoot = center;

		SetScreen(screen);
		UpdateHud();
	}

	private void ShowMenu(bool open)
	{
		if (_menuRoot != null) _menuRoot.Visible = open;
	}

	private void UpdateHud()
	{
		if (_hud != null)
			_hud.Text = $"V = {(_config.ShowDetection ? "ocultar" : "ver")} formas  |  Mover: A/D o flechas  |  Saltar: W o espacio  |  Esc = menú";
	}
}
