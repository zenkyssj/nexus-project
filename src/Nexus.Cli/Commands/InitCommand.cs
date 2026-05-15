using System.CommandLine;
using System.CommandLine.Invocation;
using System.Diagnostics;
using Nexus.Core.Configuration;
using Spectre.Console;

namespace Nexus.Cli.Commands
{
    public static class InitCommand
    {
        public static Command Build()
        {
            var cmd = new Command("init", "Configure Nexus for the first time");

            var forceOption = new Option<bool>("--force", "Overwrite existing configuration");
            cmd.AddOption(forceOption);

            cmd.SetHandler(async (InvocationContext ctx) =>
            {
                var force = ctx.ParseResult.GetValueForOption(forceOption);
                await RunAsync(force);
            });

            return cmd;
        }

        public static async Task RunAsync(bool force)
        {
            if (ConfigManager.ConfigExists() && !force)
            {
                AnsiConsole.MarkupLine("[yellow]⚠ Config already exists.[/] Use [bold]nexus init --force[/] to overwrite.");
                AnsiConsole.MarkupLine($"[grey]Config path: {ConfigManager.GetConfigPath()}[/]");
                return;
            }

            AnsiConsole.MarkupLine("[bold cyan]Starting Nexus setup...[/]\n");

            var channel = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                .Title("[bold]?[/] Select the communication channel: ")
                .AddChoices("whatsapp", "telegram")
            );


            string? phoneNumber = null;
            string? telegramToken = null;
            
            if (channel == "whatsapp")
            {
                phoneNumber = AnsiConsole.Ask<string>(
                    "[bold]?[/] Your WhatsApp number [grey](with country code, e.g. 595981234567):[/]"
                ).Trim();

                while (!IsValidPhone(phoneNumber))
                {
                  AnsiConsole.MarkupLine("[red]Invalid number. Include country code, digits only.[/]");
                  phoneNumber = AnsiConsole.Ask<string>(
                      "[bold]?[/] Your WhatsApp number:"
                  ).Trim();
                }
            }
            else
            {
                telegramToken = AnsiConsole.Prompt(
                    new TextPrompt<string>("[bold]?[/] Telegram bot token [grey](from @BotFather):[/]")
                      .Secret()
                ).Trim();

                while (string.IsNullOrEmpty(telegramToken))
                {
                  telegramToken = AnsiConsole.Prompt(
                      new TextPrompt<string>("[bold]?[/] Telegram bot token:").Secret()
                  ).Trim();
                }
            }

            // ─── Claude API Key ───────────────────────────────────────────────────
            var claudeKey = AnsiConsole.Prompt(
                new TextPrompt<string>("[bold]?[/] Claude API key [grey](sk-ant-...):[/]")
                    .Secret()
            ).Trim();

            while (!claudeKey.StartsWith("sk-ant-"))
            {
                AnsiConsole.MarkupLine("[red]Invalid API key. Should start with sk-ant-[/]");
                claudeKey = AnsiConsole.Prompt(
                    new TextPrompt<string>("[bold]?[/] Claude API key:").Secret()
                ).Trim();
            }

            // ─── Allowed path ─────────────────────────────────────────────────────
            var defaultPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var allowedPath = AnsiConsole.Ask(
                $"[bold]?[/] Allowed root folder for the AI assistant [grey](default: {defaultPath}):[/]",
                defaultPath
            ).Trim();

            if (!Directory.Exists(allowedPath))
            {
                AnsiConsole.MarkupLine($"[yellow]⚠ Directory does not exist. It will be used anyway, make sure it exists when the bot runs.[/]");
            }

            // ─── Enable PowerShell execution ──────────────────────────────────────
            var enableExec = AnsiConsole.Confirm(
                "[bold]?[/] Enable PowerShell command execution? [grey](risky — allows AI to run commands)[/]",
                defaultValue: false
            );

            if (enableExec)
            {
                AnsiConsole.MarkupLine("[yellow]⚠ PowerShell execution enabled. The AI will be able to run commands on your server.[/]");
            }

