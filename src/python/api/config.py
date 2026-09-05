"""
Framework Configuration
======================
Centralized configuration management for the WPF Test Automation Framework.
All configuration can be overridden via environment variables or config file.
"""

import os
from typing import List, Dict


class FrameworkConfig:
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
        self._load_from_env()
    
    def _load_from_env(self):
        """Load configuration from environment variables."""
        # Driver order
        driver_order_env = os.environ.get("DRIVER_ORDER")
        if driver_order_env:
            self.DRIVER_ORDER = [d.strip() for d in driver_order_env.split(",")]
        
        # Timeouts
        timeout_env = os.environ.get("DEFAULT_TIMEOUT")
        if timeout_env:
            self.DEFAULT_TIMEOUT = float(timeout_env)
        
        for driver, env_key in [
            ("FlaUI", "FLAUI_TIMEOUT"),
            ("WPFSpy", "WPFSPY_TIMEOUT"),
            ("Sikuli", "SIKULI_TIMEOUT"),
        ]:
            env_val = os.environ.get(env_key)
            if env_val:
                self.DRIVER_TIMEOUTS[driver] = float(env_val)
        
        # Retry configuration
        max_retries = os.environ.get("MAX_RETRIES")
        if max_retries:
            self.MAX_RETRIES = int(max_retries)
        
        retry_delay = os.environ.get("RETRY_DELAY")
        if retry_delay:
            self.RETRY_DELAY = float(retry_delay)
        
        # Circuit breaker
        cb_threshold = os.environ.get("CIRCUIT_BREAKER_THRESHOLD")
        if cb_threshold:
            self.CIRCUIT_BREAKER_THRESHOLD = int(cb_threshold)
        
        cb_timeout = os.environ.get("CIRCUIT_BREAKER_TIMEOUT")
        if cb_timeout:
            self.CIRCUIT_BREAKER_TIMEOUT = int(cb_timeout)
        
        # Logging
        log_level = os.environ.get("LOG_LEVEL")
        if log_level:
            self.LOG_LEVEL = log_level.upper()
        
        log_structured = os.environ.get("LOG_STRUCTURED")
        if log_structured:
            self.LOG_STRUCTURED = log_structured.lower() == "true"
        
        # App lifecycle
        startup_delay = os.environ.get("APP_STARTUP_DELAY")
        if startup_delay:
            self.APP_STARTUP_DELAY = float(startup_delay)
        
        reset_delay = os.environ.get("APP_RESET_DELAY")
        if reset_delay:
            self.APP_RESET_DELAY = float(reset_delay)
    
    def get_driver_timeout(self, driver_name: str) -> float:
        """Get timeout for a specific driver."""
        return self.DRIVER_TIMEOUTS.get(driver_name, self.DEFAULT_TIMEOUT)
    
    def reset(self):
        """Reset configuration (useful for testing)."""
        self._initialized = False
        self.__init__()


# Global config instance
config = FrameworkConfig()
