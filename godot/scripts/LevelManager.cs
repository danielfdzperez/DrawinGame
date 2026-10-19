using DrawingGame.Vision;
using Godot;

/// <summary>Qué objeto del juego representa un color.</summary>
public enum ObjectKind
{
	None,       // se ignora
	Platform,   // plataforma o pared: solo colisión
	Hazard,     // daño: colisión que hace perder al tocarla
	Star,       // estrella coleccionable (se coloca en el centroide)
	Start       // punto donde empieza el jugador (centroide)
}

/// <summary>Tabla color → tipo de objeto. Se guarda en config.json como texto para poder editarla a mano.</summary>
public static class LevelRules
{
	public static Dictionary<string, string> Defaults() => new()
	{
		["Black"] = "Platform",
		["Red"] = "Hazard",
		["Yellow"] = "Star",
		["Blue"] = "Start",
		["Green"] = "None"
	};

	public static Dictionary<InkColor, ObjectKind> Parse(Dictionary<string, string>? text)
	{
		var rules = new Dictionary<InkColor, ObjectKind>();
		if (text == null) return rules;
		foreach (var pair in text)
			if (Enum.TryParse<InkColor>(pair.Key, true, out var ink) && Enum.TryParse<ObjectKind>(pair.Value, true, out var kind))
				rules[ink] = kind;
		return rules;
	}
}

/// <summary>
/// Mantiene los objetos del nivel sincronizados con las figuras detectadas.
/// Cada figura tiene un Id estable (lo da ShapeStabilizer): si el Id sigue existiendo, el objeto de Godot se ACTUALIZA
/// (no se destruye, así una estrella ya cogida sigue cogida); si cambia mucho de sitio o de tamaño, o su tipo, se
/// destruye y se crea uno nuevo; si desaparece, se elimina.
/// </summary>
public partial class LevelManager : Node2D
{
	private const float RecreateDistance = 200f;     // px: salto de centroide a partir del cual es "otro objeto"
	private const double RecreateAreaMin = 0.4;      // rango de cambio de área que se considera el mismo objeto
	private const double RecreateAreaMax = 2.5;
	private const float StarMoveThreshold = 3f;      // px: la estrella solo se mueve si el centroide cambia más que esto

	private sealed class Entry
	{
		public ObjectKind Kind;
		public Node2D Node = null!;
		public DetectedShape Shape = null!;
	}

	private readonly Dictionary<int, Entry> _entries = new();
	private Dictionary<InkColor, ObjectKind> _rules = LevelRules.Parse(LevelRules.Defaults());
	private PackedScene? _starScene;
	private Vector2 _spawnLocal;
	private bool _winFired;

	/// <summary>Cambió el número de estrellas o se cogió alguna.</summary>
	public event Action? StarsChanged;

	/// <summary>Se han cogido todas las estrellas que hay (y hay al menos una).</summary>
	public event Action? AllStarsCollected;

	/// <summary>true = la estrella es el propio dibujo amarillo (sin gráfico); false = se pone el dibujo de scenes/star.tscn.</summary>
	public bool StarsAsDrawing { get; set; }

	public bool HasSpawn { get; private set; }
	public Vector2 SpawnPoint => ToGlobal(_spawnLocal);

	public int StarsTotal => _entries.Values.Count(e => e.Kind == ObjectKind.Star);
	public int StarsCollected => _entries.Values.Count(e => e.Kind == ObjectKind.Star && ((Star)e.Node).Collected);

	public void SetRules(Dictionary<InkColor, ObjectKind> rules) => _rules = rules;

	/// <summary>Ids de las estrellas ya cogidas (para borrar su dibujo de la imagen).</summary>
	public int[] CollectedStarIds() =>
		_entries.Where(p => p.Value.Kind == ObjectKind.Star && ((Star)p.Value.Node).Collected).Select(p => p.Key).ToArray();

	/// <summary>Quita todos los objetos (al empezar una partida nueva).</summary>
	public void Clear()
	{
		foreach (var entry in _entries.Values)
			entry.Node.QueueFree();
		_entries.Clear();
		HasSpawn = false;
		_winFired = false;
		StarsChanged?.Invoke();
	}

	/// <summary>Vuelve a dejar todas las estrellas sin coger.</summary>
	public void ResetStars()
	{
		foreach (var entry in _entries.Values)
			if (entry.Node is Star star) star.ResetStar();
		_winFired = false;
		StarsChanged?.Invoke();
	}

