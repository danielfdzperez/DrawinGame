# Decisiones y alternativas descartadas

## Tomadas
- **C# puro con Godot 4 .NET.** Un solo lenguaje y un solo proceso; Godot trabaja mejor con C#. OpenCvSharp4 para la visión.
- **Colisiones por polígonos con vértices** (contorno → `approxPolyDP`), no por cuadrícula de píxeles: bordes limpios, mejor rendimiento y poder tratar cada forma distinto. Es lo que Godot usa (`CollisionPolygon2D`, `Polygon2D`, segmentos).
- **Área de interés marcada a mano con 4 clics**, enderezada con perspectiva; lo de dentro se asume todo relevante.
- **Detección de trazos finos por contraste local** (black-hat) además del umbral absoluto.
- **Estabilización a nivel de forma** con aceptación instantánea del movimiento global.
- **Capturar y procesar en un hilo aparte**, entregando datos por estructura segura.

## Descartadas
- **Python** (OpenCV + pygame + pymunk + PyInstaller): se probó la idea, pero el destino es Godot, que va mejor con C#.
- **Godot + proceso Python** comunicados por UDP/WebSocket: dos procesos, más complejo.
- **Detección automática del folio por brillo** y **por diferencia con el fondo**: no funcionaron bien / no convencieron. Sustituidas por selección manual.
- **Promedio de vértices** para estabilizar: inviable por el cambio de vértices entre fotogramas.
