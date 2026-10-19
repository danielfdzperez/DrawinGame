# Pipeline de visión

Entrada: fotograma BGR de la webcam. Salida: lista de `DetectedShape` (contorno exterior + huecos, vértices en píxeles de la imagen del área).

## 1. Área de interés (`AreaWarp`)
- El usuario marca **4 esquinas** sobre la imagen de la cámara (clic; arrastrar para ajustar). Coordenadas en píxeles de la imagen de cámara; el `PictureBox` está en modo Zoom, así que se convierten con escala y desplazamiento (`TryGetViewTransform`).
- Orden de esquinas: arriba-izq (mínimo x+y), abajo-der (máximo x+y), arriba-der (mínimo y−x), abajo-izq (máximo y−x).
- `GetPerspectiveTransform` + `WarpPerspective` a **900×636** (apaisado, proporción A4) o **636×900** (vertical) según los lados. Todo lo de fuera se descarta; todo lo de dentro se asume de interés.
- Limitación: si el área no es A4 sale estirada; si el área se gira ~45° puede cambiar entre apaisado y vertical.

## 2. (HISTÓRICO, sustituido por v3 más abajo) Máscara de trazos negros
1. Gris + `GaussianBlur` 3×3.
2. **Umbral absoluto** (`BinaryInv`, valor "Umbral negro", def. 100): trazos gruesos y rellenos.
3. **Contraste local / black-hat** (kernel elíptico 15×15, valor "Contraste fino", def. 40; 0 = desactivado): detecta lo más oscuro que su entorno y más estrecho que ~15 px, aunque en cámara se vea gris. No detecta zonas grises grandes (sombras).
4. Máscara final = OR de ambas.

## 3. Contornos → polígonos
- `FindContours` con `RetrievalModes.CComp` (nivel 1 = exteriores, nivel 2 = huecos) y `ApproxSimple`.
- Se descartan formas con `max(área, perímetro/2) < 20` (el perímetro/2 protege a los trazos finos, de área pequeña).
- Huecos con área < 20 también se descartan.
- `ApproxPolyDP` (Douglas-Peucker) con epsilon = "Simplificación" (def. 3 px).
- Un trazo fino sale como un polígono estrecho que rodea la línea (válido para colisiones; alternativa futura: línea central + grosor).

## 4. Dibujado de depuración (`Paint`)
Relleno verde translúcido (`FillPoly` con todos los anillos, los huecos quedan libres), contorno verde, vértices amarillos.

## Por hacer
- Detección de **rojo** (peligros): HSV con dos rangos de H (~0–10 y ~170–180).
- Distinguir negro (plataforma) de rojo en la salida.
- Calibración/persistencia de umbrales y esquinas del área.
- Bloquear exposición/balance de blancos de la webcam si es posible.

## Detección por colores (v3: diferencia con el papel local)
Historial: v1 = bandas de matiz por píxel (azul partido en cian/azul); v2 = Lab + clasificación por figura (mejor, pero el negro/lápiz y los trazos finos seguían saliendo fragmentados, porque se umbralizaba luminosidad y croma por separado). **v3 es la actual.**

1. Entrada: salida de `ImageFilter` (ver `filtros.md`), pasada a Lab.
2. **Papel local**: mediana 41×41 por canal a 1/4 de resolución; luego 2 iteraciones de media gaussiana (σ=12 a 1/4) que ignora los píxeles ya vistos como tinta (convolución normalizada). Resultado: color del papel en cada punto, aguanta degradados de luz.
3. **Distancia** Lab (euclídea) entre cada píxel y el papel, suavizada σ=1. Negro, lápiz gris y colores pálidos son simplemente "distintos del papel".
4. **Histéresis**: máscara baja (dif ≥ Sensibilidad, def. 18) y alta (×1.8). Se acepta una componente conectada de la baja solo si contiene algún píxel de la alta. Cierre 3×3. Así las letras finas no se rompen y el ruido del papel (dif ≈ 5–13) no entra.
5. Cada componente = una figura. Se mide la diferencia media de color (a,b de Lab) entre su tinta (píxeles de la máscara alta) y el papel local: < "Color mínimo" = negro (funciona aunque el papel tenga tinte); si no, familia por matiz HSV del color medio: rojo 0–8/172–179, naranja 9–20, amarillo 21–35, verde 36–79, cian 80–95, azul 96–130, morado 131–155, rosa 156–171.
6. Se descartan componentes con max(área, perímetro/2) < MinSize, y huecos con área < max(MinSize, 80).
7. **La máscara en blanco y negro** (solo componentes aceptadas, con huecos) se muestra en la vista central: es exactamente de donde salen las figuras.

Datos medidos en una captura real (Lab): papel dif 5 (p95 10, p99 13); lápiz/letras 10–60; rectángulo celeste 45; negro 140; azul 112.
Limitaciones: dos trazos de distinto color que se tocan forman una figura; una mancha gris real en el papel se detecta como tinta (es tinta para el algoritmo).
Eliminados respecto a v2: sliders "Umbral negro" y "Contraste fino" (los sustituye "Sensibilidad").

