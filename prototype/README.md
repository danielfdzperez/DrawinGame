# Prototipo de cámara (C#)

Prototipo: elige una cámara, lee de ella, muestra la imagen y detecta dibujos de **negro y de colores** sobre fondo claro, convirtiéndolos en polígonos (vértices) que se dibujan encima con el color detectado. Muestra **tres pantallas a la vez**: izquierda = imagen filtrada, centro = máscara en blanco y negro (la tinta que detecta el algoritmo), derecha = imagen normal con las figuras coloreadas (lo que ve el usuario).

## Requisitos

- Windows 10/11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (o superior). Comprobar con `dotnet --version`.
- Una webcam conectada y con permiso de acceso (Configuración > Privacidad > Cámara).

## Ejecutar

Desde esta carpeta (`prototype`):

```
dotnet run
```

La primera vez descarga los paquetes NuGet (OpenCvSharp4), así que necesita internet.

## Uso

1. Al abrir, la app busca las cámaras disponibles (índices 0–9).
2. Elige una en el desplegable **Cámara**. Si conectas otra, pulsa **Buscar cámaras**.
3. Pulsa **Iniciar** para ver la imagen. **Detener** para parar y poder cambiar de cámara.
4. **Seleccionar el área** que interesa (folio, pizarra...). Todo lo de fuera se ignora y todo lo de dentro se analiza:
   1. Con la cámara iniciada, pulsa **Seleccionar área**. Se muestra la imagen completa.
   2. Haz clic en las **4 esquinas** del área, en cualquier orden. Puedes **arrastrar** cualquier esquina para ajustarla.
   3. Pulsa **Aplicar área**. La ventana pasa a mostrar solo esa zona, enderezada (corrección de perspectiva, proporción A4), y la detección trabaja sobre ella.
   - Para reajustar, pulsa otra vez **Seleccionar área**. **Borrar área** vuelve a la imagen completa.
   - Si mueves la cámara o el folio, hay que volver a marcar las esquinas.
5. Con **Detectar dibujo** activado, cada figura se pinta con su color; solo hay 5 (negro, rojo, amarillo, verde, azul) y se asigna el que más se parezca (naranja → amarillo, cian → verde o azul, morado/rosa → azul o rojo). Apunta la cámara a un folio blanco con dibujos en rotuladores.
6. Las tres pantallas: izquierda = imagen tras los filtros (la que recibe el detector); centro = máscara (blanco = tinta; de ahí salen las figuras, si algo falta aquí no se detectará); derecha = imagen normal + figuras. El área se marca sobre la pantalla derecha.
7. Panel lateral, **Filtros** (afectan a la imagen del algoritmo):
   - **Ruido temporal %**: promedia fotogramas (los píxeles que se mueven mucho se actualizan al instante).
   - **Ruido espacial**: filtro bilateral (quita ruido conservando bordes).
   - **Balance de blancos**: compensa el tinte de la luz (el papel pasa a ser blanco).
   - **Corregir sombras / luz %**: iguala la iluminación en el canal L de Lab.
   - **Contraste local (CLAHE)**: realza contraste por zonas.
   - **Realce de color %**: sube la saturación.
8. Ajustes de **Detección**:
   - **Sensibilidad automática** (activada por defecto): estima el umbral del ruido del papel en cada fotograma (se ve en la barra de estado). Desmárcala para usar el deslizador manual.
   - **Sensibilidad manual (dif. con el papel)**: cuánto tiene que diferenciarse un trazo del papel de alrededor para contar como tinta (negro, gris y colores pálidos por igual). Si faltan trazos flojos, bájala; si aparece ruido o manchas del papel, súbela.
   - **Color automático** (activado por defecto): decide si una figura es negra o de color midiendo cuánto difiere su color (a,b) del papel; el umbral se estima del ruido de color del papel.
   - **Color mínimo manual**: umbral a mano (típico 6–10). Si una tinta azul oscura sale negra, bájalo; si un negro sale con color, súbelo.
   - **Inferir formas** (activada por defecto): las manchas sólidas se convierten en su figura probable (rectángulo girado de 4 vértices o polígono convexo) y se ignoran los huecos pequeños; las letras/trazos se dejan tal cual. Desmárcala para ver el contorno crudo.
   - **Simplificación**: tolerancia en píxeles al convertir el contorno en polígono (Douglas-Peucker). Más alto = menos vértices y formas más angulosas; 0 = todos los vértices.
   - **Estabilidad**: elimina el temblor por ruido de cámara. Si los vértices de una forma cambian menos que estos píxeles, la forma se mantiene exactamente igual. Si la forma se mueve en conjunto (la mitad o más de sus vértices), se actualiza al instante; si solo cambia algún vértice suelto, se acepta únicamente si dura unos fotogramas. Más alto = más estable; "off" = sin estabilizar.


## Notas

- Las cámaras se listan como "Cámara 0", "Cámara 1"... (OpenCV no da el nombre). Prueba cada una si tienes varias.
- Si la imagen no aparece, cierra otras apps que usen la cámara (Teams, Zoom, navegador).
- Para generar un ejecutable: `dotnet publish -c Release -r win-x64 --self-contained`.

## Estructura

- `DrawinGamePrototype.csproj`: proyecto .NET 8 WinForms + OpenCvSharp4.
- `Program.cs`: punto de entrada.
- `MainForm.cs`: ventana, selección de cámara, selección del área con el ratón y bucle de captura en un hilo aparte.
- `AreaWarp.cs`: ordena las 4 esquinas elegidas y endereza el área (corrección de perspectiva).
- `ShapeStabilizer.cs`: estabilización entre fotogramas (seguimiento de formas con histéresis).
- `ImageFilter.cs`: cadena de filtros (temporal, bilateral, balance de blancos, iluminación, CLAHE, realce de color).
- `DrawingDetector.cs`: detección por clases de color (negro + bandas de matiz) sobre la imagen filtrada → polígonos simplificados con huecos y color medio; dibujado en el color de cada figura.

## Cambios 2026-10-19
- Detección de color por píxel (imagen × máscara) y fijado de la paleta de dibujo: cada familia se pinta con su color fijo.
- `DetectedShape` ahora tiene `Id` (asignado por `ShapeStabilizer`), `Centroid` y `Area`.
