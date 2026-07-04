import logging
import os
import re
import sys
from typing import Optional, Pattern

logger = logging.getLogger(__name__)

DEVICE_OPERATION_TIMEOUT: float = 5.0
DEVICE_STATUS_TIMEOUT: float = 0.5
MAX_INIT_RETRIES: int = 3
DUTY_CACHE_TTL: float = 30.0

# Optional device allowlist. Drop a file with this name in the plugin folder
# containing a single regex line; only devices whose description matches
# (case-insensitive) are connected. Absent file = all devices. Blank lines and
# lines starting with '#' are ignored.
DEVICE_FILTER_FILE: str = "liquidctl_filter.txt"


def _is_bundled() -> bool:
    """True when running as the built bridge exe (Nuitka or PyInstaller)."""
    # PyInstaller sets sys.frozen; Nuitka injects __compiled__ into each module.
    return getattr(sys, "frozen", False) or "__compiled__" in globals()


def _plugin_dir() -> Optional[str]:
    """Plugin folder (parent of the bridge exe folder), or None in source runs."""
    if not _is_bundled():
        return None
    return os.path.dirname(os.path.dirname(sys.executable))


def _read_filter_pattern() -> Optional[str]:
    """Return the first non-empty, non-comment line of the filter file, if any."""
    plugin_dir = _plugin_dir()
    if plugin_dir is None:
        return None
    path = os.path.join(plugin_dir, DEVICE_FILTER_FILE)
    if not os.path.isfile(path):
        return None
    try:
        with open(path, "r", encoding="utf-8") as handle:
            for line in handle:
                line = line.strip()
                if line and not line.startswith("#"):
                    logger.info("Using device filter from %s: %r", path, line)
                    return line
    except OSError as err:
        logger.warning("Could not read device filter %s: %s", path, err)
    return None


def load_device_filter() -> Optional[Pattern[str]]:
    """Compile the configured device-description filter, or None if not set."""
    pattern = _read_filter_pattern()
    if not pattern:
        return None
    try:
        return re.compile(pattern, re.IGNORECASE)
    except re.error as err:
        logger.error("Invalid device filter regex %r: %s", pattern, err)
        return None