## ¿Red neuronal?
Valorada y aplazada: con papel claro y trazos de color el método clásico ya separa todas las figuras, es instantáneo, no necesita datos de entrenamiento y es depurable en la vista de máscara. Solo se reconsideraría (p. ej. segmentación U-Net pequeña vía ONNX) si falla con luz/papeles muy variados.

## Parámetros automáticos (ruta 1)
- **Sensibilidad auto** y **Color auto** (activados por defecto): ambos umbrales = mediana + 4.5·σ de un mapa de distancias al papel, con σ = 1.4826·MAD (robusto a la tinta). Sensibilidad usa la distancia Lab total (acotada 8–40); Color usa solo (a,b) (acotado 6–30).
- Medido en captura real (papel): sensibilidad ≈ 18 (el usuario puso 19 a mano); color ≈ 7.0. Diferencia (a,b) respecto al papel por figura: negro/gris 0.4–3.1, tinta azul oscura 8.6–10.4, celeste 13.7, colores 32–58 → umbral 7 separa bien (margen justo con la tinta azul oscura: si sale negra, bajar "Color mínimo" manual a 5–6).
- Los valores usados aparecen en la barra de estado.

## Objetos de distinto color que se tocan (corrección)
Bug: dos objetos de colores distintos pegados formaban una sola componente conectada y se llevaban el color mayoritario. Un k-means sobre (a,b,L) se descartó (partía objetos de un solo color por el ruido y no es determinista).
Solución (`SplitByColor`): para cada componente se calcula el histograma circular de matiz (cubos de 10°) de sus píxeles intensos (máscara alta) con (a,b) respecto al papel; el negro (dif. (a,b) < umbral de color) es otra clase. Los picos del histograma son los colores; dos picos se fusionan si el valle entre ellos ≥ 60 % del menor (un mismo color no se parte, p. ej. un azul en el límite cian/azul). Una clase solo cuenta si tiene cuerpo: ≥ 150 px tras abrir 5×5 (las letras finas de otro color no se parten). Si hay ≥ 2 clases válidas, sus semillas crecen (12 dilataciones 3×3) hasta cubrir la región y cada clase se convierte en sus propias figuras con su color.
Comprobado en Python con captura real: un lápiz azul-verdoso sobre un rectángulo amarillo y otro negro salió como 3 clases (amarillo, verde-azulado, negro); un objeto de un solo color, 1 clase.
Limitación: la frontera entre dos objetos pegados es la de "color más cercano", no exacta; los píxeles mezclados del borde se reparten a una de las dos.

## Ruido interior y figuras que mutan (corrección + inferencia de forma)
Síntomas: un rectángulo negro salía con muchos polígonos pequeños dentro (la máscara estaba limpia: era `SplitByColor` partiéndolo por ruido de color en píxeles oscuros) y las figuras sueltas cambiaban mucho de contorno.
- **Color suavizado**: las decisiones de color usan la diferencia (a,b) con un gaussiano σ=2 (el ruido de croma en oscuros se promedia; el color de un objeto uniforme se conserva).
- **Clases más estrictas**: una clase de color solo es un objeto propio con cuerpo ≥ 200 px tras abrir 9×9 (antes 150 px / 5×5): las franjas finas de color mezclado en el borde entre dos objetos ya no cuentan.
- **Inferir formas** (`BuildShape`, casilla activada por defecto): una mancha sólida (área ≥ 400, solidez = área/área de envolvente ≥ 0.88 y sin huecos grandes) se sustituye por la figura probable: **rectángulo girado** (`MinAreaRect`) si la mancha rellena ≥ 85 % del rectángulo; si no, **envolvente convexa** simplificada. Los trazos (letras, solidez baja) quedan con su contorno. Huecos < 8 % del área de la figura se ignoran siempre.
- Efecto: 4 vértices estables para rectángulos dibujados; menos vértices y menos jitter para el estabilizador (que empareja por distancia, no por orden de vértices).
- Idea futura (no hecha): usar los fotogramas anteriores (la figura del estabilizador) como prior para la forma.

## Red neuronal como ayuda (plan, no implementado)
La combinación razonable es **híbrida**: red pequeña de máscara (tinta sí/no, U-Net mínima exportada a ONNX) que sustituya o refuerce la máscara por diferencia con el papel, y conservar contornos, color por figura, inferencia de forma y estabilizador clásicos. Etiquetas: auto-generadas con este pipeline sobre vídeos grabados (carpeta `Videos/`), corrigiendo a mano los casos malos. Ganancia esperable: robustez a sombras/manchas y a objetos que tocan el papel (lápiz, manos). Prerrequisitos: grabar un conjunto de vídeos variado y medir el coste por fotograma.

