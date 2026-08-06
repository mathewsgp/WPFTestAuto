"""
Runtime Injector for Python
===========================
Provides functionality to inject the Spy Agent into running WPF processes
and launch WPF applications with automatic Spy Agent injection.

Supports:
- Launching apps with DOTNET_STARTUP_HOOKS configured
- Attaching to already-running processes
- Checking if Spy Agent is running in a process
"""

import os
import sys
import subprocess
import time
import json
import socket
from pathlib import Path
from typing import Optional, List, Tuple, Dict, Any
from dataclasses import dataclass


@dataclass
class InjectionResult:
    """Result of an injection attempt."""
    success: bool
    process_id: Optional[int] = None
    message: str = ""
    method: str = ""


class RuntimeInjector:
    """
    Handles runtime injection of Spy Agent into WPF applications.
    
    Usage:
        injector = RuntimeInjector()
        
        # Option 1: Launch app with Spy Agent auto-injected
        result = injector.launch_with_hook(
            app_path="C:\\path\\to\\app.exe",
            startup_hook_dll="C:\\path\\to\\WpfSpyAgent.StartupHook.dll"
        )
        
        # Option 2: Attach to running process
        result = injector.attach_to_process(process_id=1234)
        
        # Option 3: Connect to existing Spy Agent
        connected = injector.connect_to_agent(process_id=1234, pipe_name="WPFSpyAgentPipe")
    """
    
    DEFAULT_PIPE_NAME = "WPFSpyAgentPipe"
    CONNECTION_TIMEOUT = 5.0
    
    def __init__(self, startup_hook_path: Optional[str] = None):
        """
        Initialize the RuntimeInjector.
        
        Args:
            startup_hook_path: Path to WpfSpyAgent.StartupHook.dll.
                              If not provided, will look in common locations.
        """
        self.startup_hook_path = startup_hook_path or self._find_startup_hook()
        self.env_vars_added: Dict[str, str] = {}
    
    def _find_startup_hook(self) -> Optional[str]:
        """Find the StartupHook DLL in common locations."""
        # Common paths relative to this file
        base_paths = [
            Path(__file__).parent.parent,  # Repository root
            Path(__file__).parent.parent / "WpfSpyAgent.StartupHook" / "bin" / "Debug" / "net8.0-windows",
            Path(__file__).parent.parent / ".." / "WpfSpyAgent.StartupHook" / "bin" / "Debug" / "net8.0-windows",
        ]
        
        for base in base_paths:
            dll_path = base / "WpfSpyAgent.StartupHook.dll"
            if dll_path.exists():
                return str(dll_path.resolve())
        
        # Also check environment variable
        env_path = os.environ.get("WPFSPY_STARTUP_HOOK_DLL")
        if env_path and Path(env_path).exists():
            return env_path
        
        return None
    
    def launch_with_hook(
        self,
        app_path: str,
        arguments: Optional[str] = None,
        pipe_name: str = DEFAULT_PIPE_NAME,
        wait_for_agent: bool = True,
        timeout: float = 30.0,
        cwd: Optional[str] = None,
        env: Optional[Dict[str, str]] = None
    ) -> InjectionResult:
        """
        Launch an application with Spy Agent automatically injected via DOTNET_STARTUP_HOOKS.
        
        Args:
            app_path: Path to the application executable
            arguments: Command-line arguments (optional)
            pipe_name: Named pipe name for the Spy Agent
            wait_for_agent: Wait for the agent to be ready
            timeout: Maximum time to wait for agent
            cwd: Working directory
            env: Additional environment variables
            
        Returns:
            InjectionResult with success status and process ID
        """
        if not Path(app_path).exists():
            return InjectionResult(
                success=False,
                message=f"Application not found: {app_path}"
            )
        
        if not self.startup_hook_path:
            return InjectionResult(
                success=False,
                message="Startup hook DLL not found. Set WPFSPY_STARTUP_HOOK_DLL or build WpfSpyAgent.StartupHook"
            )
        
        # Build environment
        full_env = os.environ.copy()
        full_env["DOTNET_STARTUP_HOOKS"] = self.startup_hook_path
        full_env["WPFSPY_AGENT_ENABLED"] = "1"
        full_env["WPFSPY_PIPE_NAME"] = pipe_name
        self.env_vars_added = {
            "DOTNET_STARTUP_HOOKS": self.startup_hook_path,
            "WPFSPY_AGENT_ENABLED": "1",
            "WPFSPY_PIPE_NAME": pipe_name
        }
        
        # Add any additional env vars
        if env:
            full_env.update(env)
        
        try:
            # Start the process
            cmd = [app_path]
            if arguments:
                cmd.append(arguments)
            
            process = subprocess.Popen(
                cmd,
                env=full_env,
                cwd=cwd,
                stdout=subprocess.PIPE,
                stderr=subprocess.PIPE
            )
            
            if wait_for_agent:
                # Wait for agent to be ready
                if not self._wait_for_agent(pipe_name, timeout):
                    process.terminate()
                    return InjectionResult(
                        success=False,
                        message=f"Agent not ready within {timeout}s"
                    )
            
            return InjectionResult(
                success=True,
                process_id=process.pid,
                message=f"Launched with Spy Agent (PID: {process.pid})",
                method="startup_hook"
            )
            
        except Exception as e:
            return InjectionResult(
                success=False,
                message=f"Failed to launch: {str(e)}"
            )
    
    def attach_to_process(
        self,
        process_id: int,
        pipe_name: str = DEFAULT_PIPE_NAME,
        timeout: float = 5.0
    ) -> InjectionResult:
        """
        Attach to an already-running WPF process.
        
        This checks if Spy Agent is already running in the target process.
        True runtime injection (without restart) requires Windows Hook API
        and is handled separately.
        
        Args:
            process_id: PID of the target process
            pipe_name: Named pipe name for the Spy Agent
            timeout: Connection timeout
            
        Returns:
            InjectionResult with success status
        """
        # First, check if agent is already running
        if self.connect_to_agent(process_id, pipe_name, timeout):
            return InjectionResult(
                success=True,
                process_id=process_id,
                message="Connected to existing Spy Agent",
                method="existing_agent"
            )
        
        return InjectionResult(
            success=False,
            process_id=process_id,
            message="Spy Agent not found in target process. "
                   "Start the app with startup hook or add SpyAgentHost.Start() to the app.",
            method="attach"
        )
    
    def connect_to_agent(
        self,
        process_id: Optional[int],
        pipe_name: str = DEFAULT_PIPE_NAME,
        timeout: float = 5.0
    ) -> bool:
        """
        Connect to an existing Spy Agent via Named Pipe.
        
        Args:
            process_id: PID of the process (optional, for logging)
            pipe_name: Named pipe name
            timeout: Connection timeout
            
        Returns:
            True if connected successfully
        """
        try:
            # Try to connect to the pipe
            sock = socket.socket(socket.AF_UNIX, socket.SOCK_STREAM)
            sock.settimeout(timeout)
            
            # Windows Named Pipe path
            pipe_path = f"\\\\.\\pipe\\{pipe_name}"
            
            # On Unix, we need to use a different approach
            # For Windows, this would use Named Pipe client
            if sys.platform == "win32":
                # Use named pipe client
                from win32file import CreateFile, OPEN_EXISTING
                from win32pipe import CallNamedPipe
                
                try:
                    result = CallNamedPipe(
                        pipe_path,
                        b'{"command":"GetVersion"}',
                        1024,
                        1000  # timeout in ms
                    )
                    return result is not None
                except:
                    pass
            
            # Fallback: try to connect as a simple socket
            sock.close()
            return False
            
        except Exception:
            return False
    
    def _wait_for_agent(self, pipe_name: str, timeout: float) -> bool:
        """Wait for the Spy Agent to be ready."""
        start_time = time.time()
        
        while time.time() - start_time < timeout:
            if self.connect_to_agent(None, pipe_name, 1.0):
                return True
            time.sleep(0.5)
        
        return False
    
    def get_running_dotnet_processes(self) -> List[Dict[str, Any]]:
        """
        Get list of running .NET/WPF processes.
        
        Returns:
            List of process info dicts with 'pid', 'name', 'title'
        """
        processes = []
        
        if sys.platform == "win32":
            try:
                import psutil
                for proc in psutil.process_iter(['pid', 'name', 'windows_title']):
                    try:
                        info = proc.info
                        if info.get('windows_title'):
                            processes.append({
                                'pid': info['pid'],
                                'name': info['name'],
                                'title': info['windows_title']
                            })
                    except (psutil.NoSuchProcess, psutil.AccessDenied):
                        pass
            except ImportError:
                # Fallback using subprocess
                try:
                    output = subprocess.check_output(
                        ['tasklist', '/FI', 'WINDOWTITLE ne 0', '/FO', 'CSV', '/NH'],
                        text=True
                    )
                    for line in output.strip().split('\n'):
                        if line:
                            parts = line.split('","')
                            if len(parts) >= 3:
                                processes.append({
                                    'pid': int(parts[1].replace('"', '')),
                                    'name': parts[0].replace('"', ''),
                                    'title': parts[2].replace('"', '')
                                })
                except:
                    pass
        
        return processes
    
    def is_agent_running(self, pipe_name: str = DEFAULT_PIPE_NAME) -> bool:
        """Check if Spy Agent is running (pipe exists)."""
        if sys.platform == "win32":
            pipe_path = f"\\\\.\\pipe\\{pipe_name}"
            try:
                from win32file import CreateFile, GENERIC_READ, OPEN_EXISTING
                handle = CreateFile(pipe_path, GENERIC_READ, 0, None, OPEN_EXISTING, 0, None)
                from win32file import CloseHandle
                CloseHandle(handle)
                return True
            except:
                return False
        return False


