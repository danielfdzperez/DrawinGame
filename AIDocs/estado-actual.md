# Estado actual y traspaso (2026-10-19)

> **ACTUALIZACIÓN 2026-10-19 (tarde)** — todo escrito a ciegas, pendiente de compilar y probar:
> 1. Prototipo: `DrawingDetector` v4 (`vision.md` §v4): máscara → color por píxel (`ClassifyInk`) → figuras por región de color; `SplitByColor` eliminado; `Paint` usa colores fijos por familia; `DetectedShape` tiene `Id`, `Centroid`, `Area`; `ShapeStabilizer` asigna Ids estables.
> 2. Godot (`godot.md` §11): `godot/vision` ya usa el detector nuevo; `LevelManager` (tabla color→tipo en `config.json`, un objeto por Id que se actualiza o recrea), `Star` + `scenes/star.tscn`, `Player.Died`; reglas: negro=plataforma, rojo=daño, amarillo=estrella, azul=inicio del jugador, verde=ninguna; victoria al coger todas las estrellas.
> 3. Lo de abajo sobre Godot («NO actualizado») y sobre `SplitByColor`/clasificación por figura queda obsoleto.


Documento para retomar el trabajo en otra conversación. Leer primero `CLAUDE.md` (reglas) y luego este archivo; el detalle técnico está en los demás `AIDocs/*.md`.

## 1. Proyecto
**DrawinGame**: plataformas donde el nivel se dibuja a mano en papel (negro = plataformas, colores = otros elementos), una webcam lo captura y se convierte en polígonos/colisiones en tiempo real. Stack final: **C# puro, Godot 4.7.2 .NET, OpenCvSharp4, Windows**. El usuario es nuevo en Godot (guiar paso a paso). Idioma de trabajo: **español**. Repo: `F:\Proyectos\DrawinGame` (`prototype/`, `godot/`, `AIDocs/`, `CLAUDE.md`).

## 2. Limitaciones del entorno de Claude (importante)
- Claude trabaja en la nube: **no puede compilar** (sin .NET/NuGet, sin cámara, sin Windows). Todo el código C# se escribe a ciegas; el usuario compila en su PC y pega los errores.
- Los cambios en archivos del usuario se hacen con `device_bash` (carpeta montada `$HOME/mnt/DrawinGame`). `rm` requiere `device_request_delete_permission`.
- Truco útil: sí hay Python + OpenCV (cv2) en la nube → se pueden **validar algoritmos con las capturas** que pega el usuario (recortar el panel filtrado y reproducir el pipeline) antes de portar a C#.
- Errores de compilación ya vistos: `Size`/`Point`/`Rect` ambiguos (calificar `OpenCvSharp.Size`, `OpenCvSharp.Point`, `System.Drawing.Size`); `Cv2.Accumulate` pide máscara `null`; un parámetro `out` no puede usarse dentro de una lambda (usar copia local).
- Convenciones: prototipo con espacios; scripts de Godot con TABS; mantener `AIDocs/` y `prototype/README.md` al día en el mismo cambio.

## 3. Estado del prototipo (`prototype/`, WinForms .NET 8 + OpenCvSharp4)
Funciona (el usuario lo prueba y manda capturas). Última compilación pendiente de confirmar tras el último cambio (5 colores).

### Interfaz
- **Tres vistas simultáneas**: izquierda = imagen filtrada (lo que recibe el algoritmo), centro = **máscara blanco/negro** (tinta aceptada; de ahí salen las figuras), derecha = imagen normal + figuras coloreadas (lo que ve el usuario; el área se marca aquí).
- Barra superior: cámara, Iniciar/Detener, Seleccionar/Aplicar área (4 clics arrastrables + enderezado con perspectiva), Borrar área, "Detectar dibujo".
- Panel lateral con sliders (valores por defecto **validados por el usuario**): Ruido temporal 0 · Ruido espacial 10 · Balance de blancos on · Corregir sombras 0 · CLAHE 4 · Realce de color 100 · **Sensibilidad automática** on (manual 19) · **Color automático** on (manual 7) · **Inferir formas** on · Simplificación 2 · Estabilidad 4.
- La barra de estado muestra recuento por color y los umbrales automáticos usados.

