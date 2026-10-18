# Estabilización de formas (`ShapeStabilizer`)

## Problema
El ruido de cámara hace temblar los vértices. No se pueden promediar vértices uno a uno porque `approxPolyDP` elige conjuntos de vértices distintos en cada fotograma. Además es un juego: un objeto que se mueve debe responder **al instante**.

## Solución: seguimiento de formas con histéresis
Cada forma estable guardada se compara con las detecciones nuevas usando una **distancia de Hausdorff aproximada** (máxima distancia de un vértice de una forma al contorno de la otra, en ambos sentidos, contando contorno y huecos; `Cv2.PointPolygonTest`).

Para cada pareja (forma guardada, detección):
1. **d ≤ tolerancia** → ruido: se mantiene la forma guardada tal cual (zona muerta).
2. **Movimiento global** → si ≥ 50 % de los vértices (en cualquiera de los dos sentidos) están a más de `tolerancia` de la otra forma: la forma se ha movido de verdad → se acepta la nueva **al instante**.
3. **Cambio local** (pocos vértices) → posible ruido de un vértice suelto: solo se acepta si el cambio se mantiene **3 fotogramas** seguidos.

## Asociación
- Pasada 1: por parecido de forma (Hausdorff ≤ 40 px; prefiltro por cajas envolventes).
- Pasada 2: objetos que saltan mucho → por centroide (≤ 150 px) y área (relación 0.6–1.6); se aceptan al instante.
- Asignación voraz por menor distancia.

## Vida de una forma
- Aparece tras **2** fotogramas seguidos.
- Desaparece tras **3** fotogramas sin verla.

## Parámetros
| Parámetro | Valor | Dónde |
|---|---|---|
| Tolerancia ("Estabilidad") | 4 px (0–15; 0 = off) | barra de la UI |
| MatchDistance | 40 px | constante |
| MaxCentroidJump | 150 px | constante |
| AreaRatio | 0.6–1.6 | constante |
| MotionFraction | 0.5 | constante |
| AppearFrames / LocalChangeFrames / DropFrames | 2 / 3 / 3 | constantes |

## Limitación conocida
Un cambio local aceptado reemplaza **toda** la forma, no solo el vértice cambiado. Un emparejamiento vértice a vértice sería más fino pero es delicado (los vértices cambian de número entre fotogramas).
