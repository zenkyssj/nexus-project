"""
nexus_agent/session.py

Gestión de sesiones: mantiene el historial de conversación
por usuario con timeout automático.
"""

import time
from typing import Optional


class SessionManager:
    """
    Almacena el historial de conversación de cada usuario en memoria.
    Las sesiones expiran automáticamente después del timeout configurado.
    """

    def __init__(self, timeout_minutes: int = 30):
        self.timeout_seconds = timeout_minutes * 60
        self._sessions: dict[str, dict] = {}  # {user_id: {messages, last_seen}}

    def get(self, user_id: str) -> list:
        """Devuelve el historial de mensajes del usuario, o [] si la sesión expiró."""
        self._cleanup_expired()
        session = self._sessions.get(user_id)

        if session is None:
            return []

        return list(session["messages"])

    def set(self, user_id: str, messages: list) -> None:
        """Guarda el historial actualizado del usuario."""
        self._sessions[user_id] = {
            "messages": messages,
            "last_seen": time.time()
        }

    def clear(self, user_id: str) -> None:
        """Limpia la sesión de un usuario (ej: cuando envía /reset)."""
        self._sessions.pop(user_id, None)

    def _cleanup_expired(self) -> None:
        """Elimina sesiones que superaron el timeout."""
        now = time.time()
        expired = [
            uid for uid, session in self._sessions.items()
            if now - session["last_seen"] > self.timeout_seconds
        ]
        for uid in expired:
            del self._sessions[uid]
            print(f"[SessionManager] Sesión expirada: {uid}")
