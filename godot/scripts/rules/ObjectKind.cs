/// <summary>
/// Qué ES una figura dibujada dentro del juego (su papel). La visión solo sabe de formas y colores;
/// quien decide qué papel tiene cada una es un clasificador (ver IShapeClassifier y ColorRuleSet).
/// El orden de los valores no se debe cambiar: los recursos .tres guardan el número.
/// </summary>
public enum ObjectKind
{
	None,       // se ignora
	Platform,   // plataforma o pared: solo colisión
	Hazard,     // daño: colisión que hace perder al tocarla
	Star,       // coleccionable (se coloca en el centroide)
	Start       // punto donde empieza el jugador (centroide)
}
