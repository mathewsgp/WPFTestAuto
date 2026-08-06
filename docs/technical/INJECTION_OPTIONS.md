# Getting the Spy Agent into an App You Didn't Build — Your Options

`SampleWpfApp/` in this repo is deliberately built with **zero
reference — compile-time or run-time — to `WpfSpyAgent` or either
injection-loader project below.** It's written exactly as if it were a
genuine third-party WPF app someone else shipped.

If you want the agent inside a WPF app whose source you can't or don't
want to touch — a third-party app, a build you don't control, or just
"any WPF app, Snoop-style" — here is an honest map of what's actually
possible, what this repo implements, and what it deliberately doesn't.

---

## Quick Reference: Which option to use?

| Your situation | Use | Status |
|---|---|---|
| App already running, has Spy Agent | **Attach to Process** | ✅ Ready |
| App already running, no Spy Agent | Use Snoop or restart with startup hook | Available |
| You own the app's source | Cooperative hosting (Option 2) | ✅ Ready |
| You can relaunch, modern .NET | Startup Hook (Option 1a) | ✅ Ready |
| You can relaunch, .NET Framework | AppDomainManager (Option 1b) | ✅ Ready |

---

## Option R — Attach to Already-Running Process (Implemented)

The WpfTestIde provides **Attach to Process** functionality that supports:

### R1. Connecting to Already-Injected Agent

If the Spy Agent is **already running** inside the target process (via
Options 1a, 1b, or 2), the IDE can connect directly via Named Pipe:

1. Open WpfTestIde
2. Click **Attach to Process...**
3. Select the running process
4. Click **Attach**

The IDE will connect to the existing Spy Agent pipe.

### R2. Runtime Injection (WpfTestIde)

The IDE attempts to inject the Spy Agent into a running process using
Windows Hook API (`SetWindowsHookEx`):

```csharp
// See: WpfTestIde/Helpers/RuntimeInjector.cs
await RuntimeInjector.InjectAsync(processId, nativeDllPath, pipeName);
```

**Features:**
- Checks if Spy Agent is already running
- Attempts Windows Hook injection
- Provides fallback suggestions if injection fails

### R3. Launch with Startup Hook

The IDE can **launch a new process** with Spy Agent automatically injected:

1. Select **Launch New Process** mode
2. Browse to the application
3. Optionally add arguments
4. Click **Attach**

The IDE sets `DOTNET_STARTUP_HOOKS` and `WPFSPY_AGENT_ENABLED` environment
variables before launching.

---

## Option 1 — Implemented: zero-source-modification loaders

Both of these require the target to be **(re)launched** with a specific
environment variable set.

### 1a. Modern .NET (Core 3.0+ / .NET 5/6/7/8) — `DOTNET_STARTUP_HOOKS`

A first-class, Microsoft-documented extensibility point. Set
`DOTNET_STARTUP_HOOKS` to the path of a DLL containing a `public static
class StartupHook { public static void Initialize() { ... } }`.

**Implemented:** `WpfSpyAgent.StartupHook/`

```powershell
# Build the startup hook
cd WpfSpyAgent.StartupHook
dotnet build

# Launch your app with the hook
cd ..\SampleWpfApp
$env:DOTNET_STARTUP_HOOKS = "$(Resolve-Path ..\WpfSpyAgent.StartupHook\bin\Debug\net6.0-windows\WpfSpyAgent.StartupHook.dll)"
$env:WPFSPY_AGENT_ENABLED = "1"
dotnet run -f net6.0-windows
```

### 1b. .NET Framework — custom `AppDomainManager`

.NET Framework uses a custom `AppDomainManager` for injection.

**Implemented:** `WpfSpyAgent.FrameworkHook/`

**Activation method A — environment variables:**

```powershell
cd WpfSpyAgent.FrameworkHook
dotnet build

cd ..\SampleWpfApp
dotnet build -f net48
Copy-Item ..\WpfSpyAgent.FrameworkHook\bin\Debug\net48\WpfSpyAgent.FrameworkHook.dll bin\Debug\net48\
Copy-Item ..\WpfSpyAgent.FrameworkHook\bin\Debug\net48\WpfSpyAgent.dll bin\Debug\net48\

$env:COMPLUS_AppDomainManagerAssembly = "WpfSpyAgent.FrameworkHook, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null"
$env:COMPLUS_AppDomainManagerType = "WpfSpyAgent.FrameworkHook.SpyAppDomainManager"
$env:WPFSPY_AGENT_ENABLED = "1"
bin\Debug\net48\SampleWpfApp.exe
```

**Activation method B — the target's `.exe.config` file:**

```xml
<!-- SomeThirdPartyFrameworkWpfApp.exe.config -->
<configuration>
  <runtime>
    <appDomainManagerAssembly value="WpfSpyAgent.FrameworkHook, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null" />
    <appDomainManagerType value="WpfSpyAgent.FrameworkHook.SpyAppDomainManager" />
  </runtime>
</configuration>
```

---

## Option 2 — Cooperative in-process hosting

If you own the app's source, add a `ProjectReference` to `WpfSpyAgent`:

```csharp
protected override void OnStartup(StartupEventArgs e)
{
    base.OnStartup(e);
    if (Environment.GetEnvironmentVariable("WPFSPY_AGENT_ENABLED") == "1")
    {
        WpfSpyAgent.SpyAgentHost.Start();
    }
}
```

---

## Option 3 — True "attach to an already-running process"

Two Microsoft-sanctioned mechanisms exist:

### 3a. Message-hook–based loading (`SetWindowsHookEx`)

This is how [Snoop](https://github.com/snoopwpf/snoopwpf) works. Uses
`SetWindowsHookEx` with `WH_GETMESSAGE` to map your hook DLL into the
target process. This is a first-class Win32 API used by decades of
legitimate debugging tools.

### 3b. CLR Profiler Attach

`ICLRProfiling::AttachProfiler` for .NET Framework, or
`Microsoft.Diagnostics.NETCore.Client`'s `AttachProfiler` for .NET Core 3.0+.
This is what real APM/profiler products use.

---

## What this repo does NOT include

A generic "inject arbitrary code into an arbitrary already-running
process" utility — the classic `CreateRemoteThread` +
`WriteProcessMemory` + `LoadLibrary` pattern — is not provided. That
technique is flagged by every EDR/AV product as a malware building block
(MITRE ATT&CK T1055).

---

## Which runtime is my target app on?

Check whether it ships a `.runtimeconfig.json` next to the `.exe`
(modern .NET → use Option 1a) or an `.exe.config` + no
`.runtimeconfig.json` (.NET Framework → use Option 1b).
