using System;

/// <summary>
/// Las REGLAS de una partida (cómo se gana, qué se cuenta, qué se enseña en el HUD). No sabe de cámara ni de colores:
/// trabaja con los objetos que el nivel (LevelManager) ya ha creado según el clasificador activo.
/// Para otro tipo de juego se escribe otra implementación y Main la usa en su lugar.
/// </summary>
public interface IGameMode
{
	/// <summary>Texto de progreso para el HUD (p. ej. «Estrellas 1/3»).</summary>
	string HudText { get; }

	/// <summary>Cambió algo que el HUD o la imagen mostrada deben reflejar.</summary>
	event Action? Changed;

	/// <summary>Se ha cumplido la condición de victoria.</summary>
	event Action? Won;

	/// <summary>Vuelve a empezar la partida con el mismo nivel dibujado.</summary>
	void Restart();

	/// <summary>Ids de las figuras cuyo dibujo hay que borrar de la imagen mostrada (p. ej. las estrellas ya cogidas).</summary>
	int[] HiddenShapeIds();
}
