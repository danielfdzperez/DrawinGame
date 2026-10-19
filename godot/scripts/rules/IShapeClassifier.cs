using DrawingGame.Vision;

/// <summary>
/// Decide qué papel (ObjectKind) tiene una figura detectada. Es el ÚNICO sitio donde se traduce «lo que se ve»
/// (color, forma, tamaño...) a «lo que significa en el juego». Cada tipo de juego puede traer su propio clasificador;
/// el nivel (LevelManager) no sabe nada de colores.
/// </summary>
public interface IShapeClassifier
{
	ObjectKind Classify(DetectedShape shape);
}
