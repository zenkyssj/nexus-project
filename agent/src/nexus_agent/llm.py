"""
nexus_agent/llm.py

Motor de IA de Nexus. Conecta con Claude via tool use para
responder mensajes del servidor con acceso real al sistema.
"""

import os
import subprocess
import glob
import json
import anthropic
from nexus_agent.session import SessionManager

# ─── Tools disponibles ────────────────────────────────────────────────────────

TOOLS = [
    {
        "name": "read_file",
        "description": (
            "Lee el contenido de un archivo del servidor. "
            "Útil para ver logs, configs, scripts, etc."
        ),
        "input_schema": {
            "type": "object",
            "properties": {
                "path": {
                    "type": "string",
                    "description": "Ruta absoluta o relativa al archivo"
                },
                "lines": {
                    "type": "integer",
                    "description": "Número de líneas a leer desde el final (tail). 0 = archivo completo.",
                    "default": 0
                }
            },
            "required": ["path"]
        }
    },
    {
        "name": "list_directory",
        "description": "Lista archivos y carpetas de un directorio del servidor.",
        "input_schema": {
            "type": "object",
            "properties": {
                "path": {
                    "type": "string",
                    "description": "Ruta del directorio. Default: directorio home."
                }
            },
            "required": []
        }
    },
    {
        "name": "get_processes",
        "description": "Lista los procesos activos del servidor con PID, CPU y memoria.",
        "input_schema": {
            "type": "object",
            "properties": {
                "filter": {
                    "type": "string",
                    "description": "Filtrar por nombre de proceso (opcional)"
                }
            },
            "required": []
        }
    },
    {
        "name": "get_disk_usage",
        "description": "Muestra el uso de disco del servidor.",
        "input_schema": {
            "type": "object",
            "properties": {
                "path": {
                    "type": "string",
                    "description": "Ruta a analizar. Default: raíz del sistema.",
                    "default": "/"
                }
            },
            "required": []
        }
    },
    {
        "name": "get_system_info",
        "description": "Información general del servidor: uptime, memoria RAM, carga del sistema.",
        "input_schema": {
            "type": "object",
            "properties": {},
            "required": []
        }
    },
    {
        "name": "execute_command",
        "description": (
            "Ejecuta un comando shell en el servidor. "
            "SOLO disponible si el usuario lo habilitó explícitamente en la configuración. "
            "Usar con precaución."
        ),
        "input_schema": {
            "type": "object",
            "properties": {
                "command": {
                    "type": "string",
                    "description": "Comando a ejecutar"
                }
            },
            "required": ["command"]
        }
    },
]

SYSTEM_PROMPT = """Sos Nexus, un asistente de servidor inteligente que responde via mensajería (WhatsApp/Telegram).

Tu objetivo es ayudar al administrador a monitorear y gestionar su servidor de forma remota.

Reglas:
- Respondé siempre en el mismo idioma que el usuario
- Sé conciso: los mensajes de chat deben ser cortos y directos
- Usá emojis con moderación para hacer los mensajes más legibles en móvil
- Si una operación puede ser destructiva, confirmá antes de ejecutarla
- Si no tenés una tool habilitada para algo, explicalo claramente
- Formateá los números de forma legible (ej: 1.2 GB en lugar de 1234567890 bytes)
- Para logs largos, mostrá solo las últimas líneas relevantes

Límites de seguridad:
- Solo podés acceder a rutas dentro de los allowed_paths configurados
- execute_command solo está disponible si el usuario lo habilitó
- Nunca reveles la API key ni información sensible de la config
"""

