# WpfSpyAgent

The in-process Spy Agent: a .NET class library. **Windows-only** (touches
WPF visual-tree types directly). Multi-targeted (`net6.0-windows` and
`net48`) so the same agent source can be loaded into either a modern .NET
or a .NET Framework WPF process.

It has **no dependents that reference it at compile time** among the
sample apps in this repo — `SampleWpfApp` is deliberately built without
any reference to this project at all (see `docs/INJECTION_OPTIONS.md`).
Instead, one of two small loader projects gets `SpyAgentHost.Start()`
running inside a target process from the outside:

| Loader | Target runtime | Mechanism |
|---|---|---|
| `../WpfSpyAgent.StartupHook/` | Modern .NET (Core 3.0+/.NET 5+) | `DOTNET_STARTUP_HOOKS` environment variable |
| `../WpfSpyAgent.FrameworkHook/` | .NET Framework | Custom `AppDomainManager` (env var or `.exe.config`) |

Both are genuinely zero-source-modification: the target app's own
binary/source never changes. See `docs/INJECTION_OPTIONS.md` for full
commands, tradeoffs, and — just as importantly — what we deliberately
did **not** build (a generic process-injection utility) and why.

If you *do* own the target app's source, a third option (simpler than
either loader, but requiring a rebuild) is covered as "Option 2" in that
same doc: reference this project directly and call
`SpyAgentHost.Start()` from your own startup code.

## Files

| File | Responsibility |
|---|---|
| `SpyAgentHost.cs` | Named Pipe server loop; marshals each request onto the WPF UI thread |
| `CommandDispatcher.cs` | Parses one JSON request, calls `VisualTreeInspector`, serializes the response |
| `VisualTreeInspector.cs` | Walks the live visual tree by `Name`; acts on controls directly (no UI Automation) |
| `ISpyInteractable.cs` | Opt-in contract for custom-rendered controls (no AutomationPeer needed) |
| `Protocol/Messages.cs` | JSON request/response DTOs — see `../docs/PROTOCOL.md` for the wire format |

## Why not use UI Automation internally?

That's the whole point of WPFSpy existing alongside FlaUI: FlaUI *is*
UI-Automation-based, so anything UIA can already see, FlaUI already
handles well. WPFSpy's value is reaching controls UIA **can't** see
reliably — which means its internals must not depend on UIA either.
`VisualTreeInspector` acts on `TextBox`/`ButtonBase`/`ComboBox`/etc.
directly via their real WPF properties and events, and on custom controls
via `ISpyInteractable` — never via `System.Windows.Automation` (with one
narrow exception: `CommandDispatcher`'s `ProbeAt` command reads the
`AutomationProperties.AutomationId` *attached property value* directly —
not via a full AutomationPeer/UIA runtime call — purely so
`WpfTestIde`'s recorder can report whether a clicked control has one).

## Testing this in isolation

There's no unit test project here (kept out of scope for this reference
implementation) — the closest thing is
`tests/self_healing_locators_demo.robot` run against a live
`SampleWpfApp` instance (with the agent injected via one of the loaders
above) and `WPFSPY_MODE=real`, which exercises the full driver → pipe →
agent → visual tree path. See `../SampleWpfApp/README.md` and
`../docs/INJECTION_OPTIONS.md` for exact commands.
