# DrawinGame – Reglas básicas para Claude

Juego de plataformas en el que **el nivel se dibuja a mano en un folio** (negro = plataformas, rojo = peligros). Una webcam apunta al folio y el programa construye el mapa de colisiones en tiempo real; si el dibujo cambia, el nivel se actualiza.

## Reglas básicas

- **Idioma**: responder y documentar siempre en **español**, de forma clara y directa.
- **Stack: C# puro.** Motor final: **Godot 4.x edición .NET**. Visión con **OpenCvSharp4**. Sin Python. Windows como plataforma inicial.
- **Estado**: existe un prototipo de visión en `prototype/` (WinForms + OpenCvSharp) que ya funciona. Es la base de la lógica de visión que pasará a Godot.
- **Colisiones**: polígonos con vértices (contornos simplificados), nunca cuadrícula de píxeles.
- **Hilos**: la cámara/visión corre en un hilo aparte y entrega polígonos mediante una estructura segura (cola/lock). En Godot, los nodos solo se tocan desde el hilo principal.
- **El usuario es nuevo en Godot**: guiarle paso a paso (qué menú pulsar, qué nodo crear), sin dar nada por sabido. Ver `AIDocs/godot.md`.
- **Método de trabajo**: pasos pequeños y funcionales antes de ampliar. Preguntar solo lo imprescindible; si hay una opción por defecto razonable, tomarla y mencionarla.
- **Claude no puede compilar ni probar aquí** (sin acceso a NuGet, sin cámara, sin Windows). El usuario compila y pega los errores. Por eso: escribir código con mucho cuidado y calificar los tipos ambiguos (`OpenCvSharp.Size`/`Point` frente a `System.Drawing`).
- **Sin webcam**: mantener siempre una vía para probar con una imagen cargada.
- **Contexto**: antes de programar, leer el documento de `AIDocs/` que corresponda a la tarea.
- **Documentación viva**: si cambia una decisión, un parámetro o el comportamiento, actualizar en el mismo cambio el md de `AIDocs/` afectado y el `README.md` de `prototype/`.

## Índice de documentación (`AIDocs/`)

Cada archivo amplía un tema concreto. Léelo solo cuando la tarea lo requiera.

| Tema | Documento |
|---|---|
| Entorno, dependencias y limitaciones del entorno de Claude | `AIDocs/entorno.md` |
| Prototipo C#: estructura, uso, qué se probó y qué se descartó | `AIDocs/prototipo.md` |
| Pipeline de visión: área, detección, polígonos, parámetros | `AIDocs/vision.md` |
| Filtros de imagen (ruido, iluminación, color) y fuentes | `AIDocs/filtros.md` |
| Auto-ajuste de parámetros y evaluación de redes neuronales (YOLO) | `AIDocs/auto-y-redes.md` |
| **Estado actual y traspaso entre conversaciones (leer primero)** | `AIDocs/estado-actual.md` |
| Estabilización de formas entre fotogramas | `AIDocs/estabilizacion.md` |
| Godot: guía para principiante y plan de integración | `AIDocs/godot.md` |
| Cámara del móvil: app Android, servidor en Godot, protocolo | `AIDocs/movil.md` |
| Decisiones tomadas y alternativas descartadas | `AIDocs/decisiones.md` |
| Roadmap y siguientes pasos | `AIDocs/roadmap.md` |
