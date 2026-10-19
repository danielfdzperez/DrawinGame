package com.drawingame.camera

import android.Manifest
import android.content.Intent
import android.content.pm.PackageManager
import android.graphics.Color
import android.os.Build
import android.os.Bundle
import android.view.Gravity
import android.view.View
import android.view.ViewGroup
import android.view.WindowManager
import android.widget.Button
import android.widget.CheckBox
import android.widget.FrameLayout
import android.widget.LinearLayout
import android.widget.RadioButton
import android.widget.RadioGroup
import android.widget.TextView
import androidx.activity.result.contract.ActivityResultContracts
import androidx.appcompat.app.AppCompatActivity
import androidx.core.content.ContextCompat
import androidx.core.view.ViewCompat
import androidx.core.view.WindowInsetsCompat
import com.google.mlkit.vision.barcode.common.Barcode
import com.google.mlkit.vision.codescanner.GmsBarcodeScannerOptions
import com.google.mlkit.vision.codescanner.GmsBarcodeScanning

/**
 * Pantalla única. Escanear el QR de Godot (o abrir un enlace drawingame://) arranca el servicio de streaming.
 * La interfaz se construye por código para no necesitar archivos de diseño.
 */
class MainActivity : AppCompatActivity() {

    private lateinit var statusView: TextView
    private lateinit var scanButton: Button
    private lateinit var lastButton: Button
    private lateinit var stopButton: Button
    private lateinit var blackButton: Button
    private lateinit var presetGroup: RadioGroup
    private lateinit var presetLabel: TextView
    private lateinit var keepOnCheck: CheckBox
    private lateinit var blackOverlay: TextView

    private var pending: ServerInfo? = null
    private val prefs by lazy { getSharedPreferences("drawingame", MODE_PRIVATE) }

    private val permissionLauncher =
        registerForActivityResult(ActivityResultContracts.RequestMultiplePermissions()) {
            val cameraOk = ContextCompat.checkSelfPermission(this, Manifest.permission.CAMERA) == PackageManager.PERMISSION_GRANTED
            val info = pending
            pending = null
            if (cameraOk && info != null) {
                launchStreaming(info)
            } else {
                showMessage("Hace falta el permiso de cámara para enviar la imagen al PC.")
            }
        }

