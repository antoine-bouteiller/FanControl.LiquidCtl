import re


def format_status_key(string: str) -> str:
    """
    Format status key by normalizing fan names and removing 'duty' suffix.

    Examples:
        "Fan 1 duty" -> "fan1"
        "Fan 2 speed" -> "fan2 speed"
        "Pump duty" -> "pump"
        "Temperature" -> "temperature"
    """
    string = string.lower()

    fan_pattern = re.match(r"fan\s*(\d+)(?:\s*duty)?(.*)", string)
    if fan_pattern:
        return f"fan{fan_pattern.group(1)}{fan_pattern.group(2)}"

    generic_pattern = re.match(r"(.*?)(?:\s*duty)", string)
    if generic_pattern:
        return generic_pattern.group(1)

    return string
