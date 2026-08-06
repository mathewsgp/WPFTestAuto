"""
Robot Framework App Launcher Library
====================================
Library for launching WPF applications with Spy Agent injection
from Robot Framework tests.

Usage in Robot Framework:
    *** Settings ***
    Library    api.robot_launcher

    *** Test Cases ***
    Launch App With Spy Agent
        ${process}=    Launch Application    C:\\path\\to\\app.exe
        # Run tests...
        Terminate Application    ${process}

    Launch With Custom Pipe
        ${process}=    Launch Application
        ...    app_path=C:\\path\\to\\app.exe
        ...    pipe_name=MyCustomPipe
"""

import os
import sys
import subprocess
import time
from pathlib import Path
from typing import Optional

# Add parent directory to path
sys.path.insert(0, str(Path(__file__).parent))

from runtime_injector import RuntimeInjector, AppLauncher


class RobotLauncher:
    """
    Robot Framework library for launching WPF applications with Spy Agent.
    
    This library provides keywords for Robot Framework tests to:
    - Launch applications with Spy Agent auto-injected
    - Connect to already-running Spy Agent instances
    - Manage application lifecycle
    """

    ROBOT_LIBRARY_SCOPE = "GLOBAL"

    def __init__(self, startup_hook_path: Optional[str] = None):
        """
        Initialize the launcher.
        
        Args:
            startup_hook_path: Path to WpfSpyAgent.StartupHook.dll.
                             If not provided, will search common locations.
        """
        self.injector = RuntimeInjector(startup_hook_path)
        self._processes = {}  # Track launched processes
    
    def launch_application(
        self,
        app_path: str,
        arguments: Optional[str] = None,
        pipe_name: str = "WPFSpyAgentPipe",
        timeout: float = 30.0,
        cwd: Optional[str] = None
    ) -> int:
        """
        Launch an application with Spy Agent injected.
        
        Args:
            app_path: Path to the application executable
            arguments: Command-line arguments (optional)
            pipe_name: Named pipe name for Spy Agent (default: WPFSpyAgentPipe)
            timeout: Max time to wait for agent readiness (default: 30s)
            cwd: Working directory (optional)
            
        Returns:
            Process ID (integer)
            
        Example:
            ${pid}=    Launch Application    C:\\path\\to\\app.exe
        """
        if not Path(app_path).exists():
            raise FileNotFoundError(f"Application not found: {app_path}")
        
        # Find startup hook if not already found
        if not self.injector.startup_hook_path:
            hook_path = os.environ.get("WPFSPY_STARTUP_HOOK_DLL")
            if hook_path:
                self.injector.startup_hook_path = hook_path
        
        if not self.injector.startup_hook_path:
            raise RuntimeError(
                "Startup hook DLL not found. Set WPFSPY_STARTUP_HOOK_DLL "
                "environment variable or build WpfSpyAgent.StartupHook project."
            )
        
        # Build environment
        full_env = os.environ.copy()
        full_env["DOTNET_STARTUP_HOOKS"] = self.injector.startup_hook_path
        full_env["WPFSPY_AGENT_ENABLED"] = "1"
        full_env["WPFSPY_PIPE_NAME"] = pipe_name
        
        # Build command
        cmd = [app_path]
        if arguments:
            cmd.append(str(arguments))
        
        # Launch process
        process = subprocess.Popen(
            cmd,
            env=full_env,
            cwd=cwd,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE
        )
        
        # Wait for agent if needed
        time.sleep(1)  # Give app time to start
        
        self._processes[process.pid] = {
            'process': process,
            'pipe_name': pipe_name,
            'app_path': app_path
        }
        
        return process.pid
    
    def attach_to_application(
        self,
        process_id: int,
        pipe_name: str = "WPFSpyAgentPipe",
        timeout: float = 5.0
    ) -> bool:
        """
        Attach to an already-running application.
        
        Args:
            process_id: PID of the target process
            pipe_name: Named pipe name
            timeout: Connection timeout
            
        Returns:
            True if connected successfully
            
        Example:
            ${connected}=    Attach To Application    ${pid}
        """
        result = self.injector.attach_to_process(process_id, pipe_name, timeout)
        return result.success
    
    def terminate_application(self, process_id: int) -> bool:
        """
        Terminate a launched application.
        
        Args:
            process_id: PID of the process to terminate
            
        Returns:
            True if terminated successfully
            
        Example:
            ${terminated}=    Terminate Application    ${pid}
        """
        if process_id in self._processes:
            proc_info = self._processes[process_id]
            proc = proc_info['process']
            
            proc.terminate()
            try:
                proc.wait(timeout=5)
            except subprocess.TimeoutExpired:
                proc.kill()
            
            del self._processes[process_id]
            return True
        
        # Try to terminate by PID
        try:
            if sys.platform == "win32":
                subprocess.run(['taskkill', '/F', '/PID', str(process_id)], check=True)
                return True
            else:
                subprocess.run(['kill', str(process_id)], check=True)
                return True
        except:
            return False
    
    def terminate_all_applications(self) -> int:
        """
        Terminate all applications launched by this library.
        
        Returns:
            Number of applications terminated
            
        Example:
            ${count}=    Terminate All Applications
        """
        count = 0
        pids = list(self._processes.keys())
        for pid in pids:
            if self.terminate_application(pid):
                count += 1
        return count
    
    def get_process_id(self, app_path: Optional[str] = None) -> Optional[int]:
        """
        Get PID of a running application.
        
        Args:
            app_path: Path to executable (optional)
            
        Returns:
            PID if found, None otherwise
            
        Example:
            ${pid}=    Get Process Id    C:\\path\\to\\app.exe
        """
        for pid, info in self._processes.items():
            if app_path is None or info['app_path'] == app_path:
                if info['process'].poll() is None:  # Still running
                    return pid
        return None
    
    def is_agent_ready(self, pipe_name: str = "WPFSpyAgentPipe") -> bool:
        """
        Check if Spy Agent is ready on the named pipe.
        
        Args:
            pipe_name: Named pipe name
            
        Returns:
            True if agent is ready
            
        Example:
            ${ready}=    Is Agent Ready
        """
        return self.injector.is_agent_running(pipe_name)
    
    def get_startup_hook_path(self) -> Optional[str]:
        """
        Get the path to the startup hook DLL.
        
        Returns:
            Path to DLL or None if not found
        """
        return self.injector.startup_hook_path


# Robot Framework keyword mappings
__all__ = [
    'RobotLauncher',
    'launch_application',
    'attach_to_application',
    'terminate_application',
    'terminate_all_applications',
    'get_process_id',
    'is_agent_ready',
    'get_startup_hook_path'
]
