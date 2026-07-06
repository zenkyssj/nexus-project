using System.CommandLine;
using System.CommandLine.Invocation;
using System.Diagnostics;
using Nexus.Core.Configuration;
using Spectre.Console;

namespace Nexus.Cli.Commands
{
    public static class AlertCommand
    {
        public static Command Build()
        {
            var cmd = new Command("alert", "Manage server alerts");

            cmd.SetHandler(async (InvocationContext ctx) =>
            {
                await RunAsync();
            });

            return cmd;
        }

        public static async Task RunAsync()
        {
            AnsiConsole.MarkupLine("[bold cyan] Alert save successfully[/]\n");  
            return;
            // Implement the logic for the alert command here
        }
    }
}