# Getting the Spy Agent into an App You Didn't Build — Your Options

`SampleWpfApp/` in this repo is deliberately built with **zero
reference — compile-time or run-time — to `WpfSpyAgent` or either
injection-loader project below.** It's written exactly as if it were a
genuine third-party WPF app someone else shipped. That's on purpose: it
lets Options 1a and 1b below be demonstrated and verified against a
real, unmodified executable, not a cooperative stand-in.

If you want the agent inside a WPF app whose source you can't or don't
want to touch — a third-party app, a build you don't control, or just
"any WPF app, Snoop-style" — here is an honest map of what's actually
possible, what this repo implements, and what it deliberately doesn't.

## What this repo does NOT include, and why

A generic "inject arbitrary code into an arbitrary already-running
process" utility — the classic `CreateRemoteThread` +
`WriteProcessMemory` + `LoadLibrary` pattern — is not something we're
providing as reusable code here. That specific technique has no
first-class, OS-sanctioned purpose beyond injection; it's the same
primitive flagged by every EDR/AV product as a core malware building
block (MITRE ATT&CK T1055), and a generic implementation of it works
identically against *any* process, not just a WPF test target. The fact
that the use case here is benign doesn't change what the code itself is
capable of once it exists as a standalone tool. So that's a hard no,
independent of intent.

The good news: every legitimate route below gets you the same practical
outcome, using mechanisms Windows/.NET explicitly designed for exactly
this "load diagnostic/instrumentation code into an app I didn't build
hooks into" scenario.

## Option 1 — Implemented here: zero-source-modification loaders

Both of these require the target to be **(re)launched** with a specific
environment variable set — not source, not binaries, nothing about the
target changes. Which one applies depends entirely on which runtime the
target app is built on; **they are not interchangeable** — .NET
Framework and modern .NET (Core/5+) are different CLRs with different
extensibility points, so an assembly built for one cannot be loaded by
the other at all.

### 1a. Modern .NET (Core 3.0+ / .NET 5/6/7/8) — `DOTNET_STARTUP_HOOKS`

A first-class, Microsoft-documented extensibility point. Set
`DOTNET_STARTUP_HOOKS` to the path of a DLL containing a `public static
class StartupHook { public static void Initialize() { ... } }`, and the
runtime loads and calls it automatically before the target's own
`Main()`. This is the same mechanism real APM/observability vendors use
for "codeless" .NET instrumentation.

**Implemented in this repo:** `WpfSpyAgent.StartupHook/`, verified
against the unmodified `SampleWpfApp/` (built as `net6.0-windows`):

```powershell
cd WpfSpyAgent.StartupHook
dotnet build

cd ..\SampleWpfApp
$env:DOTNET_STARTUP_HOOKS = "$(Resolve-Path ..\WpfSpyAgent.StartupHook\bin\Debug\net6.0-windows\WpfSpyAgent.StartupHook.dll)"
$env:WPFSPY_AGENT_ENABLED = "1"
dotnet run -f net6.0-windows
```

The same two environment variables work against any other modern-.NET
WPF app — just point `DOTNET_STARTUP_HOOKS` at the built
`WpfSpyAgent.StartupHook.dll` and launch that app instead of
`SampleWpfApp`:

```powershell
$env:DOTNET_STARTUP_HOOKS = "C:\path\to\WpfSpyAgent.StartupHook.dll"
$env:WPFSPY_AGENT_ENABLED = "1"
& "C:\path\to\SomeThirdPartyWpfApp.exe"
```

### 1b. .NET Framework — custom `AppDomainManager`

.NET Framework has no startup-hooks equivalent by that name, but it has
an older, equally first-class extensibility point: the CLR will
instantiate a **custom `AppDomainManager`** for the default AppDomain if
told to, via one of two supported activation methods. `AppDomainManager`
is a real, documented `System` base class designed precisely for hosts
and diagnostic tooling to customize AppDomain creation — this is not a
memory-injection trick, it's a configuration knob the CLR itself reads
at startup.

**Implemented in this repo:** `WpfSpyAgent.FrameworkHook/`'s
`SpyAppDomainManager`, calling `SpyAgentHost.Start()` from
`InitializeNewDomain` exactly the way the .NET Core startup hook calls it
from `Initialize`. Verified against the unmodified `SampleWpfApp/` (built
as `net48`) using activation method A below.

Two supported activation methods exist. Either way, the target's own
`.exe` and source are untouched; only method A also requires two extra
DLLs to sit in the same folder (nothing about the target's own binary
changes).

**Activation method A — environment variables:**

```powershell
# One-time: build the loader (also builds WpfSpyAgent's net48 output)
cd WpfSpyAgent.FrameworkHook
dotnet build

# Point it at SampleWpfApp (or swap in any other .NET Framework WPF app's folder)
cd ..\SampleWpfApp
dotnet build -f net48
Copy-Item ..\WpfSpyAgent.FrameworkHook\bin\Debug\net48\WpfSpyAgent.FrameworkHook.dll bin\Debug\net48\
Copy-Item ..\WpfSpyAgent.FrameworkHook\bin\Debug\net48\WpfSpyAgent.dll bin\Debug\net48\

$env:COMPLUS_AppDomainManagerAssembly = "WpfSpyAgent.FrameworkHook, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null"
$env:COMPLUS_AppDomainManagerType = "WpfSpyAgent.FrameworkHook.SpyAppDomainManager"
$env:WPFSPY_AGENT_ENABLED = "1"
bin\Debug\net48\SampleWpfApp.exe
```

