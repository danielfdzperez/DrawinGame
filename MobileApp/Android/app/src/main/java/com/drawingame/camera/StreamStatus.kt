package com.drawingame.camera

import android.os.Handler
import android.os.Looper

/** Estado del streaming, compartido entre el servicio y la pantalla. */
object StreamStatus {
    @Volatile
    var running: Boolean = false
        private set

    @Volatile
    var text: String = ""
        private set

    /** Se llama en el hilo principal cada vez que cambia el estado. */
    @Volatile
    var listener: (() -> Unit)? = null

    private val main = Handler(Looper.getMainLooper())

    fun update(running: Boolean, text: String) {
        this.running = running
        this.text = text
        main.post { listener?.invoke() }
    }
}
