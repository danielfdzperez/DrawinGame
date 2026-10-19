# Prototipo C# (`prototype/`)

Aplicación WinForms (.NET 8) + OpenCvSharp4 para validar la visión antes de pasar a Godot. Ejecutar: `cd prototype && dotnet run` (ver `prototype/README.md`).

## Archivos
| Archivo | Función |
|---|---|
| `Program.cs` | Punto de entrada. |
| `MainForm.cs` | Ventana, elección de cámara, selección del área con el ratón, bucle de captura en hilo aparte, controles de parámetros. |
| `AreaWarp.cs` | Ordena las 4 esquinas y endereza el área (perspectiva). |
| `ImageFilter.cs` | Filtros previos a la detección (ver `filtros.md`). |
| `DrawingDetector.cs` | Imagen filtrada → polígonos con color (`DetectedShape`) y dibujado en el color detectado. |
| `ShapeStabilizer.cs` | Estabilización temporal de formas. |

## Flujo en tiempo real
1. Lectura de fotograma (hilo de captura).
2. Si hay área aplicada: enderezado a 900×636 (A4). Si no, imagen completa.
3. `ImageFilter.Apply` → imagen filtrada (pantalla izquierda, la que ve el algoritmo).
3b. `DrawingDetector.Detect(filtrada)` → polígonos con color.
4. `ShapeStabilizer.Update` → polígonos estables.
5. `DrawingDetector.Paint` sobre la imagen normal → relleno translúcido, contorno y vértices en el color de cada figura (pantalla derecha, la del usuario).
6. Conversión de ambas a `Bitmap` y envío a sus `PictureBox` (hilo de UI).

## Qué se probó y qué se descartó
| Intento | Resultado |
|---|---|
| Detectar solo por umbral de gris | Trazos finos de rotulador salen grises en cámara: no se detectaban; subir el umbral detectaba sombras. → **Black-hat (contraste local)**. |
| Promediar vértices entre fotogramas | Inviable: `approxPolyDP` elige vértices distintos cada vez. → **seguimiento de forma completa**. |
| Estabilizador con espera fija de 5 fotogramas | Mucho retardo en objetos móviles. → **movimiento global aceptado al instante**. |
| Detectar el folio automáticamente por brillo (Otsu + cuadrilátero más grande) | No funcionó bien de forma fiable. **Descartado.** |
| Detectar el folio por diferencia con una foto del fondo | No convenció al usuario. **Descartado.** |
| **Seleccionar el área con 4 clics** | **Funciona de lujo. Es el método actual.** |

## Interfaz y valores por defecto
Tres vistas simultáneas (filtrada · máscara · usuario) + panel lateral de filtros y detección. Cámara (lista "Cámara N", sin nombres) · Iniciar/Detener · Seleccionar/Aplicar área · Borrar área · Detectar dibujo · Sensibilidad 18 · Simplificación 2 px · Estabilidad 4 px.

Valores añadidos: Ruido temporal 50 · Ruido espacial 3 · Balance blancos on · Corregir luz 80 · CLAHE 2 · Realce color 40 · Color mínimo (croma Lab) 18.

## Valores por defecto actuales (validados por el usuario, 2026-10-19)
Ruido temporal 0 · Ruido espacial 10 · Balance de blancos on · Corregir sombras 0 · CLAHE 4 · Realce de color 100 · Sensibilidad 19 · Color mínimo 30 · Simplificación 2 · Estabilidad 4.
Nota: el usuario desactiva el promedio temporal y la corrección de iluminación (el temporal difumina objetos en movimiento y el papel ya se estima localmente en el detector).
