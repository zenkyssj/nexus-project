class NexusLLM:
    def __init__(self, config):
        self.config = config

    def process(self, user_message: str) -> str:
        return f"Recibi: {user_message}"
