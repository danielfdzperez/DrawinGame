# Roadmap

## Hecho (prototipo de visión)
1. Elegir cámara, leer y mostrar.
2. Detectar dibujos negros sobre blanco, con superposición verde.
3. Trazos finos (contraste local).
4. Salida en polígonos con vértices (huecos incluidos).
5. Estabilización por forma, con respuesta inmediata al movimiento.
6. Selección del área con 4 puntos y corrección de perspectiva.

## Siguiente: Godot (en curso)
Hecho: proyecto creado, personaje, integración de cámara/visión y flujo de pantallas (cámara → esquinas → ajustes → juego). Ver `AIDocs/godot.md`.

1. Instalar Godot 4 .NET y crear el proyecto (ver `AIDocs/godot.md`).
2. Escena de juego mínima con un personaje que salta sobre un suelo fijo.
3. Convertir los polígonos del prototipo a colisiones: primero cargando un **JSON** de prueba exportado desde el prototipo.
4. Integrar la cámara y la visión en Godot (hilo de captura + cola de polígonos).
5. Reconstruir el nivel al cambiar el dibujo sin perder la posición/estado del jugador.

## Después
- Rojo = peligros (HSV, dos rangos de H).
- Calibración persistente (umbrales, esquinas del área) y pantalla de ajuste.
- Mantener la orientación del área estable al girar el folio.
- Exposición/balance de blancos de la webcam fijos.
- Empaquetado/exportación del juego para Windows.
