# Category 2 — SampleWpfApp Automation Tests (Framework + App)

These tests use the **TestAutoLayer framework** to automate the **SampleWpfApp** application.

They load `TestAutoLayer/api/DriverAgnosticApi.py` as a Robot Framework library and call its keywords (`Launch Application`, `Click Element`, `Set Element Value`, etc.) to drive `SampleWpfApp.exe`.

**Dependency on:** `SampleWpfApp.exe` (built at `Tests/bin/SampleWpfApp/Debug/net9.0-windows/SampleWpfApp.exe`), TestAutoLayer framework.

## What's tested

| Test file | What it verifies |
|-----------|-----------------|
| `order_tests.robot` | Create orders, verify grid content via OCR, reject invalid login |
| `runtime_injection_test.robot` | Library discovery, startup hook path, DLL existence, inject/attach workflow |
| `test_multi_app_clipboard.robot` | Multi-app: SampleWpfApp + Notepad clipboard flow |
| `ide_generated_test.robot` | Recorded session playback (IDE-generated script) |
| `self_healing_locators_demo.robot` | Toggle priority checkbox, OCR verification, multi-strategy fallback |

## How to run

```powershell
# From repo root (SampleWpfApp.exe must be built first)
robot --outputdir output Tests/SampleWpfApp/
```

Or run individual suites:
```powershell
robot --outputdir output Tests/SampleWpfApp/order_tests.robot
```

## CI guidance

Requires `SampleWpfApp.exe` to be built (`.NET 9` or `.NET Framework 4.6.1`). These tests are slower than Category 1 — they spawn real processes and interact with real UI.
