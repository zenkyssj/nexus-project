# NEXUS

> Your server in your pocket. Via WhatsApp or Telegram.

Nexus is an AI-powered server assistant that runs on your VPS and connects to your messaging app. Ask it about RAM, logs, processes, disk usage — all in natural language, without opening SSH.

---

## Requirements

Before installing Nexus, make sure your server has:

| Dependency | Version | Purpose |
|---|---|---|
| **Node.js** | 18+ | Runs the messaging bridge (Telegram / WhatsApp) |
| **Python** | 3.10+ | Runs the AI agent |
| **pip3** | any | Installs Python dependencies automatically |

### Install requirements on Ubuntu / Debian
```bash
sudo apt update
sudo apt install nodejs npm python3 python3-pip -y
```

### Install requirements on Amazon Linux / RHEL
```bash
sudo yum install nodejs npm python3 python3-pip -y
```

---

## Installation

### Linux / macOS
```bash
curl -L https://github.com/zenkyssj/nexus-project/releases/latest/download/nexus-linux-x64 -o nexus
chmod +x nexus
sudo mv nexus /usr/local/bin/nexus
nexus init
```

### macOS (Apple Silicon)
```bash
curl -L https://github.com/zenkyssj/nexus-project/releases/latest/download/nexus-osx-x64 -o nexus
chmod +x nexus
sudo mv nexus /usr/local/bin/nexus
nexus init
```

### Windows
Download [`nexus-win-x64.exe`](https://github.com/zenkyssj/nexus-project/releases/latest/download/nexus-win-x64.exe), rename it to `nexus.exe` and add it to your PATH.

No .NET installation required — Nexus ships as a self-contained binary.

---

## Setup

```bash
nexus init    # Configure channel, API keys, and permissions
nexus start   # Start the bot
```

During `nexus init` you will be asked for:
- **Telegram bot token** — get one from [@BotFather](https://t.me/BotFather)
- **Claude API key** — get one at [console.anthropic.com](https://console.anthropic.com)
- **Allowed folder** — the root path the AI is allowed to read (default: your home directory)
- **Command execution** — whether to allow the AI to run shell commands (disabled by default)

---

## Usage

Once running, send messages to your bot:

```
cuánta RAM tengo disponible?
mostrame el log de nginx
qué procesos están usando más CPU?
listá los archivos de /var/www
```

Send `reset` to start a new conversation session.

---

## Uninstall

```bash
sudo rm /usr/local/bin/nexus
rm -rf ~/.nexus
```
