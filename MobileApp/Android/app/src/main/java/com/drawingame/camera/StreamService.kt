package com.drawingame.camera

import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import android.content.pm.ServiceInfo
import android.graphics.Bitmap
import android.net.wifi.WifiManager
import android.os.Build
import android.os.PowerManager
import android.os.SystemClock
import android.util.Size
import androidx.camera.core.AspectRatio
import androidx.camera.core.CameraSelector
import androidx.camera.core.ImageAnalysis
import androidx.camera.core.ImageProxy
import androidx.camera.core.resolutionselector.AspectRatioStrategy
import androidx.camera.core.resolutionselector.ResolutionSelector
import androidx.camera.core.resolutionselector.ResolutionStrategy
import androidx.camera.lifecycle.ProcessCameraProvider
import androidx.core.app.NotificationCompat
import androidx.core.content.ContextCompat
import androidx.lifecycle.LifecycleService
import java.io.ByteArrayOutputStream
import java.util.concurrent.ExecutorService
import java.util.concurrent.Executors

/**
 * Servicio en primer plano: abre la cámara y manda las imágenes a Godot.
 * Al vivir en un servicio, el streaming sigue aunque se apague la pantalla o se cierre la app.
 */
class StreamService : LifecycleService() {

    companion object {
        private const val ACTION_STOP = "com.drawingame.camera.STOP"
        private const val EXTRA_URI = "uri"
        private const val EXTRA_PRESET = "preset"
        private const val CHANNEL_ID = "stream"
        private const val NOTIFICATION_ID = 1
        private const val TARGET_FPS = 15

        fun start(context: Context, info: ServerInfo, preset: Int) {
            val intent = Intent(context, StreamService::class.java)
                .putExtra(EXTRA_URI, info.toUriString())
                .putExtra(EXTRA_PRESET, preset)
            ContextCompat.startForegroundService(context, intent)
        }

        fun stop(context: Context) {
            context.stopService(Intent(context, StreamService::class.java))
        }
    }

    private var connection: Connection? = null
    private var executor: ExecutorService? = null
    private var cameraProvider: ProcessCameraProvider? = null
    private var wakeLock: PowerManager.WakeLock? = null
    private var wifiLock: WifiManager.WifiLock? = null

    private val jpeg = ByteArrayOutputStream(512 * 1024)
    private var quality = 80
    private val minIntervalMs = 1000L / TARGET_FPS
    private var lastSent = 0L
    private var framesInSecond = 0
    private var secondStart = 0L

    @Volatile
    private var baseStatus = ""

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        super.onStartCommand(intent, flags, startId)

        if (intent?.action == ACTION_STOP) {
            stopSelf()
            return START_NOT_STICKY
        }

        val info = ServerInfo.parse(intent?.getStringExtra(EXTRA_URI))
        if (info == null) {
            stopSelf()
            return START_NOT_STICKY
        }

