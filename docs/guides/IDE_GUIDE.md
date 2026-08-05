# WpfTestIde — Script Development IDE

A real WPF desktop application that implements the full authoring loop as a GUI tool: **attach → record → auto-generate Element Repository + script → add verifications → run**.

## What it does

| Feature | How |
|---|---|
| **Attach** | Pick a running process by window title; configure the Spy Agent's pipe name and a window-title → page-alias map |
| **Record** | A global mouse hook detects clicks on the attached app; each click is resolved to an element via the **same FlaUI-first, WPFSpy-fallback logic used at test-execution time** |
| **Auto-generate Element Repository** | Every newly-seen element becomes a `repository/elements/*.yaml`-shaped entry, live, in the "Element Repository" tab |
| **Auto-generate Script** | Every recorded step becomes a line in a Layer 1 `.robot` script, live, in the "Generated Script" tab |
| **Add verification steps** | Click "+ verify after" on any recorded step; pick an element, and the expected value is pre-filled from that element's **current on-screen value** |
| **Run scripts** | Shells out to `python -m robot`, streaming console output and a PASS/FAIL summary |
| **Spy Tool** | Inspect UI elements with visual tree, property grid, and XPath editor |
| **Checkpoint Wizard** | Point-and-click creation of verification points |
| **Visual Test Builder** | Drag-and-drop test creation with Robot code generation |
| **Element Tree View** | Hierarchical view of all elements with search and filtering |

## Recording uses the framework's own self-healing philosophy

Every click during recording is resolved by `Recording/ElementProbe.cs`:

1. Try **FlaUI** first (`UIA3Automation.FromPoint`) — if it finds a usable `AutomationId` at that screen point, use it.
2. If not — fall back to asking the attached app's **live in-process Spy Agent** directly, over the same Named Pipe protocol, which can hit-test *any* control, standard or custom-rendered.

See [Self-Healing Locators](../features/SELF_HEALING.md) for more details.

## Project layout

```
WpfTestIde/
├── WpfTestIde.csproj          References: FlaUI.Core, FlaUI.UIA3, YamlDotNet
├── App.xaml(.cs)               Registers value converters
├── MainWindow.xaml(.cs)        Toolbar + Element Tree + Steps + tabbed panels
├── ViewModels/
│   ├── MainViewModel.cs       Main application logic (MVVM)
│   ├── ElementTreeViewModel.cs Hierarchical element tree management
│   └── TestFlowViewModel.cs   Visual test builder logic
├── Models/
│   ├── RecordedStep.cs
│   └── ElementEntry.cs
├── Views/
│   ├── ElementTreeView.xaml   Hierarchical element tree panel
│   ├── ElementEditorView.xaml Element editing panel
│   └── TestFlowDialog.xaml   Visual test builder dialog
├── Dialogs/
│   ├── AttachToProcessDialog.xaml(.cs)
│   ├── SpyToolDialog.xaml(.cs)       Element inspection tool
│   ├── CheckpointWizardDialog.xaml(.cs)  Verification wizard
│   └── AddVerificationDialog.xaml(.cs)
├── Recording/
│   ├── GlobalMouseHook.cs      Win32 WH_MOUSE_LL — sees clicks in OTHER processes
│   ├── SpyAgentClient.cs       C# Named Pipe client
│   ├── ElementProbe.cs         FlaUI-first, WPFSpy-fallback element resolution
│   ├── RecordingSession.cs     Orchestrates the above into RecordedStep events
│   ├── ScriptGenerator.cs      RecordedStep list -> .robot text
│   └── RepositoryWriter.cs     ElementEntry list -> repository YAML
├── Execution/RobotRunner.cs    Shells out to `python -m robot`
├── Converters/Converters.cs
└── RelayCommand.cs
```

## Build & run (on Windows, .NET 6 SDK + WPF workload)

```powershell
cd WpfTestIde
dotnet restore
dotnet build
dotnet run
```

### Try it end-to-end against SampleWpfApp

`SampleWpfApp` has no built-in agent hook (see
`docs/INJECTION_OPTIONS.md`) — start it with the Spy Agent injected via
the startup-hook loader first, exactly as `SampleWpfApp/README.md`
describes:

```powershell
# Terminal 1 — build the loader once
cd WpfSpyAgent.StartupHook
dotnet build

# Terminal 1 (cont.) — run the UNMODIFIED target app with the agent injected
cd ..\SampleWpfApp
$env:DOTNET_STARTUP_HOOKS = "$(Resolve-Path ..\WpfSpyAgent.StartupHook\bin\Debug\net6.0-windows\WpfSpyAgent.StartupHook.dll)"
$env:WPFSPY_AGENT_ENABLED = "1"
dotnet run -f net6.0-windows

# Terminal 2 — the IDE
cd WpfTestIde
dotnet run
```

In the IDE:
1. **Attach to Process...** → select `SampleWpfApp` from the list (its
   default page-name mapping — "Login" → `LoginPage`, "Orders" →
   `OrdersPage` — already matches `SampleWpfApp`'s window titles).
2. Click **● Record**.
3. In the `SampleWpfApp` window: type `user1` / `Pass@123`, click
   **Login**, select a SKU, toggle **Priority**, click **Create Order**.
4. Click **■ Stop Recording**. Check the **Element Repository** and
   **Generated Script** tabs — the priority checkbox should be flagged
   non-standard.
5. Click a step's **+ verify after**, pick
   `OrdersPage.lblConfirmation`, confirm the pre-filled expected value.
6. Click **▶ Run Script** — this writes the generated script to
   `tests/ide_generated_test.robot` and runs it via `python -m robot`,
   streaming output into the **Run Results** tab.
7. **Export Repository (.yaml)** / **Export Script (.robot)** to save
   them into `repository/elements/` and `tests/` for real, following the
   same review/refactor steps as `docs/RECORDER_GUIDE.md`.

No live app? Click **Load Sample** to populate the Recorded Steps,
Element Repository, and Generated Script tabs with a worked example —
useful for exploring the UI, adding verifications, and running, without
needing `SampleWpfApp` running.

## Known simplifications (documented, not hidden)

- **One Spy Agent per recording session.** The page-name map is
  configured once at Attach time (window-title substring → page alias);
  it doesn't auto-discover new page names as new windows open. Add a row
  per screen your app navigates to before recording.
- **FlaUI's re-query on focus-lost** (`ElementProbe.GetCurrentValue`)
  uses the cached `AutomationElement` reference from the original probe.
  If the control was recreated (rare, but possible after certain layout
  changes) this could throw; the exception is caught and the last-known
  text is used instead.
- **No undo/redo** on the Recorded Steps list — delete and re-record
  instead.
- This project could not be compiled or run in the sandbox used to write
  it (no Windows/.NET SDK available there) — see the repo's top-level
  README for the same caveat that applies to `SampleWpfApp` and
  `WpfSpyAgent`. Build on a real Windows machine to verify.
