import json
import os
import sys
from http.server import HTTPServer, BaseHTTPRequestHandler
from nexus_agent.config import load_config
from nexus_agent.llm import NexusLLM

AGENT_PORT = 5000
BRIDGE_PORT = 5001

class NexusHandler(BaseHTTPRequestHandler):
    llm: NexusLLM = None 

    def do_POST(self):
        length = int(self.headers.get('Content-Length',0))
        body = json.loads(self.rfile.read(length)) if length else{}

        if self.path == '/message':
            self._handle_message(body)
        elif self.path == '/send':
            self._handle_send(body)
        else:
            self._send_json(404, {"error": "not found"})

    def _handle_message(self, data):
        from_id = data.get('from', '')
        text = data.get('body', '').strip()

        print(f"\n[Message from {from_id}] {text}")

        if not text:
            self._send_json(200, {"ok": True})
            return
        
        # Comando especial para resetear la sesión
        if text.lower() in ("/reset", "/new", "/clear", "reset"):
            self.llm.sessions.clear(from_id)
            response = "🔄 Sesión reiniciada. ¿En qué puedo ayudarte?"
        else:
            response = self.llm.process(text, from_id=from_id)

        print(f"[Respuesta] {response[:100]}{'...' if len(response) > 100 else ''}")
        self._send_to_bridge(from_id, response)
        self._send_json(200, {"ok": True, "response": response})

    def _handle_send(self, data):
        print(f"[Agent sends] {data.get('message','')}")

        self._send_json(200, {"ok": True})

    def _send_to_bridge(self, to, message):
        import http.client
        try:
            conn = http.client.HTTPConnection('127.0.0.1', BRIDGE_PORT, timeout=5)
            body = json.dumps({"to": to, "message":message})

            conn.request('POST', '/send', body, {
                'Content-Type': 'application/json'
            })

            conn.getresponse().read()
            conn.close()
        
        except Exception as e:
            print(f"[An error ocurred sending to bridge] : {e}")

    def _send_json(self, status, data):
        self.send_response(status)
        self.send_header('Content-Type', 'application/json')
        self.end_headers()
        self.wfile.write(json.dumps(data).encode())

    def log_message(self, format, *args):
        pass

def main():
    config = load_config()
    NexusHandler.llm = NexusLLM(config)

    server = HTTPServer(('127.0.0.1', AGENT_PORT), NexusHandler)
    print(f"[Nexus Agent] Listening on http://localhost:{AGENT_PORT}")
    print(f"[Nexus Agent] Canal: {config.channel}")
    print(f"[Nexus Agent] Tools: read_file={config.tools.read_file}, "
          f"list_dir={config.tools.list_directory}, "
          f"execute={config.tools.execute_command}")

    try:
        server.serve_forever()
    except KeyboardInterrupt:
        print("\n[Nexus Agent] Shutting down...")
        server.server_close();

if __name__ == '__main__':
    main()