### Archivos
`Program.cs`, `MainForm.cs` (UI, hilo de captura), `AreaWarp.cs`, `ImageFilter.cs` (filtros), `DrawingDetector.cs` (detección), `ShapeStabilizer.cs` (estabilización, empareja solo el mismo color), `README.md`.

### Pipeline (por fotograma)
1. Captura (DirectShow) → si hay área aplicada, enderezado a 900×636.
2. `ImageFilter.Apply` (temporal EMA, bilateral, balance de blancos, iluminación Lab, CLAHE, realce de saturación).
3. `DrawingDetector.Detect(filtrada, ajustes, out máscara, out sens, out color)`:
   - Lab; **papel local** = mediana 41×41 a 1/4 de resolución + 2 iteraciones de media gaussiana (σ=12) que ignora la tinta.
   - **Distancia Lab** al papel (σ=1) → **histéresis**: baja = sensibilidad (auto: mediana + 4.5·σ con σ=1.4826·MAD, acotado 8–40), alta = ×1.8; cierre 3×3; una componente solo se acepta si contiene píxeles de la máscara alta.
   - Cada componente = una figura. **Negro/color**: diferencia media (a,b) tinta–papel < umbral de color (auto: mediana+4.5σ del mapa (a,b), acotado 6–30) → negro; si no, color por matiz HSV.
   - **Solo 5 colores** (decisión del usuario): Negro, Rojo (H 0–14 y 150–179), Amarillo (15–44), Verde (45–89), Azul (90–149). Naranja→amarillo, cian→verde/azul, morado/rosa→azul/rojo.
   - **División por color** (`SplitByColor`) cuando una componente junta objetos de distinto color que se tocan: píxeles claramente cromáticos (dif. (a,b) ≥ 2× umbral) caen en las 4 bandas, el resto = negro; una clase solo cuenta con cuerpo ≥ 200 px tras abrir 9×9; las semillas crecen 12 dilataciones para cubrir la región.
   - **Inferir formas** (`BuildShape`): mancha sólida (área ≥ 400, solidez ≥ 0.88, sin huecos grandes) → rectángulo girado (`MinAreaRect`, si rellena ≥ 85 %) o envolvente convexa simplificada; huecos < 8 % del área se ignoran; trazos/letras quedan con contorno crudo (ApproxPolyDP con Simplificación).
   - **Huecos**: solo reales si parecen papel (diferencia media < 0.5×sensibilidad) y, como garantía final, **ningún hueco donde la máscara blanco/negro no lo tenga** (regla fijada por el usuario).
4. `ShapeStabilizer.Update` (seguimiento por forma con distancia tipo Hausdorff; ruido ≤ tolerancia → se mantiene; movimiento global ≥ 50 % de vértices → instantáneo; cambio local necesita 3 fotogramas; aparece a los 2, desaparece a los 3).
5. `Paint`: relleno translúcido en el color, contorno y vértices; la UI muestra los tres bitmaps.

## 4. Historial resumido de decisiones
- Detección de papel automática (brillo / diferencia con fondo): **descartada**. Método actual: **4 clics manuales + warp**.
- Detección por bandas de matiz por píxel (v1, 9 colores): descartada (azul partido en cian/azul). v2 Lab+clasificación por figura. **v3 actual**: diferencia con el papel local + histéresis.
- k-means para separar colores: descartado (ruidoso, no determinista). Histograma de picos de matiz: sustituido por 4 bandas fijas.
- Redes neuronales: el usuario quiere una **ya entrenada** (no entrenar). Investigado: YOLO COCO no sirve y los de Ultralytics son AGPL-3.0; candidatas genéricas: **U²-Netp** (ONNX 4.4 MB, 320×320, Apache-2.0, "objeto saliente") y **MobileSAM/EdgeSAM** (~200 ms/imagen solo el codificador en iPhone 14; solo a 1–2 Hz como ayuda). **Nada integrado**; plan: probarlas offline en Python con fotogramas de `Videos/` y comparar su máscara con la del algoritmo. Ver `auto-y-redes.md` y `vision.md`.
- Auto-ajuste: sensibilidad y color ya automáticos. Filtros casi sin efecto con los valores validados (candidatos a fijarse/ocultarse). Pendiente: esconder sliders de filtros dejando solo Simplificación y Estabilidad.

