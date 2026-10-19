package com.drawingame.camera

import android.net.Uri

/**
 * Datos para conectar con Godot. Viajan en el QR como:
 *   drawingame://connect?h=192.168.1.5,10.0.0.3&p=48750&t=abcd1234
 * h = direcciones IP del PC (se prueban en orden), p = puerto, t = código de acceso.
 */
data class ServerInfo(val hosts: List<String>, val port: Int, val token: String) {

    fun toUriString(): String = "drawingame://connect?h=${hosts.joinToString(",")}&p=$port&t=$token"

    companion object {
        fun parse(text: String?): ServerInfo? {
            if (text.isNullOrBlank()) return null
            val uri = try {
                Uri.parse(text.trim())
            } catch (e: Exception) {
                return null
            }
            if (uri.scheme != "drawingame" || uri.host != "connect") return null

            val hosts = uri.getQueryParameter("h")?.split(",")?.map { it.trim() }?.filter { it.isNotEmpty() }
            val port = uri.getQueryParameter("p")?.toIntOrNull()
            val token = uri.getQueryParameter("t")
            if (hosts.isNullOrEmpty() || port == null || port !in 1..65535) return null
            if (token.isNullOrEmpty() || token.length > 100) return null
            return ServerInfo(hosts, port, token)
        }
    }
}
