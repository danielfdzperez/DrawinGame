using DrawingGame.Vision;
using Godot;

/// <summary>
/// Tabla «color → qué es» editable desde el editor de Godot (es un Resource: se guarda como .tres y se ve en el Inspector).
/// Hay varios juegos posibles con los mismos dibujos: cada uno usa su propio ColorRuleSet.
///   · Para que un color signifique otra cosa, cambia su desplegable.
///   · Para que el color NO importe (todo lo dibujado es lo mismo), marca «Ignore Color» y elige qué es.
/// Se asigna en la propiedad «Rules» del nodo Main (main.tscn). Ejemplos: res://rules/*.tres.
/// </summary>
[GlobalClass]
public partial class ColorRuleSet : Resource, IShapeClassifier
{
	[ExportGroup("Qué es cada color")]
	[Export] public ObjectKind Black { get; set; } = ObjectKind.Platform;
	[Export] public ObjectKind Red { get; set; } = ObjectKind.Hazard;
	[Export] public ObjectKind Yellow { get; set; } = ObjectKind.Star;
	[Export] public ObjectKind Green { get; set; } = ObjectKind.None;
	[Export] public ObjectKind Blue { get; set; } = ObjectKind.Start;

	[ExportGroup("Si el color no importa")]
	/// <summary>Si es true se ignora la tabla de arriba: toda figura, sea del color que sea, es «Any Color Kind».</summary>
	[Export] public bool IgnoreColor { get; set; } = false;
	[Export] public ObjectKind AnyColorKind { get; set; } = ObjectKind.Platform;

	public ObjectKind Classify(DetectedShape shape)
	{
		if (IgnoreColor) return AnyColorKind;
		return shape.Ink switch
		{
			InkColor.Black => Black,
			InkColor.Red => Red,
			InkColor.Yellow => Yellow,
			InkColor.Green => Green,
			InkColor.Blue => Blue,
			_ => ObjectKind.None
		};
	}

	/// <summary>Reglas por defecto (las del platformer) para cuando no hay ningún .tres asignado o no se puede cargar.</summary>
	public static ColorRuleSet CreateDefault() => new();
}
