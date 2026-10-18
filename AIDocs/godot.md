# Godot: guía para principiante y plan de integración

El usuario **no ha usado Godot nunca**. Guiar paso a paso, indicando menús y nombres exactos, y avanzar en pasos pequeños que se puedan probar.

## 1. Instalación
- El usuario ya tiene **Godot 4.7.2 edición .NET (mono)** descomprimido en `F:\Proyectos\DrawinGame\Godot_v4.7.2\`. Ejecutable: `Godot_v4.7.2-stable_mono_win64.exe` (el `_console.exe` abre además una consola con los mensajes, útil para depurar). No hay instalador: se ejecuta directamente. La carpeta `GodotSharp` debe quedar junto al `.exe`.
- La carpeta `Godot_v*/` está en el `.gitignore` (el motor no se sube al repo).
- Hace falta el **.NET SDK** (ya está el 8). Comprobar en la documentación de Godot 4.7 qué versión de .NET exige; si pide una más nueva, instalarla.
- Editor de código opcional: Visual Studio, Rider o VS Code. Godot trae un editor de scripts básico, pero para C# conviene uno externo (Editor > Editor Settings > Dotnet > Editor > External Editor).

## 2. Crear el proyecto
1. Project Manager > **Create** > nombre `DrawinGame`, carpeta **`F:\Proyectos\DrawinGame\godot`** (dentro del repo, junto a `prototype/`).
2. Renderer: **Compatibility** (suficiente para 2D y más compatible) o Forward+. Version control: Git.
3. Con el proyecto abierto: menú **Project > Tools > C# > Create C# solution**. Genera el `.csproj` y el `.sln`.
4. Añadir al `.csproj` los paquetes de visión (`OpenCvSharp4`, `OpenCvSharp4.runtime.win`) cuando lleguemos a ese paso.

## 3. Conceptos básicos
- **Nodo**: unidad básica (un sprite, un cuerpo físico, una cámara...). Los nodos forman un árbol.
- **Escena**: un árbol de nodos guardado en `.tscn`. Una escena puede contener otras (el jugador es una escena dentro del nivel).
- **Script**: un `.cs` asociado a un nodo. La clase debe ser `partial`, heredar del tipo del nodo y **llamarse igual que el archivo** (`Player.cs` → `public partial class Player : CharacterBody2D`).
- **Señales**: eventos entre nodos.
- **Ciclo de vida**: `_Ready()` (al entrar en el árbol), `_Process(double delta)` (cada fotograma), `_PhysicsProcess(double delta)` (paso fijo de física; aquí va el movimiento).
- **Coordenadas 2D**: X a la derecha, **Y hacia abajo** (igual que las imágenes de OpenCV).
- Tras editar C#, hay que **compilar** (botón del martillo, "Build", arriba a la derecha) para que Godot vea los cambios.

## 4. Nodos físicos 2D que vamos a usar
| Nodo | Uso |
|---|---|
| `CharacterBody2D` | El jugador. Se mueve con `MoveAndSlide()`. |
| `StaticBody2D` | Plataformas (las formas negras). No se mueven. |
| `Area2D` | Zonas de peligro (formas rojas); detecta cuando el jugador entra. |
| `CollisionShape2D` / `CollisionPolygon2D` | Forma de colisión, hija de un cuerpo. |
| `Camera2D` | Cámara del juego. |

## 5. Ejercicios de aprendizaje (en orden)
1. **Escena vacía**: crear `Node2D` raíz, guardarla como `Main.tscn`, ejecutarla (F5, y fijarla como escena principal).
2. **Jugador**: escena `Player` con `CharacterBody2D` + `CollisionShape2D` (rectángulo) + `ColorRect` o `Sprite2D`. Script mínimo:
```csharp
using Godot;

public partial class Player : CharacterBody2D
{
    [Export] public float Speed = 300f;
    [Export] public float JumpVelocity = -450f;

