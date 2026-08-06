"""
RobotLauncher - Robot Framework Library for WPF Application Launching
===================================================================
Library for launching WPF applications with Spy Agent injection.

Usage:
    *** Settings ***
    Library    ../api/robot_launcher.py

    *** Test Cases ***
    Launch App
        ${pid}=    Launch Application    C:\\path\\to\\app.exe
        Terminate Application    ${pid}
"""

import os
import sys
import subprocess
import time
from pathlib import Path


class AppLauncher:
    """
    Robot Framework library for launching WPF applications with Spy Agent.
    
    Provides keywords:
    - Launch Application
    - Attach To Application
    - Terminate Application
    - Terminate All Applications
    - Get Process Id
    - Is Agent Ready
    - Get Startup Hook Path
    """
    
    ROBOT_LIBRARY_SCOPE = "GLOBAL"
    
    def __init__(self):
        """Initialize the launcher."""
        self._startup_hook_path = self._find_startup_hook()
        self._processes = {}
    
    def _find_startup_hook(self):
        """Find the StartupHook DLL in common locations."""
        base_paths = [
            Path(__file__).parent.parent,
            Path(__file__).parent.parent / "WpfSpyAgent.StartupHook" / "bin" / "Debug" / "net6.0-windows",
            Path(__file__).parent.parent / "WpfSpyAgent.StartupHook" / "bin" / "Debug" / "net8.0-windows",
        ]
        
        for base in base_paths:
            dll_path = base / "WpfSpyAgent.StartupHook.dll"
            if dll_path.exists():
                return str(dll_path.resolve())
        
        env_path = os.environ.get("WPFSPY_STARTUP_HOOK_DLL")
        if env_path and Path(env_path).exists():
            return env_path
        
        return None
    
    def launch_application(self, app_path, arguments=None, pipe_name="WPFSpyAgentPipe", timeout=30.0, cwd=None):
        """Launch an application with Spy Agent injected."""
        if not Path(app_path).exists():
            raise FileNotFoundError(f"Application not found: {app_path}")
        
        hook_path = self._startup_hook_path
        if not hook_path:
            hook_path = os.environ.get("WPFSPY_STARTUP_HOOK_DLL")
        
        if not hook_path:
            raise RuntimeError(
                "Startup hook DLL not found. Set WPFSPY_STARTUP_HOOK_DLL "
                "or build WpfSpyAgent.StartupHook project."
            )
        
        full_env = os.environ.copy()
        full_env["DOTNET_STARTUP_HOOKS"] = hook_path
        full_env["WPFSPY_AGENT_ENABLED"] = "1"
        full_env["WPFSPY_PIPE_NAME"] = pipe_name
        
        cmd = [app_path]
        if arguments:
            cmd.append(str(arguments))
        
        process = subprocess.Popen(
            cmd,
            env=full_env,
            cwd=cwd,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE
        )
        
        time.sleep(1)
        self._processes[process.pid] = {
            'process': process,
            'pipe_name': pipe_name,
            'app_path': app_path
        }
        
        return process.pid
    
    def attach_to_application(self, process_id, pipe_name="WPFSpyAgentPipe", timeout=5.0):
        """Attach to an already-running application."""
        return self._check_agent_running(process_id, pipe_name)
    
    def _check_agent_running(self, process_id, pipe_name):
        """Check if Spy Agent is running in the process."""
        return False  # Would need win32 API call
    
    def terminate_application(self, process_id):
        """Terminate a launched application."""
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
        
        try:
            if sys.platform == "win32":
                subprocess.run(['taskkill', '/F', '/PID', str(process_id)], check=True)
                return True
            else:
                subprocess.run(['kill', str(process_id)], check=True)
                return True
        except:
            return False
    
    def terminate_all_applications(self):
        """Terminate all applications launched by this library."""
        count = 0
        pids = list(self._processes.keys())
        for pid in pids:
            if self.terminate_application(pid):
                count += 1
        return count
    
    def get_process_id(self, app_path=None):
        """Get PID of a running application."""
        for pid, info in self._processes.items():
            if app_path is None or info['app_path'] == app_path:
                if info['process'].poll() is None:
                    return pid
        return None
    
    def is_agent_ready(self, pipe_name="WPFSpyAgentPipe"):
        """Check if Spy Agent is ready on the named pipe."""
        if sys.platform != "win32":
            return False
        
        try:
            import win32file
            pipe_path = f"\\\\.\\pipe\\{pipe_name}"
            handle = win32file.CreateFile(
                pipe_path,
                win32file.GENERIC_READ,
                0, None,
                win32file.OPEN_EXISTING,
                0, None,
            )
            win32file.CloseHandle(handle)
            return True
        except:
            return False
    
    def get_startup_hook_path(self):
        """Get the path to the startup hook DLL."""
        return self._startup_hook_path or "None"
