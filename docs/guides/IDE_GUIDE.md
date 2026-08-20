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
| **Import Elements** | Import element entries from existing `repository/elements/*.yaml` files |
| **Export Steps / Import Steps** | Save recorded steps to YAML or load them back into the IDE |
| **Reset Layout** | Restore the IDE layout to its default state via **View → Reset Layout** |

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
├── Helpers/RuntimeInjector.cs    ← Runtime injection support
└── RelayCommand.cs
```

## Build & run (on Windows, .NET 9 SDK + WPF workload)

```powershell
cd WpfTestIde
dotnet restore
dotnet build
dotnet run
```

Or use the bundled script from the repo root:

```bat
build_and_run_vs2022.bat
```

## Attaching to Processes

The IDE supports three ways to connect to a WPF application:

### Option 1: Runtime Attach (Already Running)

If the Spy Agent is **already running** inside your application:

1. **Attach to Process...** → select your app from the list
2. Click **Attach**

The IDE will connect via Named Pipe to the existing Spy Agent.

### Option 2: Launch with Spy Agent

The IDE can **launch a new process** with Spy Agent automatically injected:

1. **Attach to Process...** → select **Launch New Process** mode
2. Browse to your application executable
3. Click **Attach**

The IDE sets `DOTNET_STARTUP_HOOKS` and `WPFSPY_AGENT_ENABLED` environment
variables before launching.

### Option 3: Manual Startup Hook

Start your app with the Spy Agent injected via the startup-hook loader:

```powershell
# Build the loader
cd WpfSpyAgent.StartupHook
dotnet build

# Launch your app with the hook
cd ..\SampleWpfApp
$env:DOTNET_STARTUP_HOOKS = "$(Resolve-Path ..\WpfSpyAgent.StartupHook\bin\Debug\net8.0-windows\WpfSpyAgent.StartupHook.dll)"
$env:WPFSPY_AGENT_ENABLED = "1"
dotnet run -f net8.0-windows

# Then attach from the IDE
```

See [Injection Options](../technical/INJECTION_OPTIONS.md) for more details.

## Using the IDE

1. **Attach to Process...** → select your app (page-name mapping is pre-configured)
2. Click **● Record** to start recording
3. Interact with your application (type, click, select)
4. Click **■ Stop Recording**
5. Review **Recorded Steps** and **Element Repository**
6. Add verifications: Click **+ verify after** on any step
7. Click **▶ Run Script** to execute

**Load Sample** populates the IDE with a worked example without needing a live app.

## Known simplifications (documented, not hidden)

- **One Spy Agent per recording session.** The page-name map is
  configured once at Attach time (window-title substring → page alias);
  it doesn't auto-discover new page names as new windows open. Add a row
  per screen your app navigates to before recording.
- **Element resolution during recording** uses the same FlaUI-first,
  WPFSpy-fallback logic as test execution. Custom-rendered controls that
  lack AutomationPeers are resolved via the in-process WPFSpy agent's
  live visual-tree hit-testing.
- **No undo/redo** on the Recorded Steps list — delete and re-record
  instead.
- **Layout persistence** is best-effort. If `%AppData%\WpfTestIde\layout.json`
  is corrupt, the IDE falls back to the XAML default layout on next launch.
  Use **View → Reset Layout** to force defaults.
- This project could not be compiled or run in the sandbox used to write
  it (no Windows/.NET SDK available there) — see the repo's top-level
  README for the same caveat that applies to `SampleWpfApp` and
  `WpfSpyAgent`. Build on a real Windows machine to verify.
