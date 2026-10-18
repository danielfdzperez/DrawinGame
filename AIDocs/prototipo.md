# Prototipo C# (`prototype/`)

Aplicación WinForms (.NET 8) + OpenCvSharp4 para validar la visión antes de pasar a Godot. Ejecutar: `cd prototype && dotnet run` (ver `prototype/README.md`).

## Archivos
| Archivo | Función |
|---|---|
| `Program.cs` | Punto de entrada. |
| `MainForm.cs` | Ventana, elección de cámara, selección del área con el ratón, bucle de captura en hilo aparte, controles de parámetros. |
| `AreaWarp.cs` | Ordena las 4 esquinas y endereza el área (perspectiva). |
| `DrawingDetector.cs` | Imagen → polígonos (`DetectedShape`) y dibujado de la superposición verde. |
| `ShapeStabilizer.cs` | Estabilización temporal de formas. |

## Flujo en tiempo real
1. Lectura de fotograma (hilo de captura).
2. Si hay área aplicada: enderezado a 900×636 (A4). Si no, imagen completa.
3. `DrawingDetector.Detect` → polígonos.
4. `ShapeStabilizer.Update` → polígonos estables.
5. `DrawingDetector.Paint` → relleno verde translúcido + contorno + vértices amarillos.
6. Conversión a `Bitmap` y envío al `PictureBox` (hilo de UI).

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
Cámara (lista "Cámara N", sin nombres) · Iniciar/Detener · Seleccionar/Aplicar área · Borrar área · Detectar dibujo · Umbral negro 100 · Contraste fino 40 · Simplificación 3 px · Estabilidad 4 px.
