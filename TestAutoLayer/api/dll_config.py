"""
DLL Configuration for WPF Test Auto
===================================
Centralized configuration for DLL paths and staging logic.
Supports debug, release, and production configurations.
"""

import os
from pathlib import Path
from typing import List, Dict, Optional
from dataclasses import dataclass, field


@dataclass
class DllPaths:
    """Paths to all required DLLs for a given configuration."""

    # Core agent DLLs
    wpf_spy_agent: str = "WpfSpyAgent.dll"
    wpf_spy_agent_startup_hook: str = "WpfSpyAgent.StartupHook.dll"
    wpf_spy_agent_framework_hook: str = "WpfSpyAgent.FrameworkHook.dll"
    wpf_spy_agent_native_inject: str = "WpfSpyAgent.NativeInject.dll"
    newtonsoft_json: str = "Newtonsoft.Json.dll"

    # Source directories (relative to repo root)
    bin_debug_net9: str = "bin/Debug/net9.0-windows"
    bin_release_net9: str = "bin/Release/net9.0-windows"

    # Per-project source directories
    startup_hook_debug: str = "WPFSpyAgent/StartupHook/bin/Debug/net9.0-windows"
    startup_hook_release: str = "WPFSpyAgent/StartupHook/bin/Release/net9.0-windows"
    framework_hook_debug: str = "WPFSpyAgent/FrameworkHook/bin/Debug/net461"
    framework_hook_release: str = "WPFSpyAgent/FrameworkHook/bin/Release/net461"
    native_inject_debug: str = "WPFSpyAgent/NativeInject/bin/Debug/x64"
    native_inject_release: str = "WPFSpyAgent/NativeInject/bin/Release/x64"

    # Sample app location (moved under Tests/)
    sample_app_net9: str = "Tests/bin/SampleWPFApp/Debug/net9.0-windows"
    sample_app_net461: str = "Tests/bin/SampleWPFApp/Debug/net461"
    sample_app_release_net9: str = "Tests/bin/SampleWPFApp/Release/net9.0-windows"
    sample_app_release_net461: str = "Tests/bin/SampleWPFApp/Release/net461"


class DllConfig:
    """Centralized DLL configuration with environment variable overrides."""

    _instance = None

    def __new__(cls):
        if cls._instance is None:
            cls._instance = super().__new__(cls)
            cls._instance._initialized = False
        return cls._instance

    def __init__(self):
        if self._initialized:
            return
        self._initialized = True
        self.paths = DllPaths()
        self._load_from_env()

    def _load_from_env(self):
        """Load configuration from environment variables."""
        # Override source directories
        for attr in [
            "bin_debug_net9", "bin_debug_net8", "bin_debug_net461",
            "bin_release_net9", "bin_release_net8", "bin_release_net461",
            "startup_hook_debug", "startup_hook_release",
            "framework_hook_debug", "framework_hook_release",
            "native_inject_debug", "native_inject_release",
            "sample_app_net9", "sample_app_net461",
            "sample_app_release_net9", "sample_app_release_net461",
        ]:
            env_key = f"WPFSPY_{attr.upper()}"
            env_val = os.environ.get(env_key)
            if env_val:
                setattr(self.paths, attr, env_val)

        # Override DLL filenames
        for attr in [
            "wpf_spy_agent", "wpf_spy_agent_startup_hook",
            "wpf_spy_agent_framework_hook", "wpf_spy_agent_native_inject",
            "newtonsoft_json",
        ]:
            env_key = f"WPFSPY_DLL_{attr.upper()}"
            env_val = os.environ.get(env_key)
            if env_val:
                setattr(self.paths, attr, env_val)

    def get_repo_root(self) -> Path:
        """Get the repository root directory."""
        return Path(__file__).parent.parent.parent

    def get_search_paths(self, dll_name: str) -> List[Path]:
        """Get all search paths for a given DLL name."""
        repo = self.get_repo_root()
        p = self.paths
        search_paths: List[Path] = []

        if dll_name == p.wpf_spy_agent_startup_hook:
            search_paths = [
                repo / p.bin_debug_net9,
                repo / p.startup_hook_debug,
                repo / p.startup_hook_release,
                repo / p.bin_release_net9,
            ]
        elif dll_name == p.wpf_spy_agent_framework_hook:
            search_paths = [
                repo / p.framework_hook_debug,
                repo / p.framework_hook_release,
                repo / p.bin_debug_net461,
                repo / p.bin_release_net461,
            ]
        elif dll_name == p.wpf_spy_agent_native_inject:
            search_paths = [
                repo / p.bin_debug_net9,
                repo / p.bin_release_net9,
                repo / p.native_inject_debug,
                repo / p.native_inject_release,
            ]
        elif dll_name == p.wpf_spy_agent:
            search_paths = [
                repo / p.bin_debug_net9,
                repo / p.bin_debug_net461,
                repo / p.bin_release_net9,
                repo / p.bin_release_net461,
            ]
        elif dll_name == p.newtonsoft_json:
            search_paths = [
                repo / p.bin_debug_net461,
                repo / p.bin_release_net461,
                repo / p.bin_debug_net9,
                repo / p.bin_release_net9,
            ]

        return search_paths

    def find_dll(self, dll_name: str) -> Optional[Path]:
        """Find a DLL in the configured search paths."""
        for search_dir in self.get_search_paths(dll_name):
            dll_path = search_dir / dll_name
            if dll_path.exists():
                return dll_path.resolve()
        return None

    def get_framework_agent_dir(self) -> Optional[Path]:
        """Get the directory containing the .NET Framework build of the Spy Agent."""
        repo = self.get_repo_root()
        p = self.paths
        candidates = [
            repo / p.bin_debug_net461,
            repo / p.bin_release_net461,
        ]
        for c in candidates:
            if c.is_dir() and (c / p.wpf_spy_agent).exists():
                return c
        return None

    def get_sample_app_dir(self, framework: str = "framework") -> Path:
        """Get the SampleWpfApp directory for the given framework."""
        repo = self.get_repo_root()
        p = self.paths
        if framework == "framework":
            return repo / p.sample_app_net461
        else:
            return repo / p.sample_app_net9

    def get_staged_dlls(self, framework: str = "framework") -> List[str]:
        """Get the list of DLL names to stage for the given framework."""
        p = self.paths
        if framework == "framework":
            return [
                p.wpf_spy_agent,
                p.wpf_spy_agent_framework_hook,
                p.newtonsoft_json,
            ]
        else:
            return [
                p.wpf_spy_agent,
                p.wpf_spy_agent_startup_hook,
            ]


# Global config instance
dll_config = DllConfig()