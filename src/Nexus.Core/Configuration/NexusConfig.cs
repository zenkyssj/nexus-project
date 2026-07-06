using System;
using System.Collections.Generic;
using System.Text;

namespace Nexus.Core.Configuration
{
    public class NexusConfig
    {
        public string ClaudeApiKey { get; set; } = string.Empty;
        public List<string> AllowedPaths { get; set; } = [];
        public int SessionTimeoutMinutes { get; set; } = 30;
        public ToolsConfig Tools { get; set; } = new();
        public List<CustomCommand> CustomCommands { get; set; } = [];
        public string Channel { get; set; } = "whatsapp";
        public string TelegramToken { get; set; } = string.Empty;
    }

    public class ToolsConfig
    {
        public bool ExecuteCommand { get; set; } = false;
        public bool ReadFile { get; set; } = true;
        public bool WriteFile { get; set; } = false;
        public bool ListDirectory { get; set; } = true;
        public bool GetProcesses { get; set; } = true;
        public bool GetServices { get; set; } = true;
        public bool StartService { get; set; } = false;
        public bool StopService { get; set; } = false;
    }

    public class CustomCommand
    {
        public string Command { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty;
        public string Target { get; set; } = string.Empty;
    }
}