        goForeground()
        stopStreaming()
        beginStreaming(info, intent?.getIntExtra(EXTRA_PRESET, 1) ?: 1)
        return START_NOT_STICKY
    }

    override fun onDestroy() {
        super.onDestroy()
        stopStreaming()
        if (StreamStatus.running) StreamStatus.update(false, "Detenido")
    }

    // ------------------------------------------------------------ Notificación / primer plano

    private fun goForeground() {
        val manager = getSystemService(NotificationManager::class.java)
        manager.createNotificationChannel(
            NotificationChannel(CHANNEL_ID, "Envío de la cámara", NotificationManager.IMPORTANCE_LOW)
        )

        val open = PendingIntent.getActivity(
            this, 0,
            Intent(this, MainActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_SINGLE_TOP),
            PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT
        )
        val stop = PendingIntent.getService(
            this, 1,
            Intent(this, StreamService::class.java).setAction(ACTION_STOP),
            PendingIntent.FLAG_IMMUTABLE
        )

        val notification = NotificationCompat.Builder(this, CHANNEL_ID)
            .setSmallIcon(android.R.drawable.ic_menu_camera)
            .setContentTitle("DrawinGame Cámara")
            .setContentText("Enviando la cámara al PC")
            .setContentIntent(open)
            .addAction(0, "Detener", stop)
            .setOngoing(true)
            .build()

        if (Build.VERSION.SDK_INT >= 29) {
            startForeground(NOTIFICATION_ID, notification, ServiceInfo.FOREGROUND_SERVICE_TYPE_CAMERA)
        } else {
            startForeground(NOTIFICATION_ID, notification)
        }
    }

    // ------------------------------------------------------------ Streaming

    private fun beginStreaming(info: ServerInfo, preset: Int) {
        acquireLocks()
        StreamStatus.update(true, "Conectando…")

        val size = when (preset) {
            0 -> Size(960, 540)
            2 -> Size(1920, 1080)
            else -> Size(1280, 720)
        }
        quality = if (preset == 2) 85 else 80

        connection = Connection(
            info,
            onStatus = { text ->
                baseStatus = text
                StreamStatus.update(true, text)
            },
            onFatal = { text -> fail(text) }
        ).also { it.start() }

        executor = Executors.newSingleThreadExecutor()
        bindCamera(size)
    }

    private fun bindCamera(size: Size) {
        val future = ProcessCameraProvider.getInstance(this)
        future.addListener({
            try {
                val provider = future.get()
                cameraProvider = provider

                val selector = ResolutionSelector.Builder()
                    .setAspectRatioStrategy(AspectRatioStrategy(AspectRatio.RATIO_16_9, AspectRatioStrategy.FALLBACK_RULE_AUTO))
                    .setResolutionStrategy(ResolutionStrategy(size, ResolutionStrategy.FALLBACK_RULE_CLOSEST_HIGHER_THEN_LOWER))
                    .build()

                val analysis = ImageAnalysis.Builder()
                    .setResolutionSelector(selector)
                    .setBackpressureStrategy(ImageAnalysis.STRATEGY_KEEP_ONLY_LATEST)
                    .setOutputImageFormat(ImageAnalysis.OUTPUT_IMAGE_FORMAT_RGBA_8888)
                    .build()
                analysis.setAnalyzer(executor ?: return@addListener) { image -> onFrame(image) }

                provider.unbindAll()
                provider.bindToLifecycle(this, CameraSelector.DEFAULT_BACK_CAMERA, analysis)
            } catch (e: Exception) {
                fail("No se pudo abrir la cámara: ${e.message}")
            }
        }, ContextCompat.getMainExecutor(this))
    }

    /** Se ejecuta en el hilo del analizador, una vez por imagen de la cámara. */
    private fun onFrame(image: ImageProxy) {
        try {
            val conn = connection ?: return
            val now = SystemClock.elapsedRealtime()
            if (!conn.connected || now - lastSent < minIntervalMs) return
            lastSent = now

            val bitmap = image.toBitmap()
            jpeg.reset()
            bitmap.compress(Bitmap.CompressFormat.JPEG, quality, jpeg)
            bitmap.recycle()

            conn.send(jpeg, (image.imageInfo.rotationDegrees / 90) and 3)

            framesInSecond++
            if (secondStart == 0L) secondStart = now
            if (now - secondStart >= 2000) {
                val fps = framesInSecond * 1000f / (now - secondStart)
                if (conn.connected) StreamStatus.update(true, "$baseStatus · ${"%.0f".format(fps)} imágenes/s")
                framesInSecond = 0
                secondStart = now
            }
        } finally {
            image.close()
        }
    }

    private fun fail(message: String) {
        StreamStatus.update(false, message)
        stopSelf()
    }

    private fun stopStreaming() {
        connection?.stop()
        connection = null
        try {
            cameraProvider?.unbindAll()
        } catch (e: Exception) {
            // ya cerrada
        }
        cameraProvider = null
        executor?.shutdown()
        executor = null
        releaseLocks()
    }

    // ------------------------------------------------------------ Mantener CPU y WiFi con la pantalla apagada

    @Suppress("DEPRECATION")
    private fun acquireLocks() {
        releaseLocks()
        val power = getSystemService(PowerManager::class.java)
        wakeLock = power.newWakeLock(PowerManager.PARTIAL_WAKE_LOCK, "drawingame:stream").apply {
            setReferenceCounted(false)
            acquire()
        }
        val wifi = applicationContext.getSystemService(WifiManager::class.java)
        wifiLock = wifi?.createWifiLock(WifiManager.WIFI_MODE_FULL_HIGH_PERF, "drawingame:wifi")?.apply {
            setReferenceCounted(false)
            acquire()
        }
    }

    private fun releaseLocks() {
        try {
            wakeLock?.takeIf { it.isHeld }?.release()
            wifiLock?.takeIf { it.isHeld }?.release()
        } catch (e: Exception) {
            // nada
        }
        wakeLock = null
        wifiLock = null
    }
}
