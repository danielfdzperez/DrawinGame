# Prototipo de cámara (C#)

Prototipo: elige una cámara, lee de ella, muestra la imagen y detecta dibujos en negro sobre fondo blanco, convirtiéndolos en polígonos (vértices) que se dibujan en verde encima.

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
5. Con **Detectar dibujo** activado, los trazos negros se pintan en verde. Apunta la cámara a un folio blanco con un dibujo en rotulador negro.
6. Ajustes:
   - **Umbral negro**: oscuridad absoluta. Para trazos gruesos o rellenos. Mantenlo bajo para no detectar grises.
   - **Simplificación**: tolerancia en píxeles al convertir el contorno en polígono (Douglas-Peucker). Más alto = menos vértices y formas más angulosas; 0 = todos los vértices.
   - **Estabilidad**: elimina el temblor por ruido de cámara. Si los vértices de una forma cambian menos que estos píxeles, la forma se mantiene exactamente igual. Si la forma se mueve en conjunto (la mitad o más de sus vértices), se actualiza al instante; si solo cambia algún vértice suelto, se acepta únicamente si dura unos fotogramas. Más alto = más estable; "off" = sin estabilizar.
   - **Contraste fino**: para trazos finos. Detecta lo que es más oscuro que su entorno inmediato y estrecho (hasta ~15 px), aunque en la cámara se vea gris. Si no detecta un trazo fino, bájalo; si detecta ruido, súbelo. En 0 se desactiva. A la derecha se indica cuántas formas y vértices detecta.

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
- `DrawingDetector.cs`: detección (gris → umbral + contraste → contornos → descarte de ruido → polígonos simplificados con huecos) y dibujado (relleno verde, contorno y vértices en amarillo).
