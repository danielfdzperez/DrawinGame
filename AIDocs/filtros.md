# Filtros de imagen (`ImageFilter`)

Objetivo: dar al detector una imagen con menos ruido y más robusta a la iluminación, con los colores resaltados. La imagen filtrada solo la ve el algoritmo (pantalla izquierda); el usuario ve la imagen normal.

Orden de la cadena (cada paso se puede desactivar con su ajuste a 0/off):
1. **Temporal**: media móvil exponencial entre fotogramas; los píxeles con cambio grande (movimiento) se sustituyen al instante para no dejar estela.
2. **Espacial**: filtro bilateral (suaviza ruido conservando bordes).
3. **Balance de blancos**: white-patch, ganancia por canal estimada del máximo de una copia desenfocada, suavizada en el tiempo.
4. **Iluminación**: en canal L de Lab se estima la iluminación (dilatar + desenfocar a 1/4 de resolución) y se divide (idea de Retinex / corrección de sombras); mezcla según %. Después CLAHE.
5. **Realce de color**: multiplica la saturación en HSV.

Valores por defecto: ver `prototipo.md`. Pendiente de validar con luz real; ajustar con los deslizadores.

## Fuentes
- OpenCV GrayworldWB: https://docs.opencv.org/4.1.0/d7/d71/classcv_1_1xphoto_1_1GrayworldWB.html
- Edge AI Vision, Adaptive Shadow Correction Using OpenCV: https://www.edge-ai-vision.com/?p=56674
- OpenCV Q&A, constancia de color (ecualización, gamma, DoG, CLAHE, retinex): https://answers.opencv.org/question/9054
- Survey de suavizado preservando bordes: https://arxiv.org/abs/1503.07297