class RobotLauncher:
    """
    High-level application launcher with Spy Agent support.
    
    Usage:
        launcher = RobotLauncher()
        
        # Launch with automatic injection
        process = launcher.launch("C:\\path\\to\\app.exe")
        
        # Or use context manager
        with launcher.launch("C:\\path\\to\\app.exe") as process:
            # Run tests
            pass
    """
    
    def __init__(self, startup_hook_path: Optional[str] = None):
        self.injector = RuntimeInjector(startup_hook_path)
        self.process: Optional[subprocess.Popen] = None
    
    def launch(
        self,
        app_path: str,
        arguments: Optional[str] = None,
        pipe_name: str = RuntimeInjector.DEFAULT_PIPE_NAME,
        wait: bool = True,
        timeout: float = 30.0,
        cwd: Optional[str] = None,
        env: Optional[Dict[str, str]] = None
    ) -> subprocess.Popen:
        """
        Launch application with Spy Agent.
        
        Args:
            app_path: Path to executable
            arguments: Command-line arguments
            pipe_name: Spy Agent pipe name
            wait: Wait for agent to be ready
            timeout: Timeout for agent readiness
            cwd: Working directory
            env: Additional environment variables
            
        Returns:
            subprocess.Popen process object
            
        Raises:
            RuntimeError: If launch fails
        """
        result = self.injector.launch_with_hook(
            app_path=app_path,
            arguments=arguments,
            pipe_name=pipe_name,
            wait_for_agent=wait,
            timeout=timeout,
            cwd=cwd,
            env=env
        )
        
        if not result.success:
            raise RuntimeError(f"Failed to launch app: {result.message}")
        
        # Re-create process with same environment
        full_env = os.environ.copy()
        full_env.update(self.injector.env_vars_added)
        if env:
            full_env.update(env)
        
        cmd = [app_path]
        if arguments:
            cmd.append(arguments)
        
        self.process = subprocess.Popen(
            cmd,
            env=full_env,
            cwd=cwd,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE
        )
        
        return self.process
    
    def __enter__(self) -> subprocess.Popen:
        if not self.process:
            raise RuntimeError("Call launch() first")
        return self.process
    
    def __exit__(self, exc_type, exc_val, exc_tb):
        if self.process:
            self.process.terminate()
            self.process.wait(timeout=5)
            self.process = None