            // ─── Session timeout ──────────────────────────────────────────────────
            var timeout = AnsiConsole.Ask("[bold]?[/] Session timeout in minutes [grey](default: 30):[/]", 30);

            // ─── Build and save config ────────────────────────────────────────────
            var config = new NexusConfig
            {
                Channel = channel,
                AuthorizedNumber = phoneNumber ?? string.Empty,
                TelegramToken = telegramToken ?? string.Empty,
                ClaudeApiKey = claudeKey,
                AllowedPaths = [allowedPath],
                SessionTimeoutMinutes = timeout,
                Tools = new ToolsConfig
                {
                    ExecuteCommand = enableExec,
                    ReadFile = true,
                    WriteFile = false,
                    ListDirectory = true,
                    GetProcesses = true,
                    GetServices = true,
                    StartService = false,
                    StopService = false
                }
            };

            AnsiConsole.WriteLine();

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("Saving configuration...", async ctx =>
                {
                    ConfigManager.Save(config);
                    await Task.Delay(500);

                    ctx.Status("Installing Node.js bridge...");
                    await InstallBridgeAsync(channel);

                    ctx.Status("Installing Python agent...");
                    await InstallAgentAsync();

                    ctx.Status("Creating directories...");
                    Directory.CreateDirectory(ConfigManager.GetLogsPath());
                    Directory.CreateDirectory(ConfigManager.GetSessionsPath());
                    await Task.Delay(300);
                });

            AnsiConsole.MarkupLine($"[green]✅ Configuration saved:[/] [grey]{ConfigManager.GetConfigPath()}[/]");
            AnsiConsole.MarkupLine("[green]✅ Bridge installed[/]");
            AnsiConsole.MarkupLine("[green]✅ Directories created[/]");

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"Run [bold cyan]nexus start[/] to launch the bot and connect {channel}.");
        }

        private static async Task InstallBridgeAsync(string channel)
        {
            var bridgePath = ConfigManager.GetBridgePath();
            Directory.CreateDirectory(bridgePath);

            // Write bridge files
            await BridgeInstaller.WriteFilesAsync(bridgePath, channel);

            // npm install
            var npm = OperatingSystem.IsWindows() ? "npm.cmd" : "npm";
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = npm,
                    Arguments = "install",
                    WorkingDirectory = bridgePath,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            try
            {
                process.Start();
                await process.WaitForExitAsync();

                if (process.ExitCode != 0)
                {
                    var error = await process.StandardError.ReadToEndAsync();
                    AnsiConsole.MarkupLine($"[yellow]⚠ npm install warning: {error}[/]");
                }
            }
            catch
            {
                AnsiConsole.MarkupLine("[yellow]⚠ Could not run npm install automatically. Run it manually in:[/]");
                AnsiConsole.MarkupLine($"[grey]{bridgePath}[/]");
            }
        }

        private static async Task InstallAgentAsync()
        {
            var agentPath = ConfigManager.GetAgentPath();
            Directory.CreateDirectory(agentPath);

            var agentUrl = "https://github.com/zenkyssj/nexus-project/releases/latest/download/nexus-agent.tar.gz";
            var tarGz = Path.Combine(agentPath, "nexus-agent.tar.gz");

            using var client = new HttpClient();
            var response = await client.GetAsync(agentUrl);

            if (!response.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine("[yellow]⚠ Could not download agent. Make sure Python agent is installed manually.[/]");
                return;
            }

            await using var fs = new FileStream(tarGz, FileMode.Create);
            await response.Content.CopyToAsync(fs);
            fs.Close();

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "tar",
                    Arguments = $"-xzf \"{tarGz}\" -C \"{agentPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            await process.WaitForExitAsync();

            File.Delete(tarGz);
        }

        private static bool IsValidPhone(string phone) =>
            phone.Length >= 8 && phone.All(char.IsDigit);
    }
}
