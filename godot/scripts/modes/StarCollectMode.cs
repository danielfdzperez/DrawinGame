using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Modo «coger todas las estrellas»: se gana al coger todas las estrellas que haya (mínimo una).
/// Una estrella es cualquier objeto del nivel de tipo ObjectKind.Star; qué color es lo decide el clasificador, no este modo.
/// </summary>
public sealed class StarCollectMode : IGameMode
{
	private readonly LevelManager _level;
	private bool _won;

	public event Action? Changed;
	public event Action? Won;

	public StarCollectMode(LevelManager level)
	{
		_level = level;
		_level.ObjectCreated += OnObjectCreated;
		_level.LevelChanged += OnLevelChanged;
	}

	private IEnumerable<(int Id, Star Star)> Stars() =>
		_level.Objects.Where(o => o.Kind == ObjectKind.Star && o.Node is Star).Select(o => (o.Id, (Star)o.Node));

	private int Total => Stars().Count();
	private int Collected => Stars().Count(s => s.Star.Collected);

	public string HudText => $"Estrellas {Collected}/{Total}";

	public void Restart()
	{
		_won = false;
		foreach (var (_, star) in Stars())
			star.ResetStar();
		Changed?.Invoke();
	}

	public int[] HiddenShapeIds() =>
		_level.StarsAsDrawing
			? Stars().Where(s => s.Star.Collected).Select(s => s.Id).ToArray()
			: Array.Empty<int>();

	private void OnObjectCreated(LevelObject obj)
	{
		if (obj.Node is Star star)
			star.Picked += OnStarPicked;
	}

	private void OnLevelChanged()
	{
		if (Total == 0) _won = false;   // nivel vacío o recién vaciado: partida nueva
		Changed?.Invoke();
		CheckWin();
	}

	private void OnStarPicked(Star star)
	{
		Changed?.Invoke();
		CheckWin();
	}

	private void CheckWin()
	{
		if (_won) return;
		int total = Total;
		if (total > 0 && Collected == total)
		{
			_won = true;
			Won?.Invoke();
		}
	}
}
