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
	private Control? _winRoot;
	private Label? _remoteInfo;
	private Button? _remoteContinue;
	private Control? _tuningPanel;       // panel de ajustes abierto
	private Control? _tuningCollapsed;   // barra estrecha que lo sustituye cuando está oculto
	private bool _tuningPanelOpen = true;

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
		_winRoot = null;
		_remoteInfo = null;
		_remoteContinue = null;
		_tuningPanel = null;
		_tuningCollapsed = null;
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

	// ------------------------------------------------------------ 0. Elegir origen (PC o móvil)

	private void BuildSourceScreen()
	{
		var center = (CenterContainer)FullRect(new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore });
		var box = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
		box.AddThemeConstantOverride("separation", 16);
		center.AddChild(box);

		box.AddChild(MakeLabel("DrawinGame", 40));
		box.AddChild(MakeLabel("¿Qué cámara quieres usar?", 22));

		var pc = MakeButton("Cámara del PC" + (_config.Source == "Pc" ? "  (la última usada)" : ""),
							() => ChooseSource("Pc"), 380);
		pc.CustomMinimumSize = new Vector2(380, 64);
		var phone = MakeButton("Cámara del móvil (Android)" + (_config.Source == "Phone" ? "  (la última usada)" : ""),
							   () => ChooseSource("Phone"), 380);
		phone.CustomMinimumSize = new Vector2(380, 64);
		box.AddChild(pc);
		box.AddChild(phone);

		SetScreen(center);
	}

	// ------------------------------------------------------------ 1. (móvil) QR y espera

	private void BuildRemoteScreen()
	{
		var margin = (MarginContainer)FullRect(new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore });
		foreach (var side in new[] { "left", "right", "top", "bottom" })
			margin.AddThemeConstantOverride("margin_" + side, 20);

		var box = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
		box.AddThemeConstantOverride("separation", 10);
		margin.AddChild(box);

		box.AddChild(MakeLabel("Paso 1 de 3 · Conecta la cámara del móvil", 28));
		box.AddChild(MakeLabel("El móvil debe estar en la misma red WiFi que este PC. Escanea el código de la forma de conexión que prefieras."));

		var hosts = LocalNetwork.Addresses();
		if (_transports.Count == 0)
		{
			box.AddChild(MakeLabel("No hay ninguna forma de conexión disponible."));
		}
		else if (hosts.Count == 0)
		{
			box.AddChild(MakeLabel("No se ha encontrado ninguna red. Conecta el PC al WiFi o al router y vuelve a entrar en esta pantalla."));
		}
		else
		{
			// Un QR por cada forma de conectar el móvil
			var qrRow = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
			qrRow.AddThemeConstantOverride("separation", 40);
			foreach (var transport in _transports)
			{
				var column = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
				column.AddThemeConstantOverride("separation", 6);
				column.AddChild(MakeLabel(transport.Title, 20));
				if (transport.Error != null || transport.Port == 0)
				{
					column.AddChild(MakeLabel(transport.Error ?? "No disponible."));
				}
				else
				{
					column.AddChild(new TextureRect
					{
						Texture = MakeQrTexture(transport.BuildUri(hosts)),
						ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
						StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
						TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
						CustomMinimumSize = new Vector2(320, 320),
						MouseFilter = Control.MouseFilterEnum.Ignore
					});
					column.AddChild(MakeLabel(transport.Hint));
					column.AddChild(MakeLabel(transport.Describe(hosts)));
				}
				qrRow.AddChild(column);
			}
			box.AddChild(qrRow);
		}

		_remoteInfo = MakeLabel("", 18);
		box.AddChild(_remoteInfo);
		box.AddChild(MakeLabel("La primera vez, si Windows pregunta por el cortafuegos, permite el acceso en redes privadas."));
		box.AddChild(MakeSpacer());

		var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
		row.AddChild(MakeButton("‹ Inicio", () => GoTo(AppState.SourceSelect), 120));
		row.AddChild(MakeSpacer());
		_remoteContinue = MakeButton("Continuar ›", () => GoTo(AppState.AreaSelect), 160);
		row.AddChild(_remoteContinue);
		box.AddChild(row);

		_remoteSeenConnected = _remote?.Connected ?? false;
		SetScreen(margin);
		UpdateRemoteInfo();
	}

	private void UpdateRemoteInfo()
	{
		if (_remote == null) return;
		if (_remoteInfo != null)
			_remoteInfo.Text = _remote.Connected ? $"Móvil conectado ({_remote.ClientAddress})" : "Esperando al móvil...";
		if (_remoteContinue != null)
			_remoteContinue.Disabled = !_remote.Connected;
	}

	/// <summary>Dibuja el texto como código QR (módulos negros sobre blanco, con margen).</summary>
	private static ImageTexture? MakeQrTexture(string text)
	{
		try
		{
			const int scale = 8;     // píxeles por módulo
			const int pad = 2;       // módulos blancos extra alrededor
			using var generator = new QRCoder.QRCodeGenerator();
			using var data = generator.CreateQrCode(text, QRCoder.QRCodeGenerator.ECCLevel.M);
			var modules = data.ModuleMatrix;
			int n = modules.Count;
			int size = (n + pad * 2) * scale;

			var pixels = new byte[size * size];
			Array.Fill(pixels, (byte)255);
			for (int my = 0; my < n; my++)
			{
				for (int mx = 0; mx < n; mx++)
				{
					if (!modules[my][mx]) continue;
					for (int y = 0; y < scale; y++)
					{
						int row = ((my + pad) * scale + y) * size + (mx + pad) * scale;
						for (int x = 0; x < scale; x++) pixels[row + x] = 0;
					}
				}
			}
			return ImageTexture.CreateFromImage(Image.CreateFromData(size, size, false, Image.Format.L8, pixels));
		}
		catch (Exception ex)
		{
			GD.PrintErr("No se pudo generar el QR: " + ex.Message);
			return null;
		}
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
		bottom.AddChild(MakeButton("‹ Inicio", () => GoTo(AppState.SourceSelect), 120));
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
		row.AddChild(MakeButton(_remote != null ? "‹ Móvil" : "‹ Cámaras", BackToSource, 120));
		row.AddChild(MakeButton("Borrar puntos", ClearPoints, 140));
		if (_remote != null) row.AddChild(MakeButton("Girar imagen 90°", RotateRemote, 170));
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
		var root = FullRect(new Control { MouseFilter = Control.MouseFilterEnum.Ignore });

		// Panel de ajustes: pegado a la derecha y de ancho FIJO. Lo de dentro (con scroll) nunca puede ensancharlo,
		// así que nunca tapa la imagen: PreviewRect() reserva exactamente este ancho.
		var panel = MakePanel(0.92f);
		AnchorRight(panel, TuningPanelWidth);
		panel.ClipContents = true;

		var outer = new VBoxContainer();
		outer.AddThemeConstantOverride("separation", 8);
		panel.AddChild(outer);

		var header = new HBoxContainer();
		header.AddChild(MakeLabel("Paso 3 de 3 · Ajustes", 20));
		header.AddChild(MakeSpacer());
		var hide = MakeButton("Ocultar ›", () => SetTuningPanel(false));
		hide.TooltipText = "Oculta el panel para ver toda la imagen (tecla H)";
		header.AddChild(hide);
		outer.AddChild(header);

		var scroll = new ScrollContainer
		{
			SizeFlagsVertical = Control.SizeFlags.ExpandFill,
			HorizontalScrollMode = ScrollContainer.ScrollMode.ShowNever
		};
		var box = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		box.AddThemeConstantOverride("separation", 10);
		scroll.AddChild(box);
		outer.AddChild(scroll);

		var help = MakeLabel("Las figuras se pintan con su color: negro = plataforma, rojo = daño, amarillo = estrella, azul = inicio del jugador. Pulsa H para ocultar este panel y ver toda la imagen.");
		help.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		help.CustomMinimumSize = new Vector2(TuningHintWidth, 0);
		box.AddChild(help);

		AddSlider(box, "Simplificación (px)", 0, 20, _config.Simplify,
			"Más alto = menos vértices y formas más angulosas.",
			v => { _config.Simplify = v; ApplyParams(); });
		AddSlider(box, "Estabilidad (px)", 0, 15, _config.Stability,
			"Más alto = menos temblor. Mínimo efectivo 1.",
			v => { _config.Stability = v; ApplyParams(); });
		AddSlider(box, "Sensibilidad (0 = auto)", 0, 40, _config.Sensitivity,
			"Cuánto se tiene que diferenciar un trazo del papel. 0 = se calcula sola. Las sombras suaves no cuentan como trazo.",
			v => { _config.Sensitivity = v; ApplyParams(); });
		AddSlider(box, "Color mínimo (0 = auto)", 0, 30, _config.ColorMin,
			"Cuánto color hace falta para no ser negro. Bájalo si un azul oscuro sale negro. 0 = auto.",
			v => { _config.ColorMin = v; ApplyParams(); });
		AddSlider(box, "Grosor mínimo (px)", 1, 15, _config.MinThickness,
			"Lo más fino que esto se ignora (arañazos, polvo, rayas del papel). Bájalo si dibujas con trazo fino; 1 = sin límite.",
			v => { _config.MinThickness = v; ApplyParams(); });

		AddCheck(box, "Ver formas en el juego", _config.ShowDetection,
			"Pinta encima de la imagen lo que se detecta (tecla V durante el juego).",
			on => _config.ShowDetection = on);
		AddCheck(box, "Estrella = dibujo amarillo", _config.StarsAsDrawing,
			"El dibujo amarillo se borra de la imagen al cogerlo. Si no, aparece una estrella dibujada.",
			on => { _config.StarsAsDrawing = on; ApplyParams(); });

		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 10);
		row.AddChild(MakeButton("‹ Esquinas", () => GoTo(AppState.AreaSelect), 110));
		var go = MakeButton("Continuar al juego ›", () => GoTo(AppState.Game));
		go.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		row.AddChild(go);
		outer.AddChild(row);

		// Barra estrecha que sustituye al panel cuando está oculto (la imagen no se solapa con ella)
		var bar = MakePanel(0.92f);
		AnchorRight(bar, TuningCollapsedWidth);
		var showButton = MakeButton("‹", () => SetTuningPanel(true));
		showButton.TooltipText = "Mostrar los ajustes (tecla H)";
		showButton.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;   // un botón arriba, no una barra entera
		bar.AddChild(showButton);

		root.AddChild(panel);
		root.AddChild(bar);
		_tuningPanel = panel;
		_tuningCollapsed = bar;
		SetScreen(root);
		SetTuningPanel(true);
	}

	/// <summary>Muestra u oculta el panel de ajustes. La imagen se recoloca sola (ver PreviewRect).</summary>
	private void SetTuningPanel(bool open)
	{
		_tuningPanelOpen = open;
		if (_tuningPanel != null) _tuningPanel.Visible = open;
		if (_tuningCollapsed != null) _tuningCollapsed.Visible = !open;
	}

	/// <summary>Pega el control al borde derecho, a todo lo alto, con un ancho fijo (si no cabe, crece hacia dentro, no hacia fuera).</summary>
	private static void AnchorRight(Control control, float width)
	{
		control.SetAnchorsPreset(Control.LayoutPreset.RightWide);
		control.OffsetLeft = -width;
		control.OffsetRight = 0;
		control.OffsetTop = 0;
		control.OffsetBottom = 0;
		control.GrowHorizontal = Control.GrowDirection.Begin;
	}

	private static void AddCheck(Control parent, string title, bool value, string hint, Action<bool> onChange)
	{
		var check = new CheckBox { Text = title, ButtonPressed = value };
		check.Toggled += on => onChange(on);

		var hintLabel = MakeLabel(hint, 13);
		hintLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		hintLabel.CustomMinimumSize = new Vector2(TuningHintWidth, 0);
		hintLabel.Modulate = new Color(1, 1, 1, 0.65f);

		parent.AddChild(check);
		parent.AddChild(hintLabel);
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
		hintLabel.CustomMinimumSize = new Vector2(TuningHintWidth, 0);
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
		menuBox.AddChild(MakeButton("Cambiar de cámara", BackToSource, 260));
		menuBox.AddChild(MakeButton("Elegir origen (PC / móvil)", () => GoTo(AppState.SourceSelect), 260));
		menu.AddChild(menuBox);
		center.AddChild(menu);
		center.Visible = false;
		screen.AddChild(center);
		_menuRoot = center;

		// Pantalla de victoria (oculta hasta coger todas las estrellas)
		var winCenter = (CenterContainer)FullRect(new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore });
		var winPanel = MakePanel(0.95f);
		var winBox = new VBoxContainer();
		winBox.AddThemeConstantOverride("separation", 10);
		winBox.AddChild(MakeLabel("¡Has ganado!", 32));
		winBox.AddChild(MakeLabel("Has cogido todas las estrellas."));
		winBox.AddChild(MakeButton("Jugar de nuevo", RestartGame, 260));
		winPanel.AddChild(winBox);
		winCenter.AddChild(winPanel);
		winCenter.Visible = false;
		screen.AddChild(winCenter);
		_winRoot = winCenter;

		SetScreen(screen);
		UpdateHud();
	}

	private void ShowMenu(bool open)
	{
		if (_menuRoot != null) _menuRoot.Visible = open;
	}

	private void ShowWin(bool show)
	{
		if (_winRoot != null) _winRoot.Visible = show;
	}

	private void UpdateHud()
	{
		if (_hud != null)
			_hud.Text = $"{_mode.HudText}  |  V = {(_config.ShowDetection ? "ocultar" : "ver")} formas  |  Mover: A/D o flechas  |  Saltar: W o espacio  |  Esc = menú";
	}
}