    // ------------------------------------------------------------ Ciclo de vida

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        buildUi()
        if (savedInstanceState == null) handleIntent(intent)
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        setIntent(intent)
        handleIntent(intent)
    }

    override fun onStart() {
        super.onStart()
        StreamStatus.listener = { updateUi() }
        updateUi()
    }

    override fun onStop() {
        super.onStop()
        StreamStatus.listener = null
    }

    /** Enlace drawingame://connect?... (QR leído con la cámara normal del móvil). */
    private fun handleIntent(intent: Intent?) {
        if (intent == null) return
        val data = intent.data ?: return
        intent.data = null
        val info = ServerInfo.parse(data.toString())
        if (info == null) showMessage("El enlace no es válido.") else startStreaming(info)
    }

    // ------------------------------------------------------------ Acciones

    private fun scanQr() {
        val options = GmsBarcodeScannerOptions.Builder()
            .setBarcodeFormats(Barcode.FORMAT_QR_CODE)
            .build()
        GmsBarcodeScanning.getClient(this, options).startScan()
            .addOnSuccessListener { barcode ->
                val info = ServerInfo.parse(barcode.rawValue)
                if (info == null) showMessage("Ese QR no es de DrawinGame.") else startStreaming(info)
            }
            .addOnFailureListener { e ->
                showMessage("No se pudo leer el QR: ${e.message}")
            }
    }

    private fun reconnectLast() {
        val info = ServerInfo.parse(prefs.getString("last", null))
        if (info == null) showMessage("Aún no hay ningún PC guardado.") else startStreaming(info)
    }

    private fun startStreaming(info: ServerInfo) {
        val missing = mutableListOf<String>()
        if (ContextCompat.checkSelfPermission(this, Manifest.permission.CAMERA) != PackageManager.PERMISSION_GRANTED) {
            missing.add(Manifest.permission.CAMERA)
        }
        if (Build.VERSION.SDK_INT >= 33 &&
            ContextCompat.checkSelfPermission(this, Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED
        ) {
            missing.add(Manifest.permission.POST_NOTIFICATIONS)
        }

        if (missing.isEmpty()) {
            launchStreaming(info)
        } else {
            pending = info
            permissionLauncher.launch(missing.toTypedArray())
        }
    }

    private fun launchStreaming(info: ServerInfo) {
        prefs.edit().putString("last", info.toUriString()).apply()
        StreamService.start(this, info, selectedPreset())
        updateUi()
    }

    private fun selectedPreset(): Int = prefs.getInt("preset", 1)

    private fun showMessage(text: String) {
        statusView.text = text
    }

    // ------------------------------------------------------------ Pantalla negra (ahorra batería sin apagar la pantalla)

    private fun setBlack(on: Boolean) {
        blackOverlay.visibility = if (on) View.VISIBLE else View.GONE
        val params = window.attributes
        params.screenBrightness = if (on) 0.01f else WindowManager.LayoutParams.BRIGHTNESS_OVERRIDE_NONE
        window.attributes = params
        if (on) window.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON) else applyKeepOn()
    }

    private fun applyKeepOn() {
        if (keepOnCheck.isChecked) {
            window.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
        } else {
            window.clearFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
        }
    }

    // ------------------------------------------------------------ Interfaz

    private fun updateUi() {
        val running = StreamStatus.running
        statusView.text = StreamStatus.text.ifEmpty { "Sin conexión. Escanea el QR que muestra Godot." }
        scanButton.visibility = if (running) View.GONE else View.VISIBLE
        lastButton.visibility = if (!running && prefs.getString("last", null) != null) View.VISIBLE else View.GONE
        presetLabel.visibility = if (running) View.GONE else View.VISIBLE
        presetGroup.visibility = if (running) View.GONE else View.VISIBLE
        stopButton.visibility = if (running) View.VISIBLE else View.GONE
        blackButton.visibility = if (running) View.VISIBLE else View.GONE
    }

    private fun buildUi() {
        val column = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            layoutParams = FrameLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT)
        }

        column.addView(TextView(this).apply {
            text = "DrawinGame · Cámara"
            textSize = 26f
        })
        column.addView(TextView(this).apply {
            text = "En Godot elige «Cámara del móvil» y escanea el QR que aparece. " +
                "La imagen de este móvil se enviará al PC (misma red WiFi)."
            textSize = 15f
            setPadding(0, dp(8), 0, dp(16))
        })

        statusView = TextView(this).apply {
            textSize = 18f
            setPadding(0, dp(8), 0, dp(16))
        }
        column.addView(statusView)

        scanButton = button("Escanear QR") { scanQr() }
        lastButton = button("Reconectar con el último PC") { reconnectLast() }
        stopButton = button("Detener") { StreamService.stop(this) }
        blackButton = button("Pantalla negra (ahorra batería)") { setBlack(true) }
        column.addView(scanButton)
        column.addView(lastButton)
        column.addView(blackButton)
        column.addView(stopButton)

        presetLabel = TextView(this).apply {
            text = "Calidad de imagen"
            textSize = 15f
            setPadding(0, dp(20), 0, dp(4))
        }
        column.addView(presetLabel)

        presetGroup = RadioGroup(this)
        val names = listOf("Baja (540p, menos datos)", "Media (720p)", "Alta (1080p)")
        val ids = names.map { View.generateViewId() }
        names.forEachIndexed { index, name ->
            presetGroup.addView(RadioButton(this).apply {
                id = ids[index]
                text = name
            })
        }
        presetGroup.check(ids[selectedPreset().coerceIn(0, 2)])
        presetGroup.setOnCheckedChangeListener { _, checkedId ->
            prefs.edit().putInt("preset", ids.indexOf(checkedId).coerceAtLeast(0)).apply()
        }
        column.addView(presetGroup)

        keepOnCheck = CheckBox(this).apply {
            text = "Mantener la pantalla encendida"
            isChecked = prefs.getBoolean("keepOn", false)
            setOnCheckedChangeListener { _, checked ->
                prefs.edit().putBoolean("keepOn", checked).apply()
                applyKeepOn()
            }
        }
        column.addView(keepOnCheck, LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT
        ).apply { topMargin = dp(12) })

        blackOverlay = TextView(this).apply {
            text = "Toca la pantalla para volver"
            setTextColor(Color.rgb(50, 50, 50))
            setBackgroundColor(Color.BLACK)
            gravity = Gravity.CENTER
            visibility = View.GONE
            isClickable = true
            setOnClickListener { setBlack(false) }
        }

        val root = FrameLayout(this)
        root.addView(column)
        root.addView(blackOverlay, FrameLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT))
        setContentView(root)

        // Con la pantalla de borde a borde, dejar espacio para las barras del sistema
        ViewCompat.setOnApplyWindowInsetsListener(column) { view, insets ->
            val bars = insets.getInsets(WindowInsetsCompat.Type.systemBars())
            view.setPadding(dp(24) + bars.left, dp(24) + bars.top, dp(24) + bars.right, dp(24) + bars.bottom)
            insets
        }

        applyKeepOn()
    }

    private fun button(text: String, onClick: () -> Unit): Button = Button(this).apply {
        this.text = text
        setOnClickListener { onClick() }
        layoutParams = LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT)
            .apply { topMargin = dp(8) }
    }

    private fun dp(value: Int): Int = (value * resources.displayMetrics.density).toInt()
}
