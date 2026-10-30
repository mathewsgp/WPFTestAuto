# Test Categorization — WPFTestAuto

Tests are organized into three independent categories. Each category can be run separately.

---

## Category 1 — Framework Unit Tests (No App / No UI)

**Location:** `Tests/Framework/` + `Tests/contract/`

Verifies TestAutoLayer framework internals without launching any WPF application or interacting with any UI. Uses pure Python, mocks, and monkey-patching.

**No dependency on:** `WpfTestIde.exe`, `SampleWpfApp.exe`, or any running process.

```powershell
python -m pytest Tests/Framework/ Tests/contract/ -v
```

**92 tests pass** (3 pre-existing SikuliDriver parameter-name failures unrelated).

---

## Category 2 — SampleWpfApp Automation Tests (Framework + App)

**Location:** `Tests/SampleWpfApp/`

Uses the TestAutoLayer framework to automate `SampleWpfApp.exe`. Loads `DriverAgnosticApi.py` as a Robot Framework library.

**Dependency on:** `SampleWpfApp.exe` (built at `Tests/bin/SampleWpfApp/Debug/net9.0-windows/SampleWpfApp.exe`).

```powershell
robot --outputdir output Tests/SampleWpfApp/
```

---

## Category 3 — IDE Integration Tests (Independent of Framework)

**Location:** `Tests/Integration/IDE/`

Verifies WPFTestIDE features end-to-end. Two sub-layers:

### 3a — Standalone pywinauto tests (truly independent, zero TestAutoLayer imports)
```powershell
python -m pytest Tests/Integration/IDE/test_ide_features.py -v
```

**9 tests pass.** Cannot be broken by framework changes.

### 3b — Robot IDE tests (uses TestAutoLayer as library)
```powershell
robot --outputdir output Tests/Integration/IDE/*.robot
```

**32/32 UC tests pass.** Depends on framework loading correctly.

---

## Recommended CI Pipeline Order

1. **Category 1** (fast, hermetic) — `pytest Tests/Framework/ Tests/contract/`
2. **Category 3a** (standalone IDE, no framework dep) — `pytest Tests/Integration/IDE/test_ide_features.py`
3. **Category 2** (framework + SampleWpfApp) — `robot Tests/SampleWpfApp/`
4. **Category 3b** (Robot IDE, needs working framework) — `robot Tests/Integration/IDE/*.robot`

If Category 1 fails, stop — don't waste time on Categories 2/3.
