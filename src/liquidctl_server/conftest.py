import sys
from unittest.mock import MagicMock

if sys.platform != "win32":
    sys.modules.setdefault("liquidctl_server.win32_pipe", MagicMock())
    sys.modules.setdefault("liquidctl_server.pipe_server", MagicMock())
