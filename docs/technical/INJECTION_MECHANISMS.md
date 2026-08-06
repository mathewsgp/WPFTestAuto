# WPF Test Automation - Injection Mechanisms Summary

This document summarizes all mechanisms available for injecting the Spy Agent into WPF applications.

---

## Overview

The **Spy Agent** (`WpfSpyAgent`) provides element inspection and interaction capabilities. It must be running inside the target WPF application for the test framework to work.

| Mechanism | Requires Restart? | Owner Only? | Status |
|-----------|------------------|-------------|--------|
| Cooperative Hosting | Yes (source change) | Yes | ✅ Ready |
| DOTNET_STARTUP_HOOKS | Yes | No | ✅ Ready |
| AppDomainManager | Yes | No | ✅ Ready |
| Runtime Injection (IDE) | No | No | ✅ Ready |
| SetWindowsHookEx | No | No | Planned |

---

## Mechanism 1: Cooperative Hosting (Source Required)

**Best for:** Apps you own and can modify.

### How it works
Add a `ProjectReference` to `WpfSpyAgent` and call `SpyAgentHost.Start()` from your app's startup.

### Code change required

```csharp
// App.xaml.cs
protected override void OnStartup(StartupEventArgs e)
{
    base.OnStartup(e);
    if (Environment.GetEnvironmentVariable("WPFSPY_AGENT_ENABLED") == "1")
    {
        WpfSpyAgent.SpyAgentHost.Start();
    }
}
```

### Pros
- Clean, no external dependencies
- Works reliably
- Can pass custom pipe name

### Cons
- Requires source code modification
- Need to rebuild the application

### Files
- `WpfSpyAgent/SpyAgentHost.cs` - Main host class

---

## Mechanism 2: DOTNET_STARTUP_HOOKS (.NET Core/5+/6+)

**Best for:** Modern .NET apps you don't own but can restart.

### How it works
Use Microsoft's official `DOTNET_STARTUP_HOOKS` extensibility point. The runtime loads and executes a specified assembly before `Main()`.

### Setup

```powershell
# Build the startup hook
cd WpfSpyAgent.StartupHook
dotnet build

# Launch target app with hook
$env:DOTNET_STARTUP_HOOKS = "C:\path\to\WpfSpyAgent.StartupHook.dll"
$env:WPFSPY_AGENT_ENABLED = "1"
& "C:\path\to\TargetApp.exe"
```

### Implementation

```csharp
// WpfSpyAgent.StartupHook/StartupHook.cs
using System;

public static class StartupHook
{
    public static void Initialize()
    {
        if (Environment.GetEnvironmentVariable("WPFSPY_AGENT_ENABLED") == "1")
        {
            // Load WpfSpyAgent assembly and call SpyAgentHost.Start()
            var asm = typeof(StartupHook).Assembly;
            var hostType = asm.GetType("WpfSpyAgent.SpyAgentHost");
            var startMethod = hostType?.GetMethod("Start");
            startMethod?.Invoke(null, null);
        }
    }
}
```

### Pros
- Microsoft-documented extensibility point
- No source code changes needed
- Works with any .NET Core/5+/6+ app
- Same mechanism used by APM vendors

### Cons
- Requires app restart
- Must set environment variables

### Files
- `WpfSpyAgent.StartupHook/StartupHook.cs`
- `WpfSpyAgent.StartupHook/WpfSpyAgent.StartupHook.csproj`

---

## Mechanism 3: AppDomainManager (.NET Framework)

**Best for:** Legacy .NET Framework apps you don't own but can restart.

### How it works
.NET Framework uses a custom `AppDomainManager` as its extensibility point. The CLR instantiates it before the app's `Main()`.

### Setup - Method A (Environment Variables)

```powershell
# Build the framework hook
cd WpfSpyAgent.FrameworkHook
dotnet build

# Copy DLLs to target app directory
Copy-Item WpfSpyAgent.FrameworkHook.dll TargetAppDir\
Copy-Item WpfSpyAgent.dll TargetAppDir\

# Launch with custom AppDomainManager
$env:COMPLUS_AppDomainManagerAssembly = "WpfSpyAgent.FrameworkHook"
$env:COMPLUS_AppDomainManagerType = "WpfSpyAgent.FrameworkHook.SpyAppDomainManager"
$env:WPFSPY_AGENT_ENABLED = "1"
.\TargetApp.exe
```

### Setup - Method B (.exe.config file)

```xml
<!-- TargetApp.exe.config -->
<configuration>
  <runtime>
    <appDomainManagerAssembly value="WpfSpyAgent.FrameworkHook, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null" />
    <appDomainManagerType value="WpfSpyAgent.FrameworkHook.SpyAppDomainManager" />
  </runtime>
</configuration>
```

### Implementation

```csharp
// WpfSpyAgent.FrameworkHook/SpyAppDomainManager.cs
public class SpyAppDomainManager : AppDomainManager
{
    public override void InitializeNewDomain(AppDomainSetup appDomainInfo)
    {
        if (Environment.GetEnvironmentVariable("WPFSPY_AGENT_ENABLED") == "1")
        {
            WpfSpyAgent.SpyAgentHost.Start();
        }
    }
}
```

### Pros
- Works with .NET Framework apps
- Config file approach doesn't require env vars

### Cons
- Requires app restart
- .NET Framework only
- DLLs must be in target app's directory (for Method A)

### Files
- `WpfSpyAgent.FrameworkHook/SpyAppDomainManager.cs`
- `WpfSpyAgent.FrameworkHook/WpfSpyAgent.FrameworkHook.csproj`

---

