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

                // ── Kill any leftover processes on ports 5000/5001 ─────────────────
                await KillPortAsync(5000);
                await KillPortAsync(5001);

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

// ── Install Python deps ───────────────────────────────────────────
                var agentPath = ConfigManager.GetAgentPath();
                var python = OperatingSystem.IsWindows() ? "python" : "python3";
                var requirementsPath = Path.Combine(agentPath, "requirements.txt");

                if (File.Exists(requirementsPath))
                {
                    AnsiConsole.MarkupLine("[grey]→ Installing agent dependencies...[/]");

                    // Prefer pip3 directly; fall back to python3 -m pip
                    // Some distros ship pip3 as a binary but not as a python module
                    var pipInstalled = await TryPipInstallAsync("pip3",
                        $"install -r \"{requirementsPath}\" -q --break-system-packages");

                    if (!pipInstalled)
                    {
                        pipInstalled = await TryPipInstallAsync(python,
                            $"-m pip install -r \"{requirementsPath}\" -q --break-system-packages");
                    }

                    if (!pipInstalled && OperatingSystem.IsWindows())
                    {
                        pipInstalled = await TryPipInstallAsync("pip",
                            $"install -r \"{requirementsPath}\" -q");
                    }

                    if (!pipInstalled)
                    {
                        AnsiConsole.MarkupLine("[red]Failed to install Python dependencies automatically.[/]");
                        AnsiConsole.MarkupLine($"[grey]Run manually: pip3 install anthropic --break-system-packages[/]");
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

                // ── Helpers ───────────────────────────────────────────────────────────────

        /// <summary>Tries to run a pip command, returns true if exit code is 0.</summary>
        private static async Task<bool> TryPipInstallAsync(string exe, string args)
        {
            try
            {
                var p = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = exe,
                        Arguments = args,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                    }
                };
                p.Start();
                await p.WaitForExitAsync();
                return p.ExitCode == 0;
            }
            catch
            {
                return false; // executable not found
            }
        }

        /// <summary>
        /// Kills any process currently listening on the given TCP port.
        /// Prevents EADDRINUSE on nexus start after a dirty shutdown.
        /// </summary>
        private static async Task KillPortAsync(int port)
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    // netstat + taskkill
                    var find = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = "cmd.exe",
                            Arguments = $"/c for /f \"tokens=5\" %a in ('netstat -aon ^| findstr :{port}') do taskkill /F /PID %a",
                            UseShellExecute = false, CreateNoWindow = true,
                            RedirectStandardOutput = true, RedirectStandardError = true,
                        }
                    };
                    find.Start();
                    await find.WaitForExitAsync();
                }
                else
                {
                    // fuser -k is the most portable on Linux
                    var fuser = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = "fuser",
                            Arguments = $"-k {port}/tcp",
                            UseShellExecute = false, CreateNoWindow = true,
                            RedirectStandardOutput = true, RedirectStandardError = true,
                        }
                    };
                    fuser.Start();
                    await fuser.WaitForExitAsync();
                }
            }
            catch
            {
                // fuser may not be installed — not fatal, the bridge will fail
                // with a clearer EADDRINUSE message anyway
            }
        }
    }
}
