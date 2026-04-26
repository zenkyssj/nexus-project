using System.CommandLine;
using Nexus.Cli.Commands;

// ─── Banner ───────────────────────────────────────────────────────────────────
Console.WriteLine("""
 
  ███╗   ██╗███████╗██╗  ██╗██╗   ██╗███████╗
  ████╗  ██║██╔════╝╚██╗██╔╝██║   ██║██╔════╝
  ██╔██╗ ██║█████╗   ╚███╔╝ ██║   ██║███████╗
  ██║╚██╗██║██╔══╝   ██╔██╗ ██║   ██║╚════██║
  ██║ ╚████║███████╗██╔╝ ██╗╚██████╔╝███████║
  ╚═╝  ╚═══╝╚══════╝╚═╝  ╚═╝ ╚═════╝ ╚══════╝  v1.0.0 - by zenkyssj
 
  WhatsApp bot + AI assistant for your server
""");

var root = new RootCommand("Nexus - Server Assistant");

root.AddCommand(InitCommand.Build());
root.AddCommand(StartCommand.Build());

return await root.InvokeAsync(args);
