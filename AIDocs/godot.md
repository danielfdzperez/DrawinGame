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

## 11. Visión nueva y nivel con objetos (2026-10-19)
`godot/vision/` ya usa el detector nuevo del prototipo (copias con solo el namespace cambiado: `ImageFilter`, `DrawingDetector`, `ShapeStabilizer`). `CameraService` filtra → detecta (máscara + color por píxel, ver `vision.md` v4) → estabiliza. Ajustes de UI: Simplificación, Estabilidad, Sensibilidad (0 = auto), Color mínimo (0 = auto).

### Datos detector → juego
No hay JSON: el hilo de cámara deja en memoria un `List<DetectedShape>` y `Main._Process` lo recoge con `TryGetShapes` (lock + número de versión). `DetectedShape` = `Outline`, `Holes`, `Ink` (Black/Red/Yellow/Green/Blue), `Color` (media real), **`Id` estable** (lo asigna `ShapeStabilizer` y se mantiene mientras la figura se reconoce como la misma), `Centroid`, `Area`. Con Estabilidad 0 el estabilizador no asigna Ids, por eso Godot usa como mínimo 1.

### Reglas por color
**Obsoleto desde §14**: la tabla ya no está en `config.json`; vive en un recurso `ColorRuleSet` (`rules/*.tres`) editable en el Inspector. Por defecto (`platformer.tres`): Black → Platform (StaticBody2D, solo colisión), Red → Hazard (grupo `hazard`; `Player` muere al tocarlo y reaparece), Yellow → Star (escena `scenes/star.tscn` + `Star.cs` en el centroide; ignora la forma), Blue → Start (el jugador aparece en su centroide), Green → None. Ver §14.

### Regenerador
`LevelManager.Apply` mantiene un objeto de Godot por `Id`: si el Id sigue, se actualiza (colisiones nuevas, o la estrella se mueve si el centroide cambia > 3 px) sin destruirlo; si cambia de tipo, el centroide salta > 200 px o el área sale del rango 0.4–2.5×, se destruye y se crea otro; si el Id desaparece, se elimina. Así una estrella cogida sigue cogida aunque el dibujo tiemble.

### Victoria
Al coger todas las estrellas que hay (mínimo una) sale «¡Has ganado!» con «Jugar de nuevo» (restaura las estrellas). HUD: `Estrellas cogidas/total`. Entrar en el juego (desde Ajustes, etc.) empieza partida nueva (`LevelManager.Clear`). La lógica de victoria está ahora en `StarCollectMode` (§14).

### Pendiente
- Escrito a ciegas: compilar en Godot (Build) y pegar errores. Abrir el proyecto una vez para que Godot genere los `.uid` de `Star.cs` y `LevelManager.cs`.
- Verde sin función; vidas (de momento morir = reaparecer); el dibujo de la estrella se puede cambiar en `star.tscn`.

## 12. Origen de la cámara: PC o móvil (2026-10-19)
Nueva primera pantalla (`SourceSelect`): «Cámara del PC» o «Cámara del móvil». Con el móvil, `RemoteWait` muestra un QR y espera a la app Android; al conectarse pasa a Esquinas. Detalle completo, protocolo y dudas en `AIDocs/movil.md`. Cambios: `vision/ICameraSource.cs`, `WebcamSource.cs`, `RemoteFrameReceiver.cs`, `IRemoteTransport.cs`, `TcpFrameTransport.cs` (nuevos; `CameraService.Start(ICameraSource)`), `Main`/`MainUi` (estados nuevos, «Girar imagen 90°» en Esquinas, menú del juego con «Elegir origen»), `AppConfig` (`Source`, `RemotePort`, `RemoteToken`, `RemoteRotation`), paquete NuGet `QRCoder`. Escrito a ciegas: compilar (Build) y pegar errores.

### Estrella como dibujo (opción `StarsAsDrawing`, por defecto activada)
Casilla en Ajustes: «Estrella = el propio dibujo amarillo (se borra al cogerla)». Con ella, la figura amarilla no se sustituye por el gráfico de `star.tscn` (se oculta; queda solo una zona de recogida invisible de radio √(área/π), entre 14 y 70 px). Al coger la estrella, `LevelManager.CollectedStarIds()` → `CameraService.SetErasedIds()`: la imagen mostrada rellena (inpainting Telea en una ventana pequeña) la zona de esa figura con el tono del papel de alrededor. Solo se borra la imagen mostrada: el detector sigue viendo la figura, así que la estrella conserva su Id y su estado «cogida». «Jugar de nuevo» o salir a Ajustes vuelve a mostrar el dibujo. Limitación: si la figura amarilla desaparece de la cámara más de 3 fotogramas (p. ej. la mano la tapa) se pierde su Id y la estrella se da por eliminada.

## 13. Pantalla de ajustes: el panel nunca tapa la imagen (2026-10-19)
**Fallo**: el panel tenía `CustomMinimumSize` de 340 px, pero el texto del `CheckBox` «Estrella = el propio dibujo amarillo (se borra al cogerla)» no se parte en líneas; la anchura mínima del contenido superaba los 340 px y el `PanelContainer` crecía, tapando parte de la imagen (que se había colocado a la izquierda de 340 px exactos).