def launch_app(
    app_path: str,
    arguments: Optional[str] = None,
    startup_hook_path: Optional[str] = None,
    pipe_name: str = RuntimeInjector.DEFAULT_PIPE_NAME
) -> Tuple[subprocess.Popen, RuntimeInjector]:
    """
    Convenience function to launch an app with Spy Agent.
    
    Args:
        app_path: Path to executable
        arguments: Command-line arguments
        startup_hook_path: Path to StartupHook DLL
        pipe_name: Spy Agent pipe name
        
    Returns:
        Tuple of (process, injector)
    """
    injector = RuntimeInjector(startup_hook_path)
    result = injector.launch_with_hook(app_path, arguments, pipe_name)
    
    if not result.success:
        raise RuntimeError(f"Failed to launch: {result.message}")
    
    # Return process with same env
    full_env = os.environ.copy()
    full_env.update(injector.env_vars_added)
    
    cmd = [app_path]
    if arguments:
        cmd.append(arguments)
    
    process = subprocess.Popen(cmd, env=full_env)
    return process, injector


# Example usage when running as script
if __name__ == "__main__":
    import argparse
    
    parser = argparse.ArgumentParser(description="Launch WPF app with Spy Agent")
    parser.add_argument("app", help="Path to application executable")
    parser.add_argument("--args", help="Command-line arguments")
    parser.add_argument("--pipe", default="WPFSpyAgentPipe", help="Pipe name")
    parser.add_argument("--hook", help="Path to StartupHook DLL")
    
    args = parser.parse_args()
    
    launcher = RobotLauncher(args.hook)
    process = launcher.launch(args.app, args.args, args.pipe)
    
    print(f"Launched PID {process.pid}")
    print("Press Ctrl+C to terminate...")
    
    try:
        process.wait()
    except KeyboardInterrupt:
        process.terminate()
