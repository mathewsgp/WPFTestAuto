"""
AppLauncher - Robot Framework Library for WPF Application Launching
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
from typing import Optional

# Add parent directory to path
_THIS_DIR = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, _THIS_DIR)

from runtime_injector import RuntimeInjector


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
    
    def __init__(self, startup_hook_path: Optional[str] = None):
        """Initialize the launcher."""
        self._injector = RuntimeInjector(startup_hook_path)
        self._processes = {}
    
    def launch_application(self, app_path, arguments=None, pipe_name="WPFSpyAgentPipe", 
                          timeout=30.0, cwd=None):
        """Launch an application with Spy Agent injected."""
        if not Path(app_path).exists():
            raise FileNotFoundError(f"Application not found: {app_path}")
        
        # Find startup hook
        if not self._injector.startup_hook_path:
            hook_path = os.environ.get("WPFSPY_STARTUP_HOOK_DLL")
            if hook_path:
                self._injector.startup_hook_path = hook_path
        
        if not self._injector.startup_hook_path:
            raise RuntimeError(
                "Startup hook DLL not found. Set WPFSPY_STARTUP_HOOK_DLL "
                "or build WpfSpyAgent.StartupHook project."
            )
        
        # Build environment
        full_env = os.environ.copy()
        full_env["DOTNET_STARTUP_HOOKS"] = self._injector.startup_hook_path
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
        
        time.sleep(1)
        self._processes[process.pid] = {
            'process': process,
            'pipe_name': pipe_name,
            'app_path': app_path
        }
        
        return process.pid
    
    def attach_to_application(self, process_id, pipe_name="WPFSpyAgentPipe", timeout=5.0):
        """Attach to an already-running application."""
        result = self._injector.attach_to_process(process_id, pipe_name, timeout)
        return result.success
    
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
        return self._injector.is_agent_running(pipe_name)
    
    def get_startup_hook_path(self):
        """Get the path to the startup hook DLL."""
        return self._injector.startup_hook_path or "None"