## 5. Problemas abiertos / vigilar
1. **Puntos/polígonos pequeños dentro del rectángulo negro** (aparecían con holes dibujados). Ya aplicado: color suavizado σ=2, división más estricta (2× umbral), 5 colores fijos y garantía "sin huecos fuera de la máscara". **Si persisten**: desactivar la llamada a `SplitByColor` (comentarla) y ver si desaparecen; si no, buscar otra fuente (p. ej. el estabilizador conservando huecos antiguos).
2. Figuras que "mutan" (contorno cambia entre fotogramas): mitigado con inferencia de forma (rectángulo/convexa); falta un prior temporal (usar la forma estable previa para inferir la actual).
3. Letras ("Pedro", "HoLA") salen con muchos vértices con Simplificación 2; con valores mayores salen como triángulos. Falta decidir qué hacer con trazos finos para el juego.
4. Tinta azul oscura muy poco cromática puede salir negra (diferencia (a,b) ~8.6–10 vs umbral ~7): bajar "Color mínimo manual" a 5–6 si pasa.
5. Una mancha gris real en el papel se detecta como tinta (para el algoritmo lo es).
6. Dos trazos de distinto color pegados: la frontera es aproximada.

## 6. Godot (`godot/`) — NO actualizado con lo nuevo
- Proyecto Godot 4.7.2 .NET funcionando: `Player.cs` (flechas/WASD, espacio/W), `Main.cs` + `MainUi.cs` (flujo de 4 pantallas: cámara → 4 esquinas → ajustes → juego; Esc = menú), `vision/{AreaWarp,DrawingDetector,ShapeStabilizer,CameraService,CameraProbe}.cs` (namespace `DrawingGame.Vision`), config en `user://config.json`, cámara en hilo aparte.
- Esas copias de visión son del **detector antiguo (solo negro, verde)**. Cuando se valide el prototipo hay que **portar**: `ImageFilter`, el nuevo `DrawingDetector` (5 colores, máscara, auto-umbrales, inferencia de formas), el `ShapeStabilizer` con clase de color, y los ajustes de UI (sustituir sliders antiguos por los nuevos). Colisiones: `StaticBody2D` + `CollisionPolygon2D` (BuildMode Segments); para colores definir qué hace cada uno (negro = plataforma; rojo = peligro; resto = por decidir).
- Pendiente menor: fallback de apertura de cámara (probar índices y DSHOW/ANY) si vuelve "No se pudo abrir la cámara 0"; el motor de Godot debería estar fuera del repo (los AIDocs aún dicen `Godot_v4.7.2/` dentro).

## 7. Próximos pasos sugeridos
1. Compilar y probar el último cambio (5 colores + sin huecos fuera de máscara); confirmar que los puntos dentro del negro desaparecieron.
2. Estabilidad temporal con prior de forma; decidir tratamiento de trazos finos.
3. Opcional: probar U²-Netp / MobileSAM offline sobre `Videos/` y comparar máscaras.
4. Esconder sliders de filtros (valores fijos validados).
5. Portar el pipeline a Godot y definir la semántica de los 5 colores en el juego.
6. Actualizar `AIDocs/godot.md`, `roadmap.md` y `decisiones.md` con lo anterior.

## 8. Índice de documentación
`CLAUDE.md` → `AIDocs/{entorno,prototipo,vision,filtros,auto-y-redes,estabilizacion,godot,decisiones,roadmap,estado-actual}.md`. Nota: `vision.md` acumula secciones históricas (marcadas) y la versión vigente (v3 + correcciones + 5 colores).
