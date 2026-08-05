# WPFSpy Automation Module

## What's implemented in this repository

Unlike FlaUI and Sikuli (still mocked — see `docs/PRODUCTION_DEPLOYMENT.md`),
WPFSpy has a **real, working implementation** end-to-end:

```
SampleWpfApp/ (real WPF app, Windows/.NET — genuinely unmodified,
               no reference to WpfSpyAgent at all)
        │
        │  WpfSpyAgent.StartupHook (modern .NET) or
        │  WpfSpyAgent.FrameworkHook (.NET Framework) loads
        │  WpfSpyAgent into this process at startup —
        │  see docs/INJECTION_OPTIONS.md
        ▼
WpfSpyAgent/ (real .NET class library, now running in-process)
  └─ SpyAgentHost.cs — Named Pipe server ("WPFSpyAgentPipe")
  └─ CommandDispatcher.cs + VisualTreeInspector.cs — walks the live
     visual tree directly (no UI Automation) and acts on controls
        ▲
        │  Named Pipe, line-delimited JSON — see docs/PROTOCOL.md
        │
drivers_rf/wpfspy_robotframework/WPFSpyLibrary.py (Python, Layer 4)
  └─ WPFSpyRealDriver — real Named Pipe client (pywin32), active when
     WPFSPY_MODE=real
  └─ WPFSpyMockDriver — cross-platform fallback, active by default
     (WPFSPY_MODE unset or "mock"), talks to drivers/mock_wpf_app/ instead
```

## The three design points

1. **In-process execution, via injection — not a modified target.**
   `SampleWpfApp` has zero reference to `WpfSpyAgent`, at compile time or
   run time. The agent gets into its process via one of the
   zero-source-modification loaders in `docs/INJECTION_OPTIONS.md`
   (`WpfSpyAgent.StartupHook` for modern .NET, `WpfSpyAgent.FrameworkHook`
   for .NET Framework). This gives it direct access to the live visual
   tree and to controls that don't expose a proper UI Automation peer at
   all, like `SampleWpfApp/CustomControls/PriorityToggleControl.cs` —
   and it works against a genuinely untouched target application, the
   same as it would against a real third-party app.
2. **IPC communication.** The out-of-process test runner (Python, via
   `WPFSpyRealDriver`) talks to the agent over a Named Pipe
   (`WPFSpyAgentPipe`), one JSON command per call, synchronously — see
   `docs/PROTOCOL.md` for the exact wire format. A gRPC alternative is
   documented (not wired in) at `WpfSpyAgent.Grpc/README.md`.
3. **API parity.** `WPFSpyRealDriver`'s method signatures
   (`find_element`/`invoke`/`set_value`/`get_text`/`is_visible`/`toggle`)
   are identical to `FlaUIDriver`'s and to `WPFSpyMockDriver`'s. Layer 3
   (`api/DriverAgnosticApi.py`) never knows or cares which one is active.

## Running the real path

See `SampleWpfApp/README.md` for full build/run steps (both the modern
.NET and .NET Framework variants). Summary, modern .NET:

```powershell
# Terminal 1 — build the loader, then run the UNMODIFIED app with it
cd WpfSpyAgent.StartupHook
dotnet build
cd ..\SampleWpfApp
$env:DOTNET_STARTUP_HOOKS = "$(Resolve-Path ..\WpfSpyAgent.StartupHook\bin\Debug\net6.0-windows\WpfSpyAgent.StartupHook.dll)"
$env:WPFSPY_AGENT_ENABLED = "1"
dotnet run -f net6.0-windows

# Terminal 2 — the test suite, talking to the real agent
$env:WPFSPY_MODE = "real"
pip install pywin32
python -m robot tests/self_healing_locators_demo.robot
```

This drives the **real** `chkPriority` custom control in the **real**,
**unmodified** running WPF app, over the **real** Named Pipe, through
the **real** in-process agent, loaded there entirely from the outside —
the mock is not involved at all in this path.

## Why WPFSpy doesn't use UI Automation internally

FlaUI already covers "anything UIA can see." WPFSpy's entire reason to
exist is reaching controls UIA *can't* reliably see — so
`VisualTreeInspector` acts on WPF controls directly (their real
properties: `TextBox.Text`, `ButtonBase.Click`, etc.) and on custom
controls via the opt-in `ISpyInteractable` interface, never via
`System.Windows.Automation`. See `WpfSpyAgent/VisualTreeInspector.cs`.

## Reliability payoff

Because `OrdersPage.PriorityCheckbox`'s `FlaUI` strategy in
`repository/elements/orders_page.yaml` is intentionally pointed at an
AutomationId the app doesn't expose (simulating a real custom control gap),
runtime self-healing (`docs/SELF_HEALING_LOCATORS.md`) automatically falls
back to WPFSpy for that one element — in both mock mode (proven by the
existing passing test suite) and real mode (proven by running against
`SampleWpfApp` as above) — without the test author needing to know in
advance which controls need which driver.
