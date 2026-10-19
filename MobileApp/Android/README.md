# DrawinGame Cámara (Android)

App Android mínima (Kotlin, nativa) que manda la cámara del móvil a Godot por WiFi.

## Compilar e instalar
1. Abre la carpeta `MobileApp/Android` con **Android Studio** (File > Open) y espera a que termine la sincronización de Gradle.
2. En el móvil: activa las **opciones de desarrollador** y la **depuración por USB**, y conéctalo al PC.
3. Pulsa **Run ▶** en Android Studio con el móvil seleccionado.

Sin Android Studio: `gradlew assembleDebug` y el APK queda en `app/build/outputs/apk/debug/app-debug.apk` (se instala con `adb install -r`).

## Uso
1. En Godot: pantalla inicial > **Cámara del móvil**. Godot muestra un QR.
2. En el móvil (misma red WiFi que el PC): abre la app > **Escanear QR**. También vale leer el QR con la cámara normal del móvil: si el sistema ofrece abrir el enlace `drawingame://…`, lo abre esta app.
3. La app pide permiso de cámara (y de notificaciones) y empieza a enviar. En Godot pasa solo a la pantalla de esquinas.
4. Con el streaming activo puedes **apagar la pantalla** (botón de encendido): el envío sigue en un servicio en primer plano (aparece una notificación con «Detener»). También hay «Pantalla negra», que baja el brillo al mínimo sin apagar la pantalla.

Los detalles técnicos (protocolo, decisiones) están en `AIDocs/movil.md`.
