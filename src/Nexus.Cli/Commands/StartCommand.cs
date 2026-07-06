using Nexus.Core.Configuration;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Diagnostics;
using System.Text;

namespace Nexus.Cli.Commands
{
    public static class StartCommand
    {
        public static Command Build()
        {
            var cmd = new Command("start", "Start the Nexus bot");

            var daemonOption = new Option<bool>("--daemon", "Run in background (detached)");
            cmd.AddOption(daemonOption);

            cmd.SetHandler(async (InvocationContext ctx) =>
            {
                var daemon = ctx.ParseResult.GetValueForOption(daemonOption);
                if (!ConfigManager.ConfigExists())
                {
                    AnsiConsole.MarkupLine("[red]No config found. Run [bold]nexus init[/] first.[/]");
                    return;
                }

                var config = ConfigManager.Load();

                if (config.Channel == "telegram" && string.IsNullOrEmpty(config.TelegramToken))
                {
                    AnsiConsole.MarkupLine("[red]Telegram token not set. Run [bold]nexus init[/] again.[/]");
                    return;
                }

                if (string.IsNullOrEmpty(config.ClaudeApiKey))
                {
                    AnsiConsole.MarkupLine("[red]Claude API key not set. Run [bold]nexus config claude-key <key>[/][/]");
                    return;
                }

                AnsiConsole.MarkupLine("[bold cyan]Starting Nexus...[/]");

                // Start Node bridge
                var bridgePath = ConfigManager.GetBridgePath();
                var bridgeIndex = Path.Combine(bridgePath, "index.js");

                if (!File.Exists(bridgeIndex))
                {
                    AnsiConsole.MarkupLine("[red]Bridge not found. Run [bold]nexus init[/] again.[/]");
                    return;
                }

                AnsiConsole.MarkupLine($"[grey]→ Starting {config.Channel} bridge...[/]");

                var node = OperatingSystem.IsWindows() ? "node.exe" : "node";
                var bridgeProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = node,
                        Arguments = bridgeIndex,
                        WorkingDirectory = bridgePath,
                        UseShellExecute = false,
                        CreateNoWindow = daemon
                    }
                };

                bridgeProcess.Start();

                // Start the Python Agent 
                var agentPath = ConfigManager.GetAgentPath();
                var python = OperatingSystem.IsWindows() ? "python" : "python3";
                var pip = OperatingSystem.IsWindows() ? "python" : "python3";

                // requirements.txt is extracted alongside nexus_agent inside agentPath
                var requirementsPath = Path.Combine(agentPath, "requirements.txt");

                if (File.Exists(requirementsPath))
                {
                    AnsiConsole.MarkupLine("[grey]→ Installing agent dependencies...[/]");
                    var pipArgs = OperatingSystem.IsWindows()
                        ? $"-m pip install -r \"{requirementsPath}\" -q"
                        : $"-m pip install -r \"{requirementsPath}\" -q --break-system-packages";

                    var pipProcess = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = pip,
                            Arguments = pipArgs,
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                        }
                    };
                    pipProcess.Start();
                    await pipProcess.WaitForExitAsync();

                    if (pipProcess.ExitCode != 0)
                    {
                        var err = await pipProcess.StandardError.ReadToEndAsync();
                        AnsiConsole.MarkupLine($"[red]Failed to install dependencies: {err}[/]");
                        return;
                    }
                    AnsiConsole.MarkupLine("[grey]✓ Dependencies ready[/]");
                }

                var agentProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = python,
                        Arguments = "-m nexus_agent",
                        WorkingDirectory = agentPath,
                        UseShellExecute = false,
                        CreateNoWindow = daemon,
                    }
                };

                agentProcess.Start();

                var openMsg = config.Channel == "whatsapp"
                  ? "Open WhatsApp on your phone to scan de QR code if prompted."
                  : "Open Telegram and send /start to your bot.";

                AnsiConsole.MarkupLine($"[green]✅ Bridge started (PID {bridgeProcess.Id})[/]");
                AnsiConsole.MarkupLine($"[green]✅ Agent started (PID {agentProcess.Id})[/]");
                AnsiConsole.MarkupLine("[bold green]✅ Nexus is running![/]");
                AnsiConsole.MarkupLine($"\n{openMsg}");
                AnsiConsole.MarkupLine("[grey]Press Ctrl+C to stop.[/]");

                if (!daemon)
                {
                    var tasks = new[] { bridgeProcess.WaitForExitAsync(), agentProcess.WaitForExitAsync()};
                    await Task.WhenAny(tasks);
                }
            });

            return cmd;
        }
    }
}
