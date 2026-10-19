using Godot;

public partial class Player : CharacterBody2D
{
	[Export] public float Speed = 300f;
	[Export] public float JumpVelocity = -450f;

	/// <summary>Se lanza cuando el jugador toca algo del grupo "hazard" (figuras rojas).</summary>
	public event Action? Died;

	private bool _wWasDown;

	/// <summary>
	/// Coloca al jugador en un sitio y lo para. También se lo comunica YA al motor de física: Godot aplica los cambios de
	/// posición con retraso, y sin esto las zonas (estrellas) seguirían «viendo» al jugador en el sitio anterior.
	/// </summary>
	public void TeleportTo(Vector2 position)
	{
		GlobalPosition = position;
		Velocity = Vector2.Zero;
		PhysicsServer2D.BodySetState(GetRid(), PhysicsServer2D.BodyState.Transform, GlobalTransform);
	}

	public override void _PhysicsProcess(double delta)
	{
		var v = Velocity;

		// Gravedad cuando no está en el suelo
		if (!IsOnFloor())
			v += GetGravity() * (float)delta;

		// Salto: espacio / enter, o W (solo al pulsar, no al mantener)
		bool wDown = Input.IsPhysicalKeyPressed(Key.W);
		bool jumpPressed = Input.IsActionJustPressed("ui_accept") || (wDown && !_wWasDown);
		_wWasDown = wDown;
		if (jumpPressed && IsOnFloor())
			v.Y = JumpVelocity;

		// Movimiento horizontal: flechas o A / D
		float dir = Input.GetAxis("ui_left", "ui_right");
		if (Input.IsPhysicalKeyPressed(Key.A)) dir -= 1f;
		if (Input.IsPhysicalKeyPressed(Key.D)) dir += 1f;
		v.X = Mathf.Clamp(dir, -1f, 1f) * Speed;

		Velocity = v;
		MoveAndSlide();

		// Daño: tocar un cuerpo del grupo "hazard"
		for (int i = 0; i < GetSlideCollisionCount(); i++)
		{
			if (GetSlideCollision(i).GetCollider() is Node node && node.IsInGroup("hazard"))
			{
				Died?.Invoke();
				break;
			}
		}
	}
}
