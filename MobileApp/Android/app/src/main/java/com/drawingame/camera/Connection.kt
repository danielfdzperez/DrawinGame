package com.drawingame.camera

import java.io.BufferedOutputStream
import java.io.ByteArrayOutputStream
import java.io.IOException
import java.io.OutputStream
import java.net.InetSocketAddress
import java.net.Socket

/**
 * Conexión TCP con Godot. Se conecta (probando cada IP), se identifica con el código y reconecta sola si se corta.
 *
 * Protocolo:
 *  - móvil -> PC al conectar: "DRWG", versión (1), longitud del código (1 byte), código (UTF-8)
 *  - PC -> móvil: 'K' (aceptado) o 'N' (rechazado)
 *  - móvil -> PC, por cada imagen: longitud (4 bytes, big-endian) = 1 + tamaño del JPEG,
 *    giro (1 byte: cuartos de vuelta en sentido horario para dejarla derecha), JPEG.
 */
class Connection(
    private val info: ServerInfo,
    private val onStatus: (String) -> Unit,
    private val onFatal: (String) -> Unit
) {
    @Volatile
    var connected: Boolean = false
        private set

    @Volatile
    private var running = false

    @Volatile
    private var rejected = false

    @Volatile
    private var socket: Socket? = null

    @Volatile
    private var out: OutputStream? = null

    private var thread: Thread? = null

    fun start() {
        running = true
        thread = Thread({ loop() }, "stream-connection").also { it.start() }
    }

    fun stop() {
        running = false
        disconnect()
        thread?.interrupt()
        thread = null
    }

    /** Envía una imagen. Bloquea si la red va lenta (el analizador de la cámara descarta fotogramas mientras tanto). */
    fun send(jpeg: ByteArrayOutputStream, rotationQuarters: Int) {
        val stream = out ?: return
        try {
            val len = jpeg.size() + 1
            stream.write(len ushr 24)
            stream.write(len ushr 16)
            stream.write(len ushr 8)
            stream.write(len)
            stream.write(rotationQuarters and 3)
            jpeg.writeTo(stream)
            stream.flush()
        } catch (e: IOException) {
            disconnect()
        }
    }

    private fun loop() {
        while (running) {
            onStatus("Conectando con ${info.hosts.joinToString(" / ")}…")

            var connectedHost: String? = null
            var s: Socket? = null
            for (host in info.hosts) {
                if (!running) return
                s = tryHost(host)
                if (s != null) {
                    connectedHost = host
                    break
                }
                if (rejected) break
            }

            if (rejected) {
                onFatal("El PC ha rechazado el código. Escanea el QR de nuevo.")
                return
            }
            if (s == null) {
                sleepQuietly(1500)
                continue
            }

            try {
                out = BufferedOutputStream(s.getOutputStream(), 64 * 1024)
                socket = s
                connected = true
                onStatus("Conectado a $connectedHost")

                // El PC no envía nada tras el 'K': este read solo termina cuando se cierra la conexión
                val input = s.getInputStream()
                while (running && input.read() >= 0) { /* ignorar */ }
            } catch (e: IOException) {
                // conexión perdida
            }
            disconnect()
            if (running) {
                onStatus("Conexión perdida. Reconectando…")
                sleepQuietly(1000)
            }
        }
    }

    private fun tryHost(host: String): Socket? {
        val s = Socket()
        try {
            s.tcpNoDelay = true
            s.connect(InetSocketAddress(host, info.port), 2000)
            s.soTimeout = 3000
            val token = info.token.toByteArray(Charsets.UTF_8)
            val o = s.getOutputStream()
            o.write(byteArrayOf('D'.code.toByte(), 'R'.code.toByte(), 'W'.code.toByte(), 'G'.code.toByte(), 1, token.size.toByte()))
            o.write(token)
            o.flush()

            val reply = s.getInputStream().read()
            if (reply == 'K'.code) {
                s.soTimeout = 0
                return s
            }
            if (reply == 'N'.code) rejected = true
        } catch (e: IOException) {
            // este host no responde
        }
        try {
            s.close()
        } catch (e: IOException) {
            // nada
        }
        return null
    }

    private fun disconnect() {
        connected = false
        out = null
        val s = socket
        socket = null
        try {
            s?.close()
        } catch (e: IOException) {
            // nada
        }
    }

    private fun sleepQuietly(ms: Long) {
        try {
            Thread.sleep(ms)
        } catch (e: InterruptedException) {
            // stop() interrumpe el hilo
        }
    }
}