    public override void _PhysicsProcess(double delta)
    {
        var v = Velocity;
        if (!IsOnFloor()) v += GetGravity() * (float)delta;
        if (Input.IsActionJustPressed("ui_accept") && IsOnFloor()) v.Y = JumpVelocity;
        v.X = Input.GetAxis("ui_left", "ui_right") * Speed;
        Velocity = v;
        MoveAndSlide();
    }
}
```
   (`ui_*` son acciones predefinidas: flechas y espacio/enter. Otras se crean en Project Settings > Input Map. `GetGravity()` existe en Godot 4.3+.)
3. **Suelo fijo**: `StaticBody2D` con `CollisionShape2D` ancho; comprobar que el jugador cae y salta.
4. **Polígono a mano**: `StaticBody2D` + `CollisionPolygon2D` con unos vértices; ver cómo colisiona.
5. **Nivel desde código**: crear `StaticBody2D` + `CollisionPolygon2D` por script a partir de una lista de vértices (JSON de prueba exportado del prototipo).
6. **Cámara y visión** dentro de Godot (ver sección 7).

## 6. De polígonos detectados a colisiones
Cada `DetectedShape` tiene `Outline` y `Holes` (vértices en píxeles del área, 900×636).
- Convertir `OpenCvSharp.Point` → `Vector2` y escalar al mundo: `mundo = pixel * factorEscala` (un único factor; decidir tamaño del nivel).
- Crear por forma un `StaticBody2D` con `CollisionPolygon2D { Polygon = vértices }`.
- **Huecos**: `CollisionPolygon2D` no admite huecos. Opciones:
  - `BuildMode = Segments`: colisión solo en las aristas (válido para huecos, formas cóncavas y trazos finos). Empezar por esta, por simplicidad. Riesgo: un cuerpo que quede dentro de una forma sólida no colisiona.
  - Restar huecos con `Geometry2D.ClipPolygons(exterior, hueco)` (devuelve polígonos resultantes) y usar `Solids` más adelante.
- Las formas cóncavas son aceptables en `CollisionPolygon2D` (Godot las descompone en convexas, `Geometry2D.DecomposePolygonInConvex`).
- Visualización: un `Polygon2D` con los mismos vértices para ver el nivel.
- Reconstrucción: al llegar polígonos nuevos, actualizar/reemplazar los cuerpos sin tocar al jugador (no recrear el nodo `Player`).

## 7. Integrar cámara y visión
- Reutilizar del prototipo, sin dependencias de WinForms: `AreaWarp.cs`, `DrawingDetector.cs` (`Detect`, sin `Paint` si no hace falta), `ShapeStabilizer.cs`. Copiarlos a `godot/vision/` cambiando el namespace.
- Hilo de captura propio (`Task`/`Thread`) con `VideoCapture`; deja las formas estables en una estructura protegida (lock o `ConcurrentQueue`).
- **Los nodos de Godot solo se tocan desde el hilo principal**: en `_Process` se lee lo último disponible y se actualiza el nivel; o desde el hilo con `CallDeferred`.
- Guardar el área de 4 puntos y los parámetros (umbral, contraste, simplificación, estabilidad) en un archivo de configuración. La selección de área necesitará una pantalla propia en Godot (clics sobre una textura de la cámara) o se reutiliza el prototipo para calibrar y exportar.
- Mostrar el vídeo de la cámara en Godot (depuración): convertir el `Mat` a `Image.CreateFromData(...)` y a `ImageTexture`.

## 8. Estructura de carpetas propuesta
```
DrawinGame/
├── CLAUDE.md
├── AIDocs/
├── prototype/        # prototipo WinForms (referencia y herramienta de calibración)
└── godot/            # proyecto Godot .NET
    ├── project.godot
    ├── DrawinGame.csproj
    ├── scenes/       # Main.tscn, Player.tscn...
    ├── scripts/      # Player.cs, LevelBuilder.cs...
    └── vision/       # AreaWarp.cs, DrawingDetector.cs, ShapeStabilizer.cs
```

## 9. Consejos
- Para depurar: `GD.Print("texto")` escribe en la consola de Godot.
- `.godot/` (caché) no se sube a git; el `.gitignore` ya lo cubre.
- Si C# da errores raros tras cambiar scripts: Build, y si persiste cerrar y reabrir el proyecto.

## 10. Estado de la integración (proyecto `godot/`)
- Proyecto Godot 4.7.2 .NET en `godot/` (nombre `DrawingGame`, escena principal `main.tscn`, nodos `Player` y `Floor` dentro de `Main`).
- `scripts/Player.cs`: personaje (flechas o A/D, espacio o W).
- `vision/`: `AreaWarp`, `DrawingDetector`, `ShapeStabilizer` (copiados del prototipo, namespace `DrawingGame.Vision`), `CameraService` (hilo de cámara: endereza el área, detecta, estabiliza y entrega formas e imagen), `CameraProbe` (busca cámaras y saca una miniatura de cada una).
- `scripts/Main.cs` (lógica y estados) + `scripts/MainUi.cs` (interfaz por código, clase parcial).

### Flujo de pantallas
1. **Cámara**: miniaturas de cada cámara (se buscan en un hilo); «Usar esta cámara».
2. **Esquinas**: clic en 4 esquinas sobre la imagen completa, arrastrables; «Continuar» se activa con las 4.
3. **Ajustes**: panel con deslizadores (umbral negro, contraste fino, simplificación, estabilidad) y el dibujo detectado en verde siempre visible; casilla para ver o no las formas durante el juego.
4. **Juego**: nivel con colisiones (`StaticBody2D` + `CollisionPolygon2D` en modo `Segments`). **Esc** abre el menú (ajustes, esquinas, cámara); **V** alterna ver/ocultar las formas.
- Configuración guardada en `user://config.json` (cámara, esquinas, parámetros, ver formas). En Windows: `%APPDATA%\Godot\app_userdata\DrawingGame\`.
- El personaje y el suelo de prueba (`Floor`) solo están activos en el juego. Si el personaje cae fuera de pantalla vuelve al punto de inicio.
- Pendiente de comprobar en el PC del usuario: carga de OpenCvSharp (DLL nativa) en Godot, rendimiento y calidad de las colisiones.