## Vetas claras dentro de un trazo (puntos dentro del rectángulo negro)
Causa: con la sensibilidad automática alta (papel ruidoso) una veta clara dentro de un trazo de rotulador baja de la máscara y se cuenta como hueco (área ≥ 8 %), lo que además impedía inferir el rectángulo. Medido en una captura: huecos con diferencia media al papel 15–17 (el papel real da ≈ 5).
Corrección: un hueco solo es real si su diferencia media con el papel es < 0.5 × umbral de tinta (`IsPaperHole`); si aún tiene tinta floja dentro, se rellena. Efecto secundario aceptado: el hueco de una "o" muy pequeña también puede rellenarse si el papel está ruidoso.

## Redes ya entrenadas (investigado, sin integrar)
Ningún modelo preentrenado conoce "marcas de rotulador sobre papel"; solo servirían como segmentadores genéricos para la máscara, sin color ni formas:
- **U²-Netp** (ONNX 4.4 MB, entrada 320×320, pesos Apache-2.0; es el modelo de rembg para quitar fondos): rápido, pero es de "objeto saliente" y no está entrenado con dibujos: hay que probarlo.
- **MobileSAM / EdgeSAM** (segmentar todo sin entrenar): MobileSAM ≈ 5 imágenes/s en un iPhone 14 solo el codificador (~200 ms) y la segmentación automática necesita muchas consultas → solo viable a 1–2 Hz como ayuda, no por fotograma. Licencia sin confirmar en las fuentes consultadas.
- Los YOLO preentrenados (COCO) no sirven y los de Ultralytics son AGPL-3.0.
Plan de prueba barato: pasar U²-Netp y MobileSAM offline (Python) sobre fotogramas de `Videos/` y comparar su máscara con la del algoritmo antes de integrar nada.

## Regla: sin huecos que la máscara no tenga (2026-10-19)
El usuario fijó la regla: una figura no puede tener agujeros si la imagen blanca y negra no los tiene. Implementado como garantía final en `Detect`: tras generar las figuras se descartan los huecos cuyo interior sea blanco en la máscara (`IsBlackInMask`, < 20 % blanco).
Además, la división por color (`SplitByColor`) solo considera "otro color" a los píxeles claramente cromáticos (diferencia (a,b) ≥ 2× el umbral de color); lo dudoso se queda como negro. Sospecha de la causa de los puntitos dentro del rectángulo negro: el ruido de color partía el rectángulo en una clase "color" interior y salían polígonos pequeños dentro del grande. Si persisten, la prueba es desactivar la división (comentar la llamada a `SplitByColor`) y ver si desaparecen.

## Solo 5 colores (2026-10-19)
Decisión del usuario: menos sensibilidad al color. `InkColor` = Negro, Rojo, Amarillo, Verde, Azul. Negro = diferencia (a,b) con el papel menor que el umbral de color; el resto, al color más cercano por matiz HSV (H de OpenCV): rojo 0–14 y 150–179, amarillo 15–44, verde 45–89, azul 90–149 (naranja → amarillo; cian → verde/azul; morado/rosa → azul/rojo).
`SplitByColor` ya no usa histograma de picos: cada píxel intenso con color claro cae en una de las 4 bandas fijas y cada clase necesita cuerpo (≥ 200 px tras abrir 9×9). Esto elimina también el problema del azul en el límite cian/azul.
Las secciones anteriores que hablan de 9 familias, de histograma de matiz y de "Cian"/"Naranja" son históricas.

## v4 (2026-10-19): color por píxel sobre "imagen × máscara"
Sustituye a `SplitByColor` y a la clasificación por figura (la tinta de una figura entera ya no decide el color de todo).
1. La máscara blanco/negro (`BuildInkMask`: componentes aceptadas menos huecos reales) define qué es tinta.
2. `ClassifyInk` clasifica CADA píxel de tinta en los 5 colores. Es color si `croma(a,b)` vs papel ≥ 2.5×umbral de color, o si croma ≥ 1.7×umbral y la dirección de la diferencia con el papel es sobre todo color (`croma / |(dL/2.55, a, b)| ≥ 0.45`). La dirección hace que los bordes difuminados de un trazo azul sigan siendo azules (una mezcla trazo+papel conserva la dirección). Si no: negro. El matiz sale de HSV (σ=1) con las 5 bandas de siempre.
3. Filtro de moda (gaussiana σ=3 por clase) para limpiar ruido; después, partes finas (no caben en una apertura 25×25) con ≥25 % de su contorno tocando otro color se reasignan a ese color (sombra/borde oscuro de un lápiz o rotulador).
4. Cada región conectada de un color = una figura (`AddClassShapes`); su color es la media real de sus píxeles. Validado en Python con las capturas (lápiz azul = 1 figura, rectángulo negro = otra, aro negro, amarillo tapado por el lápiz).
Pendiente de comprobar en vivo: letras de tinta azul claro ("Pedro") pueden salir parcialmente azules; el lado oscuro de figuras de color a veces deja una franja negra pequeña.
