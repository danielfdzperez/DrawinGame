# Menos ajustes manuales: auto-ajuste y red neuronal

Contexto (2026-10-19): el usuario encuentra costoso ajustar parámetros a mano y quiere explorar (1) auto-ajuste y (2) una red pequeña preentrenada tipo YOLO.

## Ruta 1: auto-ajuste (implementado en parte)
- **Sensibilidad automática** (hecho): estadística robusta sobre la distancia al papel. mediana = nivel del papel; σ = 1.4826·MAD; umbral = mediana + 4.5σ, acotado a [8,40]. Es el mismo principio que `detect_threshold` de photutils (fondo + n·σ con σ estimada de forma robusta). Con la captura real da ≈18, y el usuario ajustó a mano 19.
- **Color automático** (hecho): negro vs color se decide por la diferencia (a,b) entre la tinta y el papel local (no por croma absoluto), con umbral = mediana + 4.5σ del ruido de color del papel (≈7). Ver `vision.md`.
- **Filtros**: con los valores validados (temporal 0, iluminación 0) casi no aportan; candidatos a fijarse y ocultarse de la UI. Ruido espacial adaptable con una estimación de ruido (Immerkær).
- **Valores validados por el usuario**: ver `prototipo.md`.

## Ruta 2: red neuronal (investigado, NO implementado)
- No existe un modelo YOLO preentrenado que sirva tal cual: los modelos COCO detectan personas/coches, no manchas de rotulador. Habría que **entrenar** uno propio (segmentación de instancias).
- Los datos se pueden generar casi gratis: el detector clásico actual sirve de **auto-etiquetador** (imagen + polígonos + color), más aumentos de luz/ruido, revisando a mano lo dudoso.
- Inferencia en C#: ONNX Runtime; librería `YoloDotNet` (MIT, .NET 8, CPU/CUDA/DirectML/OpenVINO, segmentación v8/v11/v12/v26, `GetContourPoints()`); no incluye modelos.
- **Licencia**: los modelos/código de Ultralytics (YOLOv5/8/11/26) son **AGPL-3.0** (o licencia comercial de pago) → riesgo para un juego distribuido. Alternativas permisivas: RF-DETR (Apache-2.0), YOLOX (Apache-2.0), o una U-Net pequeña propia exportada a ONNX.
- Coste en tiempo real: CPU de portátil y 30 fps con cámara + juego → hay que medirlo; una U-Net mínima de máscara + el pipeline actual para vértices/color suele ser más barata que una YOLO-seg completa.
- Recomendación: primero agotar la ruta 1; si hace falta, entrenar una red de **máscara** (tinta sí/no) con auto-etiquetas y mantener contornos/color/estabilizador clásicos.

## Fuentes
- YoloDotNet (NuGet): https://www.nuget.org/packages/YoloDotNet
- Riesgo AGPL-3.0 en visión por computador (Roboflow): https://blog.roboflow.com/why-agpl-3-0-is-a-risk-for-computer-vision-teams/
- Licencia Ultralytics: https://ultralytics.com/license
- photutils detect_threshold (umbral = fondo + n·σ robusta): https://photutils.readthedocs.io/en/stable/api/photutils.segmentation.detect_threshold.html
- ONNX Runtime C#: https://onnxruntime.ai/docs/tutorials/csharp/yolov3_object_detection_csharp.html

## Actualización 2026-10-19: preferencia por red YA entrenada
El usuario no quiere entrenar una red propia. Candidatas preentrenadas y sus límites en `vision.md` (sección "Redes ya entrenadas"). Pendiente: probarlas offline sobre vídeos reales.
Fuentes: https://huggingface.co/edgetools/u2netp · https://arxiv.org/html/2312.06660v2