The CLR resolves the assembly name via its normal probing rules, so
`WpfSpyAgent.FrameworkHook.dll` (and its `WpfSpyAgent.dll` dependency,
built for `net48`) need to be **placed in the target app's own
directory** (or the GAC) for this to resolve.

> The `COMPLUS_` environment-variable prefix for this is a long-standing,
> widely-referenced technique, but Microsoft's primary documented
> activation path is method B below — if the environment variable
> doesn't take effect on your exact .NET Framework version/patch level,
> fall back to it.


**Activation method B — the target's `.exe.config` file (Microsoft's
primary documented path):**

```xml
<!-- SomeThirdPartyFrameworkWpfApp.exe.config -->
<configuration>
  <runtime>
    <appDomainManagerAssembly value="WpfSpyAgent.FrameworkHook, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null" />
    <appDomainManagerType value="WpfSpyAgent.FrameworkHook.SpyAppDomainManager" />
  </runtime>
</configuration>
```

This edits a side-car XML config file, not the application's source or
compiled binary.

## Option 2 — If you DO own the source: cooperative in-process hosting

Not implemented anywhere in this repo anymore (see the note at the top —
`SampleWpfApp` is intentionally kept unmodified so Options 1a/1b can be
verified against a genuine target). If you own the app's source and can
afford one build-time change, this is the simplest, most robust
alternative to injection: add a `ProjectReference` to `WpfSpyAgent`, and
call it from your own startup path:

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

No environment-variable-triggered loader, no runtime dependency on
`DOTNET_STARTUP_HOOKS`/`AppDomainManager` — just a normal reference and a
normal method call, gated behind a flag so production builds are
unaffected.

## Option 3 — True "attach to an already-running process" (not implemented here)

If you genuinely need to attach to a process that's *already running*,
with *no relaunch*, the same way Snoop's live "attach to window" feature
works — two Microsoft-sanctioned mechanisms exist. Both are real, both
work, and both are meaningfully more involved than Options 1–2 (they
need a small piece of native/COM code, which is a separate, focused unit
of work — ask if you want either one built out):

**a) Message-hook–based loading (`SetWindowsHookEx`) — this is how Snoop
itself works.** `SetWindowsHookEx` with a non-low-level hook type
(`WH_CALLWNDPROC`, `WH_GETMESSAGE`, etc.) targeting a specific thread is
a first-class Win32 API that Windows itself uses to map your hook DLL
into the target process — a completely different, sanctioned code path
from manual memory injection, and the reason decades of legitimate
accessibility/debugging/utility tools (including Snoop) can do this
without being flagged as malicious. On modern .NET, the hook DLL itself
must still be a real native module; a NativeAOT-published C# component
exposing an `[UnmanagedCallersOnly]` export can serve as that native
module, which then uses .NET's official "native hosting" APIs
(`nethost`/`hostfxr` — see [Microsoft's native hosting
docs](https://learn.microsoft.com/dotnet/core/tutorials/netcore-hosting))
to load and start `WpfSpyAgent.dll`'s existing `SpyAgentHost.Start()`
inside the now-hooked target process.

**b) CLR Profiler Attach (`ICLRProfiling::AttachProfiler` for .NET
Framework, or `Microsoft.Diagnostics.NETCore.Client`'s `AttachProfiler`
for .NET Core 3.0+).** This is the actual, supported diagnostics API real
profiler/APM products use to attach to an already-running .NET process
with no relaunch. It requires implementing a native `ICorProfilerCallback`
COM component — more upfront work than (a), but it's the most
"blessed by Microsoft" of all the options here.

**Practical alternative:** if you need this capability *today* rather
than as a build-out, [Snoop](https://github.com/snoopwpf/snoopwpf) (MIT
licensed, actively maintained) already solves exactly this problem for
inspecting/interacting with an arbitrary running WPF app's visual tree.
It's worth using directly, or as a reference for the message-hook
approach above, rather than this project re-solving process attachment
from scratch.

## Which one should you use?

| Your situation | Use |
|---|---|
| You own the app's source, can rebuild | Option 2 (cooperative) |
| You can relaunch the target, modern .NET (Core/5+) | Option 1a (`DOTNET_STARTUP_HOOKS`) — implemented, ready to use |
| You can relaunch the target, .NET Framework | Option 1b (`AppDomainManager`) — implemented, ready to use |
| Must attach to an already-running process, no relaunch | Ask — Option 3a or 3b, or use Snoop directly today |

## Which runtime is my target app on?

If you don't already know: check whether it ships a `.runtimeconfig.json`
next to the `.exe` (modern .NET → use Option 1a) or an `.exe.config` +
no `.runtimeconfig.json` (.NET Framework → use Option 1b). `dotnet-info`
or simply opening the exe in a tool like ILSpy/dnSpy and checking its
referenced `mscorlib`/`System.Private.CoreLib` also tells you immediately.
