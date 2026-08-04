# SampleWpfApp

A small, **deliberately unmodified** WPF application (Login → Orders),
used as the injection target for `docs/INJECTION_OPTIONS.md`'s demos.
It has **zero reference — compile-time or run-time — to `WpfSpyAgent`
or either injection-loader project.** Nothing in this app's source or
`.csproj` needs to change to get the Spy Agent running inside it; that's
the entire point.

Multi-targeted (`net6.0-windows` and `net48`) so it can stand in as the
target for **either** runtime's injection mechanism from the exact same
source.

## What's in it

- `MainWindow.xaml(.cs)` — Login screen (`txtUsername`, `txtPassword`,
  `btnSubmit`, `lblError`), all with `AutomationProperties.AutomationId`
  set, so FlaUI can find them.
- `Views/OrdersWindow.xaml(.cs)` — Orders screen (`cmbSku`, `txtQty`,
  `btnCreateOrder`, `lblConfirmation`, `gridOrders`).
- `CustomControls/PriorityToggleControl.cs` — a **deliberately
  non-standard, owner-drawn control** (`chkPriority`), no AutomationPeer.
  This is what the self-healing locator demo falls back to WPFSpy for —
  see `docs/SELF_HEALING_LOCATORS.md`.
- `App.xaml.cs` — plain `Application` subclass, no overrides, no hooks.

## Build

```powershell
cd SampleWpfApp

# Modern .NET build
dotnet build -f net6.0-windows

# .NET Framework build
dotnet build -f net48
```

## Run it plain (no agent at all)

```powershell
dotnet run -f net6.0-windows
# or
dotnet build -f net48 -c Debug
bin\Debug\net48\SampleWpfApp.exe
```

Log in with `user1` / `Pass@123` to reach the Orders screen where
`chkPriority` lives. Nothing here talks to any agent — this is the app
completely on its own.

## Getting the Spy Agent running inside it (no source modification)

Pick the section matching which build you're running.

### Modern .NET (`net6.0-windows`) — `DOTNET_STARTUP_HOOKS`

```powershell
# One-time: build the loader (also builds WpfSpyAgent as its dependency)
cd ..\WpfSpyAgent.StartupHook
dotnet build

# Run SampleWpfApp with the agent injected via the startup hook
cd ..\SampleWpfApp
$env:DOTNET_STARTUP_HOOKS = "$(Resolve-Path ..\WpfSpyAgent.StartupHook\bin\Debug\net6.0-windows\WpfSpyAgent.StartupHook.dll)"
$env:WPFSPY_AGENT_ENABLED = "1"
dotnet run -f net6.0-windows
```

### .NET Framework (`net48`) — custom `AppDomainManager`

```powershell
# One-time: build the loader (also builds WpfSpyAgent's net48 output)
cd ..\WpfSpyAgent.FrameworkHook
dotnet build

# Copy the loader + its WpfSpyAgent dependency next to SampleWpfApp.exe
# (required so the CLR's normal assembly probing can find them — see
# docs/INJECTION_OPTIONS.md)
cd ..\SampleWpfApp
dotnet build -f net48
Copy-Item ..\WpfSpyAgent.FrameworkHook\bin\Debug\net48\WpfSpyAgent.FrameworkHook.dll bin\Debug\net48\
Copy-Item ..\WpfSpyAgent.FrameworkHook\bin\Debug\net48\WpfSpyAgent.dll bin\Debug\net48\

$env:COMPLUS_AppDomainManagerAssembly = "WpfSpyAgent.FrameworkHook, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null"
$env:COMPLUS_AppDomainManagerType = "WpfSpyAgent.FrameworkHook.SpyAppDomainManager"
$env:WPFSPY_AGENT_ENABLED = "1"
bin\Debug\net48\SampleWpfApp.exe
```

If the environment-variable form doesn't take effect on your exact
Framework patch level, use the `.exe.config` alternative documented in
`docs/INJECTION_OPTIONS.md` instead.

### Confirming the agent is actually running

Either way, once running: the Spy Agent is listening on the
`WPFSpyAgentPipe` Named Pipe. From the Python framework's directory,
with `WPFSPY_MODE=real`:

```powershell
$env:WPFSPY_MODE = "real"
pip install pywin32
python -m robot tests\self_healing_locators_demo.robot
```

`chkPriority` should resolve via WPFSpy exactly as it does in mock mode —
see `docs/PROTOCOL.md` and `docs/SELF_HEALING_LOCATORS.md`.

### Driving it from WpfTestIde

`WpfTestIde`'s "Attach to Process..." doesn't care how the agent got
into the target — start `SampleWpfApp` via either method above, then
attach and record as described in `docs/IDE_GUIDE.md`.
