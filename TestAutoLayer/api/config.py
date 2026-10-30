"""
Framework Configuration
=======================
Centralized configuration management for the WPF Test Automation Framework.
All configuration can be overridden via environment variables or .env file.
"""

from typing import List, Dict
from pydantic_settings import BaseSettings
from pydantic import field_validator
import os


class FrameworkConfig(BaseSettings):
    """Centralized configuration for the test automation framework."""

    # Driver configuration
    DRIVER_ORDER: List[str] = ["FlaUI", "WPFSpy", "Sikuli"]

    # Timeouts (seconds)
    DEFAULT_TIMEOUT: float = 30.0
    DRIVER_TIMEOUTS: Dict[str, float] = {
        "FlaUI": 10.0,
        "WPFSpy": 15.0,
        "Sikuli": 30.0,
    }

    # Retry configuration
    MAX_RETRIES: int = 3
    RETRY_DELAY: float = 0.5

    # Circuit breaker configuration
    CIRCUIT_BREAKER_THRESHOLD: int = 3
    CIRCUIT_BREAKER_TIMEOUT: int = 60

    # Logging
    LOG_LEVEL: str = "INFO"
    LOG_STRUCTURED: bool = True

    # App lifecycle
    APP_STARTUP_DELAY: float = 5.0
    APP_RESET_DELAY: float = 2.0

    model_config = {
        "env_file": ".env",
        "env_file_encoding": "utf-8",
        "case_sensitive": False,
    }

    @field_validator("DRIVER_ORDER", mode="before")
    @classmethod
    def _parse_driver_order(cls, v):
        if isinstance(v, str):
            return [d.strip() for d in v.split(",")]
        return v

    @field_validator("DRIVER_TIMEOUTS", mode="before")
    @classmethod
    def _parse_driver_timeouts(cls, v):
        result = dict(v) if isinstance(v, dict) else {}
        for driver in ("FlaUI", "WPFSpy", "Sikuli"):
            env_val = os.environ.get(f"{driver.upper()}_TIMEOUT")
            if env_val:
                try:
                    result[driver] = float(env_val)
                except ValueError:
                    pass
        return result if result else v

    def get_driver_timeout(self, driver_name: str) -> float:
        """Get timeout for a specific driver."""
        return self.DRIVER_TIMEOUTS.get(driver_name, self.DEFAULT_TIMEOUT)


# Global config instance
config = FrameworkConfig()
