using DrawingGame.Vision;
using Godot;

/// <summary>Un objeto del nivel creado a partir de una figura dibujada.</summary>
public sealed class LevelObject
{
	public int Id;                          // Id estable de la figura (lo da ShapeStabilizer)
	public ObjectKind Kind;
	public Node2D Node = null!;
	public DetectedShape Shape = null!;
}

/// <summary>
/// Mantiene los objetos de Godot sincronizados con las figuras detectadas. Solo se ocupa de ESO:
///   · NO decide qué significa cada color: se lo pregunta al clasificador (IShapeClassifier, p. ej. ColorRuleSet).
///   · NO decide cómo se gana ni qué se cuenta: eso es del modo de juego (IGameMode), que se engancha a los eventos de aquí.
/// Cada figura tiene un Id estable: si el Id sigue existiendo, el objeto de Godot se ACTUALIZA (no se destruye, así una
/// estrella ya cogida sigue cogida); si cambia mucho de sitio o de tamaño, o su tipo, se destruye y se crea uno nuevo;
/// si desaparece, se elimina.
/// </summary>
public partial class LevelManager : Node2D
{
	private const float RecreateDistance = 200f;     // px: salto de centroide a partir del cual es "otro objeto"
	private const double RecreateAreaMin = 0.4;      // rango de cambio de área que se considera el mismo objeto
	private const double RecreateAreaMax = 2.5;
	private const float StarMoveThreshold = 3f;      // px: la estrella solo se mueve si el centroide cambia más que esto

	private readonly Dictionary<int, LevelObject> _entries = new();
	private IShapeClassifier _classifier = ColorRuleSet.CreateDefault();
	private PackedScene? _starScene;
	private Vector2 _spawnLocal;

	/// <summary>Se acaba de crear un objeto (para que el modo de juego se enganche a él: p. ej. a una estrella).</summary>
	public event Action<LevelObject>? ObjectCreated;

	/// <summary>Cambió el conjunto de objetos (se creó o se eliminó alguno) o se vació el nivel. Se lanza una vez por sincronización.</summary>
	public event Action? LevelChanged;

	/// <summary>true = la estrella es el propio dibujo (sin gráfico); false = se pone el dibujo de scenes/star.tscn.</summary>
	public bool StarsAsDrawing { get; set; }

	public bool HasSpawn { get; private set; }
	public Vector2 SpawnPoint => ToGlobal(_spawnLocal);

	/// <summary>Objetos actuales del nivel.</summary>
	public IReadOnlyCollection<LevelObject> Objects => _entries.Values;

	/// <summary>Quién decide qué es cada figura. Los objetos ya creados se corrigen en la siguiente sincronización.</summary>
	public void SetClassifier(IShapeClassifier classifier) => _classifier = classifier;

	/// <summary>Quita todos los objetos (al empezar una partida nueva).</summary>
	public void Clear()
	{
		foreach (var entry in _entries.Values)
			entry.Node.QueueFree();
		_entries.Clear();
		HasSpawn = false;
		LevelChanged?.Invoke();
	}

	/// <summary>Sincroniza el nivel con las últimas figuras estables. Se llama desde el hilo principal.</summary>
	public void Apply(List<DetectedShape> shapes, int width, int height, Vector2 viewSize)
	{
		Position = ((viewSize - new Vector2(width, height)) / 2).Round();

		var alive = new HashSet<int>();
		bool changed = false;
		DetectedShape? bestStart = null;

		foreach (var shape in shapes)
		{
			var kind = _classifier.Classify(shape);
			if (kind == ObjectKind.None) continue;

			if (kind == ObjectKind.Start)
			{
				// Si hay varias figuras de inicio se usa la más grande
				if (bestStart == null || shape.Area > bestStart.Area) bestStart = shape;
				continue;
			}

			_entries.TryGetValue(shape.Id, out var entry);
			if (entry != null && (entry.Kind != kind || IsBigChange(entry.Shape, shape)))
			{
				RemoveEntry(shape.Id, entry);
				entry = null;
				changed = true;
			}

			if (entry == null)
			{
				entry = CreateEntry(kind, shape);
				_entries[shape.Id] = entry;
				changed = true;
				ObjectCreated?.Invoke(entry);
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
			RemoveEntry(id, _entries[id]);
			changed = true;
		}

		HasSpawn = bestStart != null;
		if (bestStart != null) _spawnLocal = ToVector(bestStart.Centroid);

		if (changed) LevelChanged?.Invoke();
	}

	// ------------------------------------------------------------ Objetos

	private LevelObject CreateEntry(ObjectKind kind, DetectedShape shape)
	{
		Node2D node;
		if (kind == ObjectKind.Star)
		{
			_starScene ??= GD.Load<PackedScene>("res://scenes/star.tscn");
			var star = _starScene?.Instantiate() as Star ?? new Star();
			star.Position = ToVector(shape.Centroid);   // la forma del dibujo no importa: solo dónde está
			star.Setup(StarRadius(shape), !StarsAsDrawing);
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
		return new LevelObject { Id = shape.Id, Kind = kind, Node = node, Shape = shape };
	}

	/// <summary>Radio de recogida: el de un círculo con el área del dibujo (si es el propio dibujo), o fijo si se pone una estrella.</summary>
	private float StarRadius(DetectedShape shape) =>
		StarsAsDrawing ? Mathf.Clamp((float)Math.Sqrt(Math.Max(shape.Area, 0) / Math.PI), 14f, 70f) : 24f;

	private void UpdateEntry(LevelObject entry, DetectedShape shape)
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

	private void RemoveEntry(int id, LevelObject entry)
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
