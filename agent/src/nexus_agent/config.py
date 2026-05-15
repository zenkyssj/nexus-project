import json
import os
from dataclasses import dataclass, field
from typing import Optional

CONFIG_DIR = os.path.expanduser("~/.nexus")
CONFIG_PATH = os.path.join(CONFIG_DIR, "config.json")

@dataclass
class ToolsConfig:
    execute_command: bool = False
    read_file: bool = True 
    write_file: bool = False 
    list_directory: bool = True 
    get_processes: bool = True 
    get_services: bool = True 
    start_service: bool = False 
    stop_service: bool = False 

@dataclass
class CustomCommand:
    command: str = ""
    description: str = ""
    action: str = ""
    target: str = ""

@dataclass
class NexusConfig:
    channel: str = "whatsapp"
    authorized_number: str = ""
    telegram_token: str = ""
    claude_api_key: str = ""
    allowed_paths: list = field(default_factory=lambda: [os.path.expanduser("~")])
    session_timeout_minutes: int = 30
    tools: ToolsConfig = field(default_factory=ToolsConfig)
    custom_commands: list = field(default_factory=list)

    @classmethod
    def from_dict(cls, d: dict) -> "NexusConfig":
        tools_data = d.get("tools", {})
        tools = ToolsConfig(
            execute_command=tools_data.get("execute_command", False),
            read_file=tools_data.get("readFile", True),
            write_file=tools_data.get("writeFile", False),
            list_directory=tools_data.get("listDirectory", True),
            get_processes=tools_data.get("getProcesses", True),
            get_services=tools_data.get("getServices", True),
            start_service=tools_data.get("startService", False),
            stop_service=tools_data.get("stopService", False),
        )

        raw_commands = d.get("customCommands", [])
        custom_commands = [CustomCommand(**c) for c in raw_commands]

        return cls(
            channel=d.get("channel", "whatsapp"),
            authorized_number=d.get("authorizedNumber", ""),
            telegram_token=d.get("telegramToken", ""),
            claude_api_key=d.get("claudeApiKey", ""),
            allowed_paths=d.get("allowedPaths", [os.path.expanduser("~")]),
            session_timeout_minutes=d.get("sessionTimeoutMinutes", 30),
            tools=tools,
            custom_commands=custom_commands,
        )
    
def load_config() -> NexusConfig:
    if not os.path.exists(CONFIG_PATH):
        print(f"[Nexus Agent] Config not found at {CONFIG_PATH}")
        print("[Nexus Agent] Run 'nexus init' first")
        sys.exit(1)

    with open(CONFIG_PATH, "r") as f:
        raw = json.load(f)

    return NexusConfig.from_dict(raw)