**Ahora** (`MainUi.BuildTuningScreen`): panel pegado a la derecha con **ancho fijo** (`AnchorRight`, anclas `RightWide` + `OffsetLeft = -TuningPanelWidth`, `GrowHorizontal = Begin`, `ClipContents`), contenido dentro de un `ScrollContainer` (sin scroll horizontal), casillas con texto corto + ayuda en un `Label` con autowrap de `TuningHintWidth` (= panel − 56). `PreviewRect` reserva exactamente ese ancho, y la imagen entera cabe a la izquierda (TextureRect `KeepAspectCentered`).
- **Ocultar/mostrar**: botón «Ocultar ›» o tecla **H**. Oculto, el panel se reduce a una barra de 48 px (`TuningCollapsedWidth`) con un botón «‹»; la imagen usa todo el resto y no se solapa con nada.
- Nuevo deslizador «Grosor mínimo (px)» (`AppConfig.MinThickness`, por defecto 7).
- Regla para futuros paneles: nada con texto largo sin `AutowrapMode` dentro de un panel de ancho fijo; el ancho de la imagen se calcula en `PreviewRect`, no por el layout.

## 14. Separación de responsabilidades: reglas de color, nivel y modo de juego (2026-10-19)
Antes `LevelManager` lo hacía todo (traducir color→tipo, crear objetos, contar estrellas y decidir la victoria). Ahora son cuatro piezas, cada una con un único trabajo:

| Pieza | Archivo | Qué hace | Qué NO sabe |
|---|---|---|---|
| Visión | `vision/*` | Produce `DetectedShape` (contorno, huecos, `Ink`, `Id`, centroide, área) | Nada del juego |
| Clasificador | `scripts/rules/IShapeClassifier.cs`, `ColorRuleSet.cs` | **Único sitio** que traduce «lo que se ve» a un `ObjectKind` | Cómo se crean los objetos ni cómo se gana |
| Nivel | `scripts/LevelManager.cs` | Crea/actualiza/elimina los nodos de Godot por `Id`; lanza `ObjectCreated` y `LevelChanged` | Qué significa un color; reglas de victoria |
| Modo de juego | `scripts/modes/IGameMode.cs`, `StarCollectMode.cs` | Cuenta, decide la victoria, da el texto del HUD y qué dibujos borrar | Colores; cámara |
| Orquestador | `scripts/Main.cs` | Conecta todo; exporta `Rules` | — |

`ObjectKind` (`scripts/rules/ObjectKind.cs`): `None, Platform, Hazard, Star, Start`. **No cambiar el orden**: los `.tres` guardan el número.

### Cambiar cómo se mapean los colores desde el editor (paso a paso)
1. Abre el proyecto y pulsa **Build** (martillo, arriba a la derecha) para que Godot conozca la clase `ColorRuleSet`.
2. En el panel **FileSystem** (abajo a la izquierda) abre la carpeta `rules/` y haz **doble clic en `platformer.tres`**: el **Inspector** (derecha) muestra el grupo «Qué es cada color» con un desplegable por color (Black, Red, Yellow, Green, Blue). Cambia el que quieras; **Ctrl+S** guarda.
3. «Si el color no importa»: marca **Ignore Color** y elige en **Any Color Kind** qué es toda figura dibujada (se ignora la tabla de arriba). Ejemplo ya hecho: `rules/sin_color.tres`.
4. Para crear un juego nuevo con otras reglas: en FileSystem, clic derecho sobre `rules/` → **Create New → Resource…** → busca **ColorRuleSet** → Crear → nómbralo (`mi_juego.tres`) y edítalo como arriba.
5. Para usarlo: abre `main.tscn`, selecciona el nodo **Main** y en el Inspector, propiedad **Rules**, arrastra el `.tres` (o desplegable → Load). Si se deja vacía se usa `res://rules/platformer.tres` y, si no existe, los valores por defecto del código.

### Añadir otro tipo de juego
- Otro `IGameMode` (reglas distintas) y, si hace falta, otro `ColorRuleSet` o una clase propia que implemente `IShapeClassifier` (p. ej. por tamaño o forma, no solo por color). `Main._Ready` crea el modo (`new StarCollectMode(_level)`).
- Añadir un tipo de objeto: valor **al final** del enum `ObjectKind` + rama en `LevelManager.CreateEntry/UpdateEntry`.
- La clave `ColorRules` de `config.json` ya no se usa (se ignora si aparece).

### Bug del reinicio (estrella recién cogida ya cogida)
Síntoma: tras coger la última estrella y «Jugar de nuevo», esa estrella nacía cogida. **Causa probable (hipótesis, sin verificar en ejecución)**: al ganar se desactiva al jugador, que queda en el motor de física encima de la última estrella; al reiniciar, la estrella reactivaba su `Monitoring` antes de que el motor supiera que el jugador ya estaba en el punto de salida, y lo detectaba «dentro» al instante. Arreglo en dos capas: `Player.TeleportTo` comunica la posición nueva al motor al momento (`PhysicsServer2D.BodySetState`), y `Star.ResetStar` muestra la estrella pero reactiva la zona de recogida **dos fotogramas de física después** (con un token para descartar rearmes obsoletos). Orden en `Main.RestartGame`: recolocar jugador → activarlo → `_mode.Restart()`.
