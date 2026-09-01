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
import shutil
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
    staged_dlls: Optional[List[str]] = None


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

    def _find_framework_hook(self) -> Optional[str]:
        """Find the FrameworkHook DLL in common locations (for .NET Framework AUTs)."""
        base_paths = [
            Path(__file__).parent.parent,
            Path(__file__).parent.parent / "WpfSpyAgent.FrameworkHook" / "bin" / "Debug" / "net461",
            Path(__file__).parent.parent / ".." / "WpfSpyAgent.FrameworkHook" / "bin" / "Debug" / "net461",
        ]
        for base in base_paths:
            dll_path = base / "WpfSpyAgent.FrameworkHook.dll"
            if dll_path.exists():
                return str(dll_path.resolve())
        env_path = os.environ.get("WPFSPY_FRAMEWORK_HOOK_DLL")
        if env_path and Path(env_path).exists():
            return env_path
        return None

    def _detect_target_framework(self, app_path: str) -> str:
        """Detect whether the target app is .NET / .NET Core (modern) or .NET Framework.

        Used as a fallback when no live PID is available (i.e. we are about to
        launch a new process). For an already-running process, prefer
        `_detect_target_framework_by_pid` which inspects loaded modules.
        """
        try:
            with open(app_path, "rb") as f:
                head = f.read(4096)
            # PE signature sanity check
            if head[:2] != b"MZ":
                return "framework"
            # Modern .NET apps are typically published as framework-dependent
            # (hostfxr + apphost) with a host config file (.runtimeconfig.json)
            # alongside the exe. .NET Framework apps do not have this file.
            app_dir = Path(app_path).parent
            if (app_dir / f"{Path(app_path).stem}.runtimeconfig.json").exists():
                return "modern"
            # .NET single-file publishes also lack runtimeconfig.json, but
            # contain the .NET marker signature; treat as modern.
            if b"coreclr" in head.lower() or b"hostfxr" in head.lower():
                return "modern"
            return "framework"
        except OSError:
            return "framework"

    def _detect_target_framework_by_pid(self, pid: int) -> str:
        """Inspect a running process's loaded modules to detect its CLR.

        Returns "modern" if coreclr.dll is loaded, "framework" if mscoree.dll
        / clr.dll / mscoreei.dll is loaded, "modern" otherwise (fallback).
        """
        if sys.platform != "win32":
            return "modern"
        try:
            import psutil  # type: ignore
        except ImportError:
            psutil = None
        try:
            has_coreclr = False
            has_fw_clr = False
            if psutil is not None:
                proc = psutil.Process(pid)
                for m in proc.memory_maps():
                    path = (m.path or "").lower()
                    if not path:
                        continue
                    base = path.rsplit("\\", 1)[-1]
                    if base == "coreclr.dll":
                        has_coreclr = True
                    elif base in ("clr.dll", "mscoree.dll", "mscoreei.dll"):
                        has_fw_clr = True
            else:
                # Fallback: use EnumProcessModules via ctypes if psutil is absent.
                import ctypes
                from ctypes import wintypes
                psapi = ctypes.WinDLL("psapi.dll")
                kernel32 = ctypes.WinDLL("kernel32.dll")
                PROCESS_QUERY_INFORMATION = 0x0400
                PROCESS_VM_READ = 0x0010
                hproc = kernel32.OpenProcess(
                    PROCESS_QUERY_INFORMATION | PROCESS_VM_READ, False, pid)
                if not hproc:
                    return "modern"
                try:
                    buf = (wintypes.HMODULE * 1024)()
                    needed = wintypes.DWORD()
                    if psapi.EnumProcessModules(
                            hproc, buf, ctypes.sizeof(buf), ctypes.byref(needed)):
                        count = needed.value // ctypes.sizeof(wintypes.HMODULE)
                        for i in range(count):
                            name = ctypes.create_unicode_buffer(512)
                            n = psapi.GetModuleBaseNameW(
                                hproc, buf[i], name, ctypes.sizeof(name))
                            if not n:
                                continue
                            base = name.value.lower()
                            if base == "coreclr.dll":
                                has_coreclr = True
                            elif base in ("clr.dll", "mscoree.dll", "mscoreei.dll"):
                                has_fw_clr = True
                finally:
                    kernel32.CloseHandle(hproc)
            if has_fw_clr and not has_coreclr:
                return "framework"
            return "modern"
        except Exception:
            return "modern"

    def _find_framework_agent_dir(self) -> Optional[Path]:
        """Find the directory containing the .NET Framework 4.x build of the Spy Agent.

        Returns the directory itself (not a child path) so the caller can
        decide whether to copy the contents flat or under a net461\ subfolder.
        """
        candidates = [
            Path(__file__).parent.parent / "bin" / "Debug" / "net461",
            Path(__file__).parent.parent / "bin" / "Release" / "net461",
            Path(__file__).parent.parent / ".." / "WpfSpyAgent" / "bin" / "Debug" / "net461",
            Path(__file__).parent.parent / ".." / "WpfSpyAgent" / "bin" / "Release" / "net461",
            Path(__file__).parent.parent / "WpfSpyAgent" / "bin" / "Debug" / "net461",
            Path(__file__).parent.parent / "WpfSpyAgent" / "bin" / "Release" / "net461",
            Path(__file__).parent.parent / ".." / "WpfSpyAgent.FrameworkHook" / "bin" / "Debug" / "net461",
            Path(__file__).parent.parent / ".." / "WpfSpyAgent.FrameworkHook" / "bin" / "Release" / "net461",
        ]
        for c in candidates:
            if c.is_dir() and (c / "WpfSpyAgent.dll").exists():
                return c
        return None

    def _stage_dll_set(
        self,
        aut_root: Path,
        target_dir: Path,
        src_dir: Path,
        dll_names: List[str],
    ) -> List[str]:
        """Copy a set of DLLs from src_dir to target_dir using timestamp comparison.

        Copies only when source is missing, or strictly newer than the staged copy.
        Returns the list of relative paths (relative to `aut_root`) actually
        copied/updated, so callers can unstage by passing the returned list back
        to `unstage_dlls`. Bare filenames are used when target_dir equals aut_root;
        `net461/file.dll` style paths are used when target_dir is a subfolder.
        """
        copied: List[str] = []
        for name in dll_names:
            src = src_dir / name
            if not src.exists():
                continue
            dst = target_dir / name
            try:
                if dst.exists():
                    src_mtime = src.stat().st_mtime
                    dst_mtime = dst.stat().st_mtime
                    if dst_mtime >= src_mtime:
                        continue
                shutil.copy2(src, dst)
                # Record the path relative to the AUT root, using forward slashes
                # for cross-platform stability (Windows accepts both).
                try:
                    rel = dst.relative_to(aut_root).as_posix()
                except ValueError:
                    rel = name
                copied.append(rel)
            except OSError:
                # Best-effort: don't abort the launch just because one DLL failed.
                pass
        return copied

    def stage_dlls(self, app_path: str, target_pid: Optional[int] = None) -> List[str]:
        """Stage Spy Agent DLLs next to the target application.

        TFM-aware: only the build matching the target's runtime is copied.

          - Modern pair goes to <target_dir>\ root
              * WpfSpyAgent.dll
              * WpfSpyAgent.StartupHook.dll
          - Framework trio goes to <target_dir>\net461\
              * WpfSpyAgent.dll       (Framework build)
              * WpfSpyAgent.FrameworkHook.dll
              * Newtonsoft.Json.dll    (Framework-only dependency)

        Detection priority:
          1. If `target_pid` is given, inspect the process's loaded modules
             for coreclr.dll vs mscoree.dll / clr.dll.
          2. Otherwise fall back to PE-header detection (look for a sidecar
             runtimeconfig.json or coreclr/hostfxr markers).

        Idempotent: subsequent calls with unchanged sources are no-ops.
        Returns the list of relative paths (relative to the AUT folder) that
        were copied/updated. Unstage with `unstage_dlls(app_path, returned_list)`.
        """
        target_dir = Path(app_path).parent
        target_dir.mkdir(parents=True, exist_ok=True)
        copied: List[str] = []

        if target_pid is not None:
            framework = self._detect_target_framework_by_pid(target_pid)
        else:
            framework = self._detect_target_framework(app_path)

        if framework == "framework":
            # ---- .NET Framework 4.x build -> AUT\net461\ ----
            fw_dir = self._find_framework_agent_dir()
            if fw_dir is None:
                return []
            net461_target = target_dir / "net461"
            net461_target.mkdir(parents=True, exist_ok=True)
            copied.extend(self._stage_dll_set(
                aut_root=target_dir, target_dir=net461_target, src_dir=fw_dir,
                dll_names=["WpfSpyAgent.dll", "WpfSpyAgent.FrameworkHook.dll", "Newtonsoft.Json.dll"],
            ))
        else:
            # ---- Modern build -> AUT root ----
            if not self.startup_hook_path:
                return []
            src_dir = Path(self.startup_hook_path).parent
            copied.extend(self._stage_dll_set(
                aut_root=target_dir, target_dir=target_dir, src_dir=src_dir,
                dll_names=["WpfSpyAgent.dll", "WpfSpyAgent.StartupHook.dll"],
            ))

        return copied

    def unstage_dlls(self, app_path: str, dll_names: List[str]) -> None:
        """Remove previously staged DLLs from the target app directory.

        Only removes the names that were originally staged by `stage_dlls` —
        caller passes the list returned from `stage_dlls`. We do NOT remove
        files we did not stage (defensive: avoid clobbering user files).
        Also removes the `net461\` subfolder if it is empty after cleanup.
        """
        target_dir = Path(app_path).parent
        for name in dll_names:
            dst = target_dir / name
            try:
                if dst.exists():
                    dst.unlink()
            except OSError:
                pass
        # Best-effort: drop the net461 subfolder if it ended up empty.
        try:
            sub = target_dir / "net461"
            if sub.is_dir() and not any(sub.iterdir()):
                sub.rmdir()
        except OSError:
            pass

    def launch_with_hook(
        self,
        app_path: str,
        arguments: Optional[str] = None,
        pipe_name: str = DEFAULT_PIPE_NAME,
        wait_for_agent: bool = True,
        timeout: float = 30.0,
        cwd: Optional[str] = None,
        env: Optional[Dict[str, str]] = None,
        stage_dlls: bool = False,
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
            stage_dlls: When True, copy the Spy Agent managed DLLs into the
                        target app's folder (timestamp-aware: only updates
                        when source is newer). Required when the AUT lives in
                        a different path from the framework's bin folder.
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

        # Optionally stage DLLs into the target app's folder. Tracks which
        # files were copied so the caller can clean up on teardown.
        staged: List[str] = []
        if stage_dlls:
            try:
                staged = self.stage_dlls(app_path)
            except Exception as e:
                return InjectionResult(
                    success=False,
                    message=f"DLL staging failed: {e}"
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
                method="startup_hook",
                staged_dlls=staged,
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


class ProcessLauncher:
    """
    High-level application launcher with Spy Agent support.

    Usage:
        launcher = ProcessLauncher()

        # Launch with automatic injection
        process = launcher.launch("C:\\path\\to\\app.exe")

        # Or use context manager
        with launcher.launch("C:\\path\\to\\app.exe", stage_dlls=True) as process:
            # Run tests
            pass
    """

    def __init__(self, startup_hook_path: Optional[str] = None):
        self.injector = RuntimeInjector(startup_hook_path)
        self.process: Optional[subprocess.Popen] = None
        self.staged_dlls: List[str] = []
        self._launched_app_path: Optional[str] = None

    def launch(
        self,
        app_path: str,
        arguments: Optional[str] = None,
        pipe_name: str = RuntimeInjector.DEFAULT_PIPE_NAME,
        wait: bool = True,
        timeout: float = 30.0,
        cwd: Optional[str] = None,
        env: Optional[Dict[str, str]] = None,
        stage_dlls: bool = False,
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
            stage_dlls: Copy Spy Agent DLLs into the AUT folder (timestamp-aware)

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
            env=env,
            stage_dlls=stage_dlls,
        )

        if not result.success:
            raise RuntimeError(f"Failed to launch app: {result.message}")

        # Track what we staged so __exit__ can clean up.
        self.staged_dlls = result.staged_dlls or []
        self._launched_app_path = app_path

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
        try:
            if self.process:
                self.process.terminate()
                try:
                    self.process.wait(timeout=5)
                except subprocess.TimeoutExpired:
                    self.process.kill()
                self.process = None
        finally:
            # Always clean up any DLLs we staged into the AUT folder.
            if self._launched_app_path and self.staged_dlls:
                try:
                    self.injector.unstage_dlls(self._launched_app_path, self.staged_dlls)
                finally:
                    self.staged_dlls = []
                    self._launched_app_path = None


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