## Mechanism 4: Runtime Injection (WpfTestIde)

**Best for:** Attaching to already-running processes from the IDE.

### How it works
The IDE provides UI to:
1. Connect to existing Spy Agent via Named Pipe
2. Attempt Windows Hook injection
3. Launch new process with startup hook

### IDE Features

```
Attach to Process
├── Runtime Attach (Already Running)
│   └── Connect via Named Pipe
├── Launch New Process
│   └── Auto-configure env vars
└── Runtime Injection (experimental)
    └── SetWindowsHookEx injection
```

### Implementation

```csharp
// WpfTestIde/Helpers/RuntimeInjector.cs
public static class RuntimeInjector
{
    [DllImport("user32.dll")]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    public static async Task<bool> InjectAsync(int targetProcessId, string dllPath, string pipeName)
    {
        // 1. Get target thread ID
        var process = Process.GetProcessById(targetProcessId);
        uint threadId = (uint)process.Threads[0].Id;

        // 2. Set Windows Hook
        _hookHandle = SetWindowsHookEx(WH_GETMESSAGE, hookProc, dllHandle, threadId);

        // 3. Wait for injection
        return _hookHandle != IntPtr.Zero;
    }
}
```

### Pros
- Attach to already-running processes
- No restart required (if injection succeeds)
- IDE integration

### Cons
- Requires admin privileges for some methods
- May be blocked by antivirus/EDR
- SetWindowsHookEx is complex

### Files
- `WpfTestIde/Helpers/RuntimeInjector.cs`
- `WpfTestIde/Dialogs/AttachToProcessDialog.xaml`

---

## Mechanism 5: SetWindowsHookEx (Snoop-style) - Future

**Best for:** Truly non-invasive attachment to running processes.

### How it works
This is how [Snoop](https://github.com/snoopwpf/snoopwpf) works. Uses `SetWindowsHookEx` to inject a DLL into the target process.

```csharp
[DllImport("user32.dll")]
private static extern IntPtr SetWindowsHookEx(
    int idHook,           // WH_GETMESSAGE or WH_CALLWNDPROC
    HookProc lpfn,       // Hook procedure
    IntPtr hMod,          // DLL module handle
    uint dwThreadId       // Target thread ID
);
```

### Why it's complex
1. Requires a native DLL (not pure .NET)
2. The hook DLL must be loaded into the target process
3. The DLL needs to use .NET hosting APIs to load WpfSpyAgent
4. May trigger antivirus false positives

### Status
Not implemented - would require a native C++ DLL or NativeAOT component.

---

## Comparison Matrix

| Criteria | Cooperative | StartupHook | AppDomainMgr | Runtime (IDE) | WindowsHook |
|---------|-------------|-------------|--------------|---------------|-------------|
| Restart Required | Yes | Yes | Yes | No | No |
| Source Required | Yes | No | No | No | No |
| Admin Required | No | No | No | Sometimes | Yes |
| Reliability | ★★★★★ | ★★★★★ | ★★★★☆ | ★★★☆☆ | ★★☆☆☆ |
| Complexity | Low | Low | Medium | High | Very High |

---

## Recommended Workflow

### For apps you own:
1. Add `SpyAgentHost.Start()` call to your app
2. Build and run with `WPFSPY_AGENT_ENABLED=1`

### For third-party apps:
1. Try **Runtime Attach** in the IDE first
2. If that fails, restart the app with **Startup Hook** (modern .NET) or **AppDomainManager** (.NET Framework)

### For CI/CD:
1. Package `WpfSpyAgent.StartupHook.dll` with your tests
2. Set environment variables in your test runner
3. Launch the target app automatically

---

## Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                        Target WPF Application                    │
│                                                                  │
│   ┌─────────────────────────────────────────────────────────┐   │
│   │  SpyAgentHost (Named Pipe Server)                       │   │
│   │  - Listens on WPFSpyAgentPipe                          │   │
│   │  - Dispatches commands on UI thread                     │   │
│   │  - Exposes: FindElement, GetProperty, SetProperty, etc │   │
│   └─────────────────────────────────────────────────────────┘   │
│                            ▲                                     │
│                            │ Started by                          │
│   ┌───────────────────────┼───────────────────────────────┐   │
│   │  Injection Point       │  Mechanism                     │   │
│   ├───────────────────────┼───────────────────────────────┤   │
│   │  OnStartup()           │  Cooperative Hosting           │   │
│   │  StartupHook.Initialize│  DOTNET_STARTUP_HOOKS         │   │
│   │  InitializeNewDomain()│  AppDomainManager             │   │
│   │  (Running process)     │  SetWindowsHookEx             │   │
│   └───────────────────────┴───────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────┘
                              │
                              │ Named Pipe
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                     Test Runner / IDE                             │
│                                                                  │
│   ┌─────────────────────────────────────────────────────────┐   │
│   │  SpyAgentClient / RuntimeInjector                        │   │
│   │  - Connects to pipe                                     │   │
│   │  - Sends JSON commands                                  │   │
│   │  - Receives JSON responses                             │   │
│   └─────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────┘
```

---

## Named Pipe Protocol

Commands are JSON over Named Pipes:

```json
// Find element
{"command":"FindElement","xpath":"//Button[@AutomationId='btnSubmit']"}

// Get property
{"command":"GetProperty","automationId":"btnSubmit","property":"Name"}

// Click element
{"command":"Click","automationId":"btnSubmit"}

// Get visual tree
{"command":"GetVisualTree","maxDepth":10}
```

See: `WpfSpyAgent/Protocol/Messages.cs`

---

*Document Version: 1.0*
*Last Updated: 2024*
