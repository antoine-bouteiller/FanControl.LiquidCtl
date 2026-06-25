import os
import sys

from liquidctl_server.service import config


class TestIsBundled:
    def test_source_run_is_not_bundled(self, monkeypatch):
        monkeypatch.delattr(sys, "frozen", raising=False)
        monkeypatch.delattr(config, "__compiled__", raising=False)
        assert config._is_bundled() is False

    def test_pyinstaller_frozen_is_bundled(self, monkeypatch):
        monkeypatch.delattr(config, "__compiled__", raising=False)
        monkeypatch.setattr(sys, "frozen", True, raising=False)
        assert config._is_bundled() is True

    def test_nuitka_compiled_is_bundled(self, monkeypatch):
        monkeypatch.delattr(sys, "frozen", raising=False)
        monkeypatch.setattr(config, "__compiled__", True, raising=False)
        assert config._is_bundled() is True


class TestPluginDir:
    def test_source_run_returns_none(self, monkeypatch):
        monkeypatch.setattr(config, "_is_bundled", lambda: False)
        assert config._plugin_dir() is None

    def test_bundled_returns_parent_of_exe_dir(self, monkeypatch):
        monkeypatch.setattr(config, "_is_bundled", lambda: True)
        exe = os.path.join("plugins", "liquidctl_server", "liquidctl_server.exe")
        monkeypatch.setattr(sys, "executable", exe)
        assert config._plugin_dir() == "plugins"


class TestReadFilterPattern:
    def test_no_plugin_dir_returns_none(self, monkeypatch):
        monkeypatch.setattr(config, "_plugin_dir", lambda: None)
        assert config._read_filter_pattern() is None

    def test_missing_file_returns_none(self, monkeypatch, tmp_path):
        monkeypatch.setattr(config, "_plugin_dir", lambda: str(tmp_path))
        assert config._read_filter_pattern() is None

    def test_first_real_line_returned_skipping_comments(self, monkeypatch, tmp_path):
        (tmp_path / config.DEVICE_FILTER_FILE).write_text(
            "# comment\n\n  NZXT  \nCorsair\n", encoding="utf-8"
        )
        monkeypatch.setattr(config, "_plugin_dir", lambda: str(tmp_path))
        assert config._read_filter_pattern() == "NZXT"

    def test_comments_only_returns_none(self, monkeypatch, tmp_path):
        (tmp_path / config.DEVICE_FILTER_FILE).write_text(
            "# only a comment\n\n", encoding="utf-8"
        )
        monkeypatch.setattr(config, "_plugin_dir", lambda: str(tmp_path))
        assert config._read_filter_pattern() is None

    def test_os_error_returns_none(self, monkeypatch, tmp_path):
        (tmp_path / config.DEVICE_FILTER_FILE).write_text("NZXT\n", encoding="utf-8")
        monkeypatch.setattr(config, "_plugin_dir", lambda: str(tmp_path))

        def boom(*args, **kwargs):
            raise OSError("cannot read")

        monkeypatch.setattr("builtins.open", boom)
        assert config._read_filter_pattern() is None


class TestLoadDeviceFilter:
    def test_no_pattern_returns_none(self, monkeypatch):
        monkeypatch.setattr(config, "_read_filter_pattern", lambda: None)
        assert config.load_device_filter() is None

    def test_valid_pattern_is_case_insensitive(self, monkeypatch):
        monkeypatch.setattr(config, "_read_filter_pattern", lambda: "NZXT")
        compiled = config.load_device_filter()
        assert compiled is not None
        assert compiled.search("nzxt kraken x63")
        assert compiled.search("ASUS Aura LED Controller") is None

    def test_invalid_regex_returns_none(self, monkeypatch):
        monkeypatch.setattr(config, "_read_filter_pattern", lambda: "[")
        assert config.load_device_filter() is None
