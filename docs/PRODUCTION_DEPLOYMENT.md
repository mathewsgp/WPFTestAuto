# Production Deployment — Swapping the Mock for Real Drivers

Everything in `tests/`, `modules/`, `api/`, and `repository/` is
production code as-is. Only **Layer 5** (and the small Layer 4 bodies
that call into it) needs to change to point this framework at a real WPF
application. Layer 4's method signatures (the "API parity" contract) do
**not** change — that's the point.

## What to replace

| File | Replace with |
|---|---|
| `drivers/mock_wpf_app/` | Nothing — delete/ignore. This only exists for the mock. |
| `drivers_rf/flaui_robotframework/FlaUILibrary.py` body | Real FlaUI calls (via pythonnet or a .NET Remote Library server) |
| `drivers_rf/wpfspy_robotframework/WPFSpyLibrary.py` | Already real — see "WPFSpy — already implemented for real" below |
| `drivers_rf/sikuli_robotframework/SikuliLibrary.py` body | Real SikuliX (Java) integration or `robotframework-SikuliLibrary` |

## Option A — pythonnet bridge (FlaUI)

Runs FlaUI's .NET assemblies directly inside the same Python process:

```python
import clr
clr.AddReference("FlaUI.Core")
clr.AddReference("FlaUI.UIA3")
from FlaUI.UIA3 import UIA3Automation
from FlaUI.Core import Application

class FlaUIDriver:
    def __init__(self):
        self.automation = UIA3Automation()
        self.app = Application.Attach("YourWpfApp.exe")

    def find_element(self, locator: dict):
        window = self.app.GetMainWindow(self.automation)
        return window.FindFirstDescendant(
            cf => cf.ByAutomationId(locator["value"])
        )
    # invoke / set_value / get_text / is_visible / toggle follow the
    # same signatures already defined in this repo's FlaUIDriver.
```

Requires Windows + .NET runtime on the test-execution machine, plus the
`pythonnet` package (see `requirements.txt`).

## Option B — Robot Framework Remote Library (FlaUI)

A small .NET console app hosts FlaUI
and exposes the same keyword methods over XML-RPC using
`robotframework-remoteserver`'s .NET-equivalent, or Robot Framework's
built-in `Remote` library pointed at that server's URL. This is often
preferred when the test runner itself is not Windows (e.g. a Linux CI
agent orchestrating a Windows test-execution VM).

```robotframework
*** Settings ***
Library    Remote    http://windows-test-vm:8270/
```

The Python-side wrapper classes in this repo become thin proxies whose
method bodies just forward to the Remote library — signatures unchanged.

## WPFSpy — already implemented for real

Unlike FlaUI/Sikuli below, WPFSpy does **not** need a production
swap — `WpfSpyAgent/` and `WPFSpyRealDriver` are a real, working
implementation already, proven end-to-end against `SampleWpfApp/` (which
is deliberately built with **no reference to `WpfSpyAgent` at all** —
see `docs/INJECTION_OPTIONS.md`). To point it at your own WPF
application, you have the same two choices documented there:

**If you own the app's source** (Option 2 in `docs/INJECTION_OPTIONS.md`):
1. Add a `ProjectReference` to `WpfSpyAgent.csproj` from your app's `.csproj`.
2. Call `WpfSpyAgent.SpyAgentHost.Start()` from your app's startup path,
   gated behind a test-mode flag.

**If you don't want to touch the app's source at all** (Options 1a/1b in
`docs/INJECTION_OPTIONS.md`): use `WpfSpyAgent.StartupHook` (modern .NET,
via `DOTNET_STARTUP_HOOKS`) or `WpfSpyAgent.FrameworkHook` (.NET
Framework, via a custom `AppDomainManager`) — no changes to your app at
all, just environment variables at launch. This is the path
`SampleWpfApp` itself is verified against.

Either way:
3. Make sure any custom-rendered controls you need WPFSpy to reach
   implement `ISpyInteractable` (see
   `SampleWpfApp/CustomControls/PriorityToggleControl.cs`).
4. Update `repository/elements/*.yaml` with your real controls' `Name`
   values for the `WPFSpy` strategy.

See `docs/WPFSPY_MODULE.md`, `docs/PROTOCOL.md`, and
`docs/INJECTION_OPTIONS.md` for the full design, wire protocol, and
injection mechanisms.

## WPFSpy Spy Agent injection — design note

`docs/INJECTION_OPTIONS.md` covers this in full, including how to load
the agent into a completely unmodified target process at launch (no
source change, no rebuild) via mechanisms Microsoft itself documents for
exactly this purpose (`DOTNET_STARTUP_HOOKS`, custom `AppDomainManager`).
True *live* attach to an *already-running* process (no relaunch at all —
what Snoop's UI does) is a further step up in complexity (message-hook-based
loading or CLR Profiler Attach, both requiring native/COM code) and is
intentionally not built out in this reference implementation — see that
doc's "Option 3" for the honest tradeoffs, and ask if you need it built.

## Sikuli

Real image-based matching needs: (1) actual reference screenshots saved
under an `images/` folder (referenced by `imagePath` in
`repository/elements/*.yaml`, replacing the semantic tags used by the
mock), and (2) either the SikuliX Java jar + `robotframework-SikuliLibrary`,
or an OpenCV-based Python template-matching implementation exposing the
same `find_element`/`invoke`/etc. signatures.

## Recorder

`recorder/recorder_engine.py`'s `_scripted_interactions()` stands in for
a live UI Automation event subscriber. FlaUI exposes
`Automation.RegisterEventHandler` (and related APIs) for exactly this —
subscribe to invoke/value-changed/focus events on the WPF window, and
write the same three JSON shapes (`recorded_elements.json`,
`recorded_steps.json`, `recorded_sequence.json`) this repo's recorder
already produces. Everything downstream (Converter, draft script
generation) needs no changes.

## Checklist before going live

- [ ] Layer 4 wrapper bodies replaced (FlaUI, WPFSpy, Sikuli as needed)
- [ ] Real reference images captured for any Sikuli-strategy elements
- [ ] Spy Agent injection wired into the real WPF app's test-mode startup
- [ ] `repository/elements/*.yaml` reviewed — mock-only entries
      (`OrdersPage.PriorityCheckbox`'s deliberately-broken FlaUI value)
      replaced with real, working locators
- [ ] Full suite green against the real application:
      `./run_tests.sh`
