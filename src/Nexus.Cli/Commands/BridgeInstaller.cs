namespace Nexus.Cli.Commands;

/// <summary>
/// Writes the Node.js WhatsApp bridge source files to the user's config directory.
/// This embeds the bridge so users don't need to clone the repo.
/// </summary>
public static class BridgeInstaller
{
    public static async Task WriteFilesAsync(string bridgePath, string channel)
    {
        await File.WriteAllTextAsync(
            Path.Combine(bridgePath, "index.js"),
            channel == "telegram" ? BridgeTelegramJs : BridgeWhatsAppJs
        );

        await File.WriteAllTextAsync(
            Path.Combine(bridgePath, "package.json"),
            channel == "telegram" ? TelegramPackageJson : WhatsAppPackageJson
        );
    }

    private const string TelegramPackageJson = """
    {
      "name": "nexus-bridge",
      "version": "1.0.0",
      "dependencies": {
        "node-telegram-bot-api": "^0.66.0"
      }
    }
    """;

    private const string BridgeTelegramJs = """
    const TelegramBot = require('node-telegram-bot-api');
    const http = require('http');
    const fs = require('fs');
    const cfg = JSON.parse(fs.readFileSync(process.env.HOME + '/.nexus/config.json', 'utf-8'));
    const NEXUS_API = 'http://localhost:5000';
    const bot = new TelegramBot(cfg.telegramToken, { polling: true });
    bot.on('message', async (msg) => {
        if (msg.text?.startsWith('/')) return;
        const chatId = msg.chat.id;
        try {
            const body = JSON.stringify({
                from: String(chatId),
                body: msg.text || '',
                timestamp: msg.date
            });
            
            const req = http.request(`${NEXUS_API}/message`, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'Content-Length': Buffer.byteLength(body)
                }
            });
            req.on('error', (e) => console.error('[Nexus Bridge] Forward error:', e.message));
            req.write(body);
            req.end();
        } catch (err) {
            console.error('[Nexus Bridge] Error forwarding message:', err);
        }
    });

    // HTTP server to receive replies from agent
    const server = http.createServer((req, res) => {
        if (req.method !== 'POST' || req.url !== '/send') {
            res.writeHead(404).end();
            return;
        }
        let body = '';
        req.on('data', chunk => body += chunk);
        req.on('end', async () => {
            try {
                const { to, message } = JSON.parse(body);
                await bot.sendMessage(to, message, { parse_mode: 'Markdown' });
                res.writeHead(200).end(JSON.stringify({ ok: true }));
            } catch (err) {
                console.error('[Nexus Bridge] Send error:', err);
                res.writeHead(500).end(JSON.stringify({ error: err.message }));
            }
        });
    });

    server.listen(5001, '127.0.0.1', () => {
        console.log('[Nexus Bridge] Listening on port 5001');
    });

    bot.setMyCommands([
        { command: 'start', description: 'Iniciar sesión' },
        { command: 'help', description: 'Mostrar ayuda' }
    ]);
    
    console.log('[Nexus Bridge] Telegram bot started!');
    """;

    private const string WhatsAppPackageJson = """
    {
      "name": "nexus-bridge",
      "version": "1.0.0",
      "description": "WhatsApp bridge for Nexus bot",
      "main": "index.js",
      "scripts": {
        "start": "node index.js"
      },
      "dependencies": {
        "whatsapp-web.js": "^1.26.0",
        "qrcode-terminal": "^0.12.0"
      }
    }
    """;

    private const string BridgeWhatsAppJs = """
    const { Client, LocalAuth } = require('whatsapp-web.js');
    const qrcode = require('qrcode-terminal');
    const http = require('http');
    const fs = require('fs');


    const NEXUS_API = 'http://localhost:5000';
    const BRIDGE_PORT = 5001;

    // ─── WhatsApp client ────────────────────────────────────────────────────────
    const client = new Client({
        authStrategy: new LocalAuth({ dataPath: './session' }),
        puppeteer: { headless: true, args: ['--no-sandbox'] }
    });

    client.on('qr', (qr) => {
        console.log('\n[Nexus Bridge] Scan this QR code with your WhatsApp:\n');
        qrcode.generate(qr, { small: true });
    });

    client.on('ready', () => {
        console.log('[Nexus Bridge] ✅ WhatsApp connected!');
        const cfg = JSON.parse(fs.readFileSync(
          process.env.HOME + '/.nexus/config.json', 'utf-8'
        ));
        const target = cfg.authorizedNumber + '@c.us';
        client.sendMessage(target, '🚀 *Nexus conectado!*\nEnviá un mensaje para empezar.');
    });

    client.on('disconnected', (reason) => {
        console.log('[Nexus Bridge] ⚠ Disconnected:', reason);
    });

    // ─── Receive messages and forward to .NET ──────────────────────────────────
    client.on('message', async (msg) => {
        if (msg.isGroupMsg) return;
        if (msg.from === 'status@broadcast') return;
        if (msg.fromMe) return;
            
        const cfg = JSON.parse(fs.readFileSync(
            process.env.HOME + '/.nexus/config.json', 'utf-8'
        ));
        const authorized = cfg.authorizedNumber + '@c.us';
        if (msg.from !== authorized) return;
    
        try {
            const body = JSON.stringify({
                from: msg.from,
                body: msg.body,
                timestamp: msg.timestamp
            });
        
            const req = http.request(`${NEXUS_API}/message`, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'Content-Length': Buffer.byteLength(body)
                }
            });
        
            req.on('error', (e) => console.error('[Nexus Bridge] Forward error:', e.message));
            req.write(body);
            req.end();
        } catch (err) {
            console.error('[Nexus Bridge] Error forwarding message:', err);
        }
    });

    // ─── HTTP server to receive replies from .NET ──────────────────────────────
    const server = http.createServer((req, res) => {
        if (req.method !== 'POST' || req.url !== '/send') {
            res.writeHead(404).end();
            return;
        }

        let body = '';
        req.on('data', chunk => body += chunk);
        req.on('end', async () => {
            try {
                const { to, message } = JSON.parse(body);
                await client.sendMessage(to, message);
                res.writeHead(200).end(JSON.stringify({ ok: true }));
            } catch (err) {
                console.error('[Nexus Bridge] Send error:', err);
                res.writeHead(500).end(JSON.stringify({ error: err.message }));
            }
        });
    });

    server.listen(BRIDGE_PORT, '127.0.0.1', () => {
        console.log(`[Nexus Bridge] Listening on port ${BRIDGE_PORT}`);
    });

    client.initialize();
    """;
}