class NexusLLM:
    def __init__(self, config):
        self.config = config
        self.client = anthropic.Anthropic(api_key=config.claude_api_key)
        self.sessions = SessionManager(timeout_minutes=config.session_timeout_minutes)

        # Filtrar tools según la configuración del usuario
        self._enabled_tools = self._build_enabled_tools()

    def _build_enabled_tools(self) -> list:
        """Devuelve solo las tools habilitadas en la config."""
        t = self.config.tools
        enabled = []

        if t.read_file:
            enabled.append(next(x for x in TOOLS if x["name"] == "read_file"))
        if t.list_directory:
            enabled.append(next(x for x in TOOLS if x["name"] == "list_directory"))
        if t.get_processes:
            enabled.append(next(x for x in TOOLS if x["name"] == "get_processes"))
        # Disk y system info siempre disponibles (read-only, no riesgo)
        enabled.append(next(x for x in TOOLS if x["name"] == "get_disk_usage"))
        enabled.append(next(x for x in TOOLS if x["name"] == "get_system_info"))
        if t.execute_command:
            enabled.append(next(x for x in TOOLS if x["name"] == "execute_command"))

        return enabled

    def process(self, user_message: str, from_id: str = "default") -> str:
        """
        Procesa un mensaje del usuario y devuelve la respuesta de Claude.
        Mantiene el historial de conversación por sesión.
        """
        # Obtener o crear sesión para este usuario
        history = self.sessions.get(from_id)
        history.append({"role": "user", "content": user_message})

        try:
            response_text = self._run_agent_loop(history)
            history.append({"role": "assistant", "content": response_text})
            self.sessions.set(from_id, history)
            return response_text

        except anthropic.AuthenticationError:
            return "❌ Error de autenticación con Claude. Verificá tu API key con `nexus init --force`."
        except anthropic.RateLimitError:
            return "⏳ Límite de rate de Claude alcanzado. Intentá en unos segundos."
        except Exception as e:
            print(f"[NexusLLM] Error inesperado: {e}")
            return f"❌ Error interno del agente: {type(e).__name__}"

    def _run_agent_loop(self, messages: list) -> str:
        """
        Loop de agentic tool use: llama a Claude, ejecuta tools, repite
        hasta que Claude devuelva una respuesta final de texto.
        """
        loop_messages = list(messages)

        for _ in range(10):  # máximo 10 iteraciones para evitar loops infinitos
            response = self.client.messages.create(
                model="claude-haiku-4-5-20251001",  # rápido y económico para uso en servidor
                max_tokens=1024,
                system=SYSTEM_PROMPT,
                tools=self._enabled_tools,
                messages=loop_messages
            )

            # Si Claude terminó (no quiere usar más tools)
            if response.stop_reason == "end_turn":
                return self._extract_text(response)

            # Si Claude quiere usar tools
            if response.stop_reason == "tool_use":
                # Agregar la respuesta de Claude al historial del loop
                loop_messages.append({
                    "role": "assistant",
                    "content": response.content
                })

                # Ejecutar cada tool_use block
                tool_results = []
                for block in response.content:
                    if block.type == "tool_use":
                        result = self._execute_tool(block.name, block.input)
                        tool_results.append({
                            "type": "tool_result",
                            "tool_use_id": block.id,
                            "content": result
                        })

                # Agregar resultados de tools al historial del loop
                loop_messages.append({
                    "role": "user",
                    "content": tool_results
                })
                continue

            # Caso inesperado
            break

        return self._extract_text(response)

    def _extract_text(self, response) -> str:
        """Extrae el texto de la respuesta de Claude."""
        for block in response.content:
            if hasattr(block, "text"):
                return block.text
        return "Sin respuesta."

    # ─── Implementación de tools ──────────────────────────────────────────────

    def _execute_tool(self, tool_name: str, tool_input: dict) -> str:
        """Despacha la ejecución de una tool y devuelve el resultado como string."""
        try:
            if tool_name == "read_file":
                return self._tool_read_file(**tool_input)
            elif tool_name == "list_directory":
                return self._tool_list_directory(**tool_input)
            elif tool_name == "get_processes":
                return self._tool_get_processes(**tool_input)
            elif tool_name == "get_disk_usage":
                return self._tool_get_disk_usage(**tool_input)
            elif tool_name == "get_system_info":
                return self._tool_get_system_info()
            elif tool_name == "execute_command":
                return self._tool_execute_command(**tool_input)
            else:
                return f"Tool desconocida: {tool_name}"
        except PermissionError as e:
            return f"❌ Acceso denegado: {e}"
        except FileNotFoundError as e:
            return f"❌ No encontrado: {e}"
        except Exception as e:
            return f"❌ Error ejecutando {tool_name}: {e}"

    def _check_allowed_path(self, path: str) -> str:
        """Verifica que el path esté dentro de los allowed_paths."""
        abs_path = os.path.realpath(os.path.expanduser(path))
        for allowed in self.config.allowed_paths:
            allowed_real = os.path.realpath(os.path.expanduser(allowed))
            if abs_path.startswith(allowed_real):
                return abs_path
        raise PermissionError(
            f"'{path}' está fuera de los paths permitidos. "
            f"Paths permitidos: {', '.join(self.config.allowed_paths)}"
        )

    def _tool_read_file(self, path: str, lines: int = 0) -> str:
        safe_path = self._check_allowed_path(path)

        if not os.path.isfile(safe_path):
            raise FileNotFoundError(f"No es un archivo: {safe_path}")

        size = os.path.getsize(safe_path)
        if size > 500_000:  # 500 KB máximo
            return f"⚠ Archivo demasiado grande ({size // 1024} KB). Usá 'lines' para leer solo el final."

        with open(safe_path, "r", errors="replace") as f:
            content = f.read()

        if lines and lines > 0:
            all_lines = content.splitlines()
            content = "\n".join(all_lines[-lines:])
            return f"[últimas {lines} líneas de {path}]\n{content}"

        return f"[{path}]\n{content}"

    def _tool_list_directory(self, path: str = "~") -> str:
        if path == "~" or not path:
            path = os.path.expanduser("~")
        safe_path = self._check_allowed_path(path)

        if not os.path.isdir(safe_path):
            raise FileNotFoundError(f"No es un directorio: {safe_path}")

        entries = os.listdir(safe_path)
        entries.sort()

        lines = []
        for entry in entries:
            full = os.path.join(safe_path, entry)
            if os.path.isdir(full):
                lines.append(f"📁 {entry}/")
            else:
                size = os.path.getsize(full)
                size_str = f"{size // 1024} KB" if size >= 1024 else f"{size} B"
                lines.append(f"📄 {entry} ({size_str})")

        return f"[{safe_path}]\n" + "\n".join(lines) if lines else f"[{safe_path}] (vacío)"

    def _tool_get_processes(self, filter: str = "") -> str:
        try:
            result = subprocess.run(
                ["ps", "aux", "--sort=-%cpu"],
                capture_output=True, text=True, timeout=10
            )
            lines = result.stdout.strip().splitlines()
            # header + top 15
            output_lines = [lines[0]] + lines[1:16]

            if filter:
                filtered = [l for l in lines[1:] if filter.lower() in l.lower()]
                if filtered:
                    output_lines = [lines[0]] + filtered[:15]
                else:
                    return f"No hay procesos con '{filter}' activos."

            return "\n".join(output_lines)
        except FileNotFoundError:
            # Windows fallback
            result = subprocess.run(
                ["tasklist"],
                capture_output=True, text=True, timeout=10
            )
            return result.stdout[:2000]

    def _tool_get_disk_usage(self, path: str = "/") -> str:
        try:
            result = subprocess.run(
                ["df", "-h", path],
                capture_output=True, text=True, timeout=10
            )
            return result.stdout.strip()
        except FileNotFoundError:
            # Windows
            result = subprocess.run(
                ["wmic", "logicaldisk", "get", "size,freespace,caption"],
                capture_output=True, text=True, timeout=10
            )
            return result.stdout.strip()

    def _tool_get_system_info(self) -> str:
        info = []

        # Uptime
        try:
            with open("/proc/uptime") as f:
                uptime_seconds = float(f.read().split()[0])
                days = int(uptime_seconds // 86400)
                hours = int((uptime_seconds % 86400) // 3600)
                info.append(f"⏱ Uptime: {days}d {hours}h")
        except Exception:
            pass

        # RAM
        try:
            with open("/proc/meminfo") as f:
                lines = f.readlines()
            mem = {l.split(":")[0]: int(l.split(":")[1].strip().split()[0])
                   for l in lines if ":" in l}
            total_mb = mem.get("MemTotal", 0) // 1024
            avail_mb = mem.get("MemAvailable", 0) // 1024
            used_mb = total_mb - avail_mb
            pct = int(used_mb / total_mb * 100) if total_mb else 0
            info.append(f"🧠 RAM: {used_mb} MB / {total_mb} MB ({pct}% usado)")
        except Exception:
            pass

        # Load average
        try:
            with open("/proc/loadavg") as f:
                load = f.read().split()[:3]
            info.append(f"📊 Load avg: {' '.join(load)}")
        except Exception:
            pass

        # Hostname
        try:
            result = subprocess.run(["hostname"], capture_output=True, text=True, timeout=5)
            info.append(f"🖥 Host: {result.stdout.strip()}")
        except Exception:
            pass

        return "\n".join(info) if info else "No se pudo obtener información del sistema."

    def _tool_execute_command(self, command: str) -> str:
        if not self.config.tools.execute_command:
            return "❌ execute_command está deshabilitado. Habilitalo en la configuración."

        # Bloquear comandos destructivos obvios
        blocked = ["rm -rf /", "mkfs", "dd if=", ":(){:|:&};:"]
        for b in blocked:
            if b in command:
                return f"❌ Comando bloqueado por seguridad: contiene '{b}'"

        try:
            result = subprocess.run(
                command,
                shell=True,
                capture_output=True,
                text=True,
                timeout=30,
                cwd=os.path.expanduser("~")
            )
            output = result.stdout or result.stderr or "(sin output)"
            # Limitar output para no saturar el chat
            if len(output) > 2000:
                output = output[:2000] + "\n... (output truncado)"
            return f"$ {command}\n{output}"
        except subprocess.TimeoutExpired:
            return f"⏱ Timeout: el comando tardó más de 30 segundos."
