using Godot;

/// <summary>
/// Estrella coleccionable (color amarillo). Se coloca en el centroide de la figura dibujada y no usa su forma.
/// El aspecto está en scenes/star.tscn (se puede cambiar el dibujo ahí); si la escena no está, se construye una por código.
/// </summary>
public partial class Star : Area2D
{
	public bool Collected { get; private set; }

	private float _radius = 24f;
	private bool _showGraphic = true;
	private int _armToken;   // identifica el último rearme pedido (los anteriores se descartan)

	/// <summary>Se lanza cuando el jugador la toca.</summary>
	public event Action<Star>? Picked;

	public override void _Ready()
	{
		// Si la escena no trae colisión o dibujo (p. ej. se creó con "new Star()"), se crean aquí
		if (GetNodeOrNull<CollisionShape2D>("CollisionShape2D") == null)
			AddChild(new CollisionShape2D { Shape = new CircleShape2D { Radius = 24f } });
		if (GetNodeOrNull<Polygon2D>("Polygon2D") == null)
			AddChild(new Polygon2D { Polygon = MakeStar(23f, 9.5f), Color = new Color(1f, 0.84f, 0.1f) });

		ApplySetup();
		BodyEntered += OnBodyEntered;
	}

	/// <summary>
	/// Radio de la zona donde se coge y si se dibuja la estrella. Sin dibujo (showGraphic = false) la estrella
	/// es el propio dibujo amarillo del papel: aquí solo queda la zona de recogida invisible.
	/// </summary>
	public void Setup(float radius, bool showGraphic)
	{
		bool radiusChanged = Mathf.Abs(radius - _radius) > 1.5f;
		bool graphicChanged = showGraphic != _showGraphic;
		if (!radiusChanged && !graphicChanged) return;

		_radius = radius;
		_showGraphic = showGraphic;
		ApplySetup();
	}

	private void ApplySetup()
	{
		var collision = GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
		if (collision != null)
			collision.Shape = new CircleShape2D { Radius = _radius };   // forma nueva por estrella: la de la escena es compartida

		foreach (var child in GetChildren())
			if (child is Polygon2D polygon) polygon.Visible = _showGraphic;
	}

	private void OnBodyEntered(Node2D body)
	{
		if (Collected || body is not Player) return;
		Collect();
		Picked?.Invoke(this);
	}

	public void Collect()
	{
		_armToken++;   // cancela cualquier rearme pendiente
		Collected = true;
		Visible = false;
		SetDeferred(Area2D.PropertyName.Monitoring, false);
	}

	/// <summary>
	/// Vuelve a dejarla sin coger. Se muestra ya, pero la zona de recogida se reactiva DOS fotogramas de física después:
	/// así, si el jugador acaba de ser recolocado (reinicio), el motor de física ya tiene su posición nueva y la estrella
	/// no se «recoge» sola con la posición vieja.
	/// </summary>
	public async void ResetStar()
	{
		if (!Collected) return;

		Collected = false;
		Visible = true;
		int token = ++_armToken;

		if (!IsInsideTree()) return;
		await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
		if (!IsInsideTree()) return;
		await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);

		if (token == _armToken && !Collected && IsInsideTree())
			SetDeferred(Area2D.PropertyName.Monitoring, true);
	}

	private static Vector2[] MakeStar(float outer, float inner)
	{
		var pts = new Vector2[10];
		for (int i = 0; i < 10; i++)
		{
			float r = i % 2 == 0 ? outer : inner;
			float a = -Mathf.Pi / 2f + i * Mathf.Pi / 5f;
			pts[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
		}
		return pts;
	}
}
