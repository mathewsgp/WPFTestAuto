# Debugging Python API Code from the IDE

When you click **Run** in the IDE, the generated script is executed as a
separate Python subprocess (`PythonRunner` in
`WPFTestIDE/Execution/PythonRunner.cs`). Breakpoints set inside Visual
Studio will **not** hit Python code, because the IDE is a .NET process
and the automation logic runs in Python. To debug the Python API code
(`TestAutoLayer/api/*.py`, `drivers_rf/okulix_robotframework/*.py`, etc.)
use `debugpy` and attach from VS Code.

## Prerequisites

Install `debugpy` in the Python environment the IDE uses:

```powershell
pip install debugpy
```

## Step 1: Add a wait-for-debugger hook in the Python code

Open the Python module you want to debug (for example
`TestAutoLayer/api/driver_agnostic_api.py`) and add the following near the
top of the function or module entry point:

```python
import os

if os.environ.get("OCULIX_DEBUG_WAIT") == "1":
    import debugpy
    debugpy.listen(("127.0.0.1", 5678))
    debugpy.wait_for_client()   # blocks here until a debugger attaches
```

Place additional `breakpoint()` calls wherever you want to pause:

```python
def evaluate_script_condition(...):
    breakpoint()   # second breakpoint after attach
    ...
```

## Step 2: Tell the IDE to pass the environment variable

So the subprocess starts in wait mode, add the env var to
`PythonRunner.RunAsync` (`WPFTestIDE/Execution/PythonRunner.cs`):

```csharp
startInfo.Environment["OCULIX_DEBUG_WAIT"] = "1";
```

Alternatively, set it globally before launching the IDE:

```powershell
$env:OCULIX_DEBUG_WAIT = "1"
```

## Step 3: Configure VS Code

Create (or extend) `.vscode/launch.json` in the workspace root:

```json
{
    "version": "0.2.0",
    "configurations": [
        {
            "name": "Attach to IDE Python subprocess",
            "type": "debugpy",
            "request": "attach",
            "connect": {
                "host": "127.0.0.1",
                "port": 5678
            },
            "pathMappings": [
                {
                    "localRoot": "${workspaceFolder}/TestAutoLayer",
                    "remoteRoot": "${workspaceFolder}/TestAutoLayer"
                }
            ]
        }
    ]
}
```

## Step 4: Run and attach

1. Open the Python files you want to debug in VS Code and set breakpoints
   (red dots in the gutter).
2. Start the WPF Test IDE and click **Run** to execute a script.
3. The Python subprocess launches and blocks at
   `debugpy.wait_for_client()`. The IDE output pane will show the process
   is running but paused.
4. In VS Code open the **Run and Debug** panel, select
   **Attach to IDE Python subprocess**, and press **Start Debugging** (F5).
5. VS Code attaches and your breakpoints become active. Step through,
   inspect locals, evaluate expressions, etc.
6. Detach or stop when done; the subprocess continues or exits normally.

## Option: Skip the wait (attach-on-demand)

If you don't want every run to block, remove `debugpy.wait_for_client()`
and just keep `debugpy.listen(("127.0.0.1", 5678))`. Then attach from VS
Code whenever you want; breakpoints will trigger on the next hit without
blocking the subprocess at startup.

## Known limitations

- **stdin/stdout:** The IDE captures subprocess stdout to its results
  pane. `print()` statements from the Python code appear there, but
  interactive input (e.g. `pdb`) is not supported — that's why `debugpy`
  attach is the recommended approach over `pdb.set_trace()`.
- **Transient script files:** The IDE writes the generated script to a
  temp file (`ide_run_<guid>.py`) and deletes it after the run. If you
  need to inspect the exact file that ran, copy it from the IDE output
  log before it is removed, or run the same Python manually with
  `PYTHONPATH` pointing at `TestAutoLayer`.

## Related

- `WPFTestIDE/Execution/PythonRunner.cs` — subprocess launch + env vars
- `WPFTestIDE/Execution/ScriptExecutionService.cs` — orchestration of
  Python vs Robot execution
- `docs/guides/IDE_GUIDE.md` — IDE authoring and run workflow
