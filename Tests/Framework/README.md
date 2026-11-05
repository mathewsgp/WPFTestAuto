# Category 1 — Framework Unit Tests (No App / No UI)

These tests verify **TestAutoLayer framework internals** without launching any WPF application or interacting with any UI.

They use:
- Pure Python logic
- Mock objects for drivers and repositories
- Temp directories for file I/O (DLL staging)
- Monkey-patching instead of real process spawning

**No dependency on:** `WpfTestIde.exe`, `SampleWpfApp.exe`, `WPFSpy` pipe, or any running process.

## What's tested

| Test file | What it verifies |
|-----------|-----------------|
| `test_circuit_breaker.py` | Circuit breaker open/half-open/closed states |
| `test_healing_metadata_store.py` | Healing metadata CRUD and history |
| `test_strategy_executor_healing.py` | StrategyExecutor fallback + healing recording |
| `test_dll_staging.py` | DLL timestamp-aware staging (temp files only) |
| `test_sikuli_schema.py` | Sikuli YAML schema validation |
| `test_sikuli_matcher.py` | Sikuli image matching logic |
| `test_sikuli_e2e_stubs.py` | Sikuli stub-based E2E logic |
| `legacy_compat_test.py` | Backward compatibility shims |
| `multi_app_tests.py` | MultiAppContext registration (no real apps) |
| `test_launch_terminate_application.py` | Launch/terminate keyword logic (monkey-patched) |
| `test_resolution_fallback.py` | Strategy resolution + fallback + healing (mocks) |

## Contract tests

| Test file | What it verifies |
|-----------|-----------------|
| `test_driver_contract.py` | All 3 drivers implement `BaseDriver` with matching signatures |

## How to run

```powershell
# From repo root
python -m pytest Tests/Framework/ Tests/contract/ -v
```

## CI guidance

These tests should run on every commit — they are fast, hermetic, and require no WPF runtime.
