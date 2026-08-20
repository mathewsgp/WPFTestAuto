# Getting Started

## 1. Install

### Windows (recommended)

From the repo root, run the bundled setup script:

```bat
setup_env.bat
```

This creates `.venv`, activates it, upgrades `pip`, and installs all
required + optional packages (`robotframework`, `pyyaml`, `pytest`,
`robotframework-requests`, `pywin32`, `FlaUILibrary`,
`robotframework-SikuliLibrary`, `pytesseract`, `Pillow`).

### Manual / cross-platform

```bash
python3 -m venv .venv
source .venv/bin/activate   # Windows: .venv\Scripts\activate.bat
pip install -U pip
pip install robotframework pyyaml pytest robotframework-requests pywin32
pip install FlaUILibrary robotframework-SikuliLibrary pytesseract Pillow
```

Requires Python 3.9+.

## 2. Build the .NET side (Windows only)

The framework includes a WPF sample app, the WPFSpy agent, and the WPF
Test IDE. Build them with:

```bat
build_and_run_vs2022.bat
```

or open `WpfTestFramework.sln` in Visual Studio 2022/2026 and build.

## 3. Run the suite

```bash
./run_tests.sh
```

or directly:

```bash
python3 -m robot --outputdir output tests/
```

Windows PowerShell:

```powershell
.\run_tests.ps1
```

You should see:

```
Tests.Order Tests :: ...
Create And Confirm New Order                              | PASS |
Reject Invalid Login                                        | PASS |
Create Order Without Sku Shows Prompt                        | PASS |
Tests.Self Healing Locators Demo :: ...
Toggle Priority Checkbox Self Heals To WPFSpy                | PASS |

4 tests, 4 passed, 0 failed
```

Open `results/report.html` for the summary or `results/log.html` for the
full step-by-step execution log (including the self-healing fallback
messages printed to the console log).

## 3. Run a single test / tag

```bash
python3 -m robot --outputdir results -t "Reject Invalid Login" tests/
python3 -m robot --outputdir results -i self-healing tests/
```

## 4. Write your first test

Most new tests only need Layer 1 + existing Layer 2 keywords:

```robotframework
*** Settings ***
Library          ../api/DriverAgnosticApi.py
Resource         ../modules/login_module.robot
Resource         ../modules/order_module.robot
Resource         ../modules/order_verifications.robot
Test Setup       Reset Application

*** Test Cases ***
My New Test
    Login To Application    user1    Pass@123
    Create New Order    SKU-9999    5
    Verify Order Confirmation Displayed    SKU-9999    5
```

If the element/page you need doesn't exist yet in
`repository/elements/*.yaml`, add it there first (see
`docs/ELEMENT_REPOSITORY_GUIDE.md`) — you should almost never need to
touch `api/DriverAgnosticApi.py` or anything in `drivers_rf/`/`drivers/`
just to write a new test.

## 5. Try the recorder pipeline

```bash
python3 recorder/recorder_engine.py
python3 recorder/converter.py
```

This regenerates `recorder/example_draft_output/` — a draft Element
Repository, Step Repository, and Layer 1 test script generated purely
from a recorded interaction sequence. See `docs/RECORDER_GUIDE.md` for
the full workflow this demonstrates, including how to promote a draft
into the real suite.

## 6. Regression-check after any change

Any change to `api/`, `drivers_rf/`, or `drivers/` should be followed by
a full suite run — these layers are shared by every test:

```bash
./run_tests.sh
```