	/// <summary>Sincroniza el nivel con las últimas figuras estables. Se llama desde el hilo principal.</summary>
	public void Apply(List<DetectedShape> shapes, int width, int height, Vector2 viewSize)
	{
		Position = ((viewSize - new Vector2(width, height)) / 2).Round();

		var alive = new HashSet<int>();
		bool starsChanged = false;
		DetectedShape? bestStart = null;

		foreach (var shape in shapes)
		{
			if (!_rules.TryGetValue(shape.Ink, out var kind) || kind == ObjectKind.None) continue;

			if (kind == ObjectKind.Start)
			{
				// Si hay varias figuras de inicio se usa la más grande
				if (bestStart == null || shape.Area > bestStart.Area) bestStart = shape;
				continue;
			}

			_entries.TryGetValue(shape.Id, out var entry);
			if (entry != null && (entry.Kind != kind || IsBigChange(entry.Shape, shape)))
			{
				if (entry.Kind == ObjectKind.Star) starsChanged = true;
				RemoveEntry(shape.Id, entry);
				entry = null;
			}

			if (entry == null)
			{
				entry = CreateEntry(kind, shape);
				_entries[shape.Id] = entry;
				if (kind == ObjectKind.Star) starsChanged = true;
			}
			else if (!ReferenceEquals(entry.Shape, shape))
			{
				UpdateEntry(entry, shape);
			}

			entry.Shape = shape;
			alive.Add(shape.Id);
		}

		// Lo que ya no está dibujado desaparece
		foreach (int id in _entries.Keys.Where(id => !alive.Contains(id)).ToList())
		{
			var entry = _entries[id];
			if (entry.Kind == ObjectKind.Star) starsChanged = true;
			RemoveEntry(id, entry);
		}

		HasSpawn = bestStart != null;
		if (bestStart != null) _spawnLocal = ToVector(bestStart.Centroid);

		if (starsChanged)
		{
			StarsChanged?.Invoke();
			CheckWin();
		}
	}

	// ------------------------------------------------------------ Objetos

	private Entry CreateEntry(ObjectKind kind, DetectedShape shape)
	{
		Node2D node;
		if (kind == ObjectKind.Star)
		{
			_starScene ??= GD.Load<PackedScene>("res://scenes/star.tscn");
			var star = _starScene?.Instantiate() as Star ?? new Star();
			star.Position = ToVector(shape.Centroid);   // la forma del dibujo no importa: solo dónde está
			star.Setup(StarRadius(shape), !StarsAsDrawing);
			star.Picked += OnStarPicked;
			node = star;
		}
		else
		{
			// Plataforma y daño: solo colisión (la imagen de la cámara ya muestra el dibujo)
			var body = new StaticBody2D();
			FillCollisions(body, shape);
			if (kind == ObjectKind.Hazard) body.AddToGroup("hazard");
			node = body;
		}

		AddChild(node);
		return new Entry { Kind = kind, Node = node, Shape = shape };
	}

	/// <summary>Radio de recogida: el de un círculo con el área del dibujo (si es el propio dibujo), o fijo si se pone una estrella.</summary>
	private float StarRadius(DetectedShape shape) =>
		StarsAsDrawing ? Mathf.Clamp((float)Math.Sqrt(Math.Max(shape.Area, 0) / Math.PI), 14f, 70f) : 24f;

	private void UpdateEntry(Entry entry, DetectedShape shape)
	{
		if (entry.Node is Star star)
		{
			var target = ToVector(shape.Centroid);
			if (star.Position.DistanceTo(target) > StarMoveThreshold) star.Position = target;
			star.Setup(StarRadius(shape), !StarsAsDrawing);
		}
		else if (entry.Node is StaticBody2D body)
		{
			FillCollisions(body, shape);
		}
	}

	private void RemoveEntry(int id, Entry entry)
	{
		_entries.Remove(id);
		entry.Node.QueueFree();
	}

	private static bool IsBigChange(DetectedShape old, DetectedShape now)
	{
		if (ToVector(old.Centroid).DistanceTo(ToVector(now.Centroid)) > RecreateDistance) return true;
		double oldArea = old.Area;
		if (oldArea <= 0) return false;
		double ratio = now.Area / oldArea;
		return ratio < RecreateAreaMin || ratio > RecreateAreaMax;
	}

	private void OnStarPicked(Star star)
	{
		StarsChanged?.Invoke();
		CheckWin();
	}

	private void CheckWin()
	{
		if (_winFired) return;
		int total = StarsTotal;
		if (total > 0 && StarsCollected == total)
		{
			_winFired = true;
			AllStarsCollected?.Invoke();
		}
	}

	// ------------------------------------------------------------ Colisiones

	private static void FillCollisions(StaticBody2D body, DetectedShape shape)
	{
		foreach (var child in body.GetChildren())
		{
			body.RemoveChild(child);
			child.QueueFree();
		}

		body.AddChild(MakeCollision(shape.Outline));
		foreach (var hole in shape.Holes)
			body.AddChild(MakeCollision(hole));
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

	private static Vector2 ToVector(OpenCvSharp.Point2d p) => new((float)p.X, (float)p.Y);
}
