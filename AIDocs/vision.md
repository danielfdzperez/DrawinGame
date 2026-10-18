# Pipeline de visión

Entrada: fotograma BGR de la webcam. Salida: lista de `DetectedShape` (contorno exterior + huecos, vértices en píxeles de la imagen del área).

## 1. Área de interés (`AreaWarp`)
- El usuario marca **4 esquinas** sobre la imagen de la cámara (clic; arrastrar para ajustar). Coordenadas en píxeles de la imagen de cámara; el `PictureBox` está en modo Zoom, así que se convierten con escala y desplazamiento (`TryGetViewTransform`).
- Orden de esquinas: arriba-izq (mínimo x+y), abajo-der (máximo x+y), arriba-der (mínimo y−x), abajo-izq (máximo y−x).
- `GetPerspectiveTransform` + `WarpPerspective` a **900×636** (apaisado, proporción A4) o **636×900** (vertical) según los lados. Todo lo de fuera se descarta; todo lo de dentro se asume de interés.
- Limitación: si el área no es A4 sale estirada; si el área se gira ~45° puede cambiar entre apaisado y vertical.

## 2. Máscara de trazos negros (`DrawingDetector.Detect`)
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
