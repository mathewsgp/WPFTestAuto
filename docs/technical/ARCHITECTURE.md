# Architecture

## The five layers

```
┌─────────────────────────────────────────────────────────┐
│ Layer 1: Test Scripts (.robot)                            │
│   Business-readable test cases. Calls ONLY Layer 2.        │
├─────────────────────────────────────────────────────────┤
│ Layer 2: Reusable Test Modules (.robot)                    │
│   Action modules  +  Verification modules. Calls Layer 3.  │
├─────────────────────────────────────────────────────────┤
│ Layer 3: Driver-Agnostic API (Python RF library)            │
│   Resolves alias -> locator + step via the repositories.    │
│   Tries each configured driver strategy in order; falls    │
│   back automatically if one fails (self-healing).           │
├─────────────────────────────────────────────────────────┤
│ Layer 4: Driver RF Wrappers                                 │
│   FLaUI.RobotFramework / WPFSpy.RobotFramework /             │
│   Sikuli.RobotFramework — identical method signatures        │
│   ("API parity") so Layer 3 can swap them freely.            │
├─────────────────────────────────────────────────────────┤
│ Layer 5: Drivers                                             │
│   FlaUI (UIA) / WPFSpy (in-process agent + IPC) / Sikuli     │
│   (image match) — talk to the actual WPF application.        │
└─────────────────────────────────────────────────────────┘
```

Each layer only ever calls the layer directly below it. This is what
makes the framework driver-agnostic: nothing above Layer 3 knows FlaUI,
WPFSpy, or Sikuli exist.

## Why this shape

- **Test scripts stay business-readable** because all technical detail
  (locators, steps, drivers) lives below Layer 2.
- **Driver swapping is free.** Layer 3 reads the Element Repository's
  `strategies` block and tries whichever drivers are configured, in a
  preferred order (FlaUI → WPFSpy → Sikuli by default). No test script or
  reusable module changes when you add a driver or change which one wins
  for a given control.
- **Reliability is built in, not bolted on.** If the primary strategy
  fails at *runtime* (not just because a different control type was
  chosen at design time), Layer 3 automatically retries the next
  configured strategy before failing the step. See
  `docs/SELF_HEALING.md`.

## Data flow for one keyword call

```
Test Script (Layer 1)
  -> Create New Order            [Layer 2: order_module.robot]
     -> Set Element Value("OrdersPage.SkuComboBox", "SKU-1001")   [Layer 3]
        -> repository_access.get_strategies("OrdersPage.SkuComboBox")
           -> {"FlaUI": {...}, "WPFSpy": {...}, "Sikuli": {...}}
        -> try FlaUIDriver.find_element(...) -> found -> set_value(...)
        -> [only on failure] try WPFSpyDriver -> ... -> try SikuliDriver
     -> Click Element("OrdersPage.CreateOrderButton")             [Layer 3]
        -> same resolution + fallback logic
  -> Verify Order Confirmation Displayed(sku, qty)  [Layer 2: order_verifications.robot]
     -> Verify Element Text("OrdersPage.ConfirmationLabel", "...")  [Layer 3]
```

## Element & Step Repositories

Two YAML-backed repositories, joined by a shared `alias`:

- **Element Repository** (`repository/elements/*.yaml`) — one entry per
  alias, with a `strategies` block containing one sub-block per driver
  that can locate it (`FlaUI`, `WPFSpy`, `Sikuli`), plus shared metadata
  (`controlType`, `parentAlias`, `defaultTimeout`, `tags`).
- **Step Repository** (`repository/steps/*.yaml`) — one entry per alias,
  naming the interaction pattern (`InvokeStep`, `ValueStep`, `ToggleStep`,
  `TextStep`, ...) and its parameters.

`api/repository_access.py` loads and merges every YAML file under each
directory at first use and caches the result; `api/DriverAgnosticApi.py`
is the only consumer.

See `docs/ELEMENT_REPOSITORY_GUIDE.md` for the full schema reference.

## WPFSpy: in-process execution + IPC

WPFSpy's distinguishing design point vs FlaUI is that it runs an agent
**inside** the target application's process (for custom-rendered WPF
controls that don't expose reliable UI Automation properties), and talks
to it via a Named Pipes / gRPC channel. See `docs/WPFSPY_MODULE.md`.

## Runtime self-healing locators

Layer 3's `_resolve_and_execute` tries every strategy configured for an
alias, in order, and only fails the step if *all* of them fail — logging
the full attempt chain either way. See `docs/SELF_HEALING.md`
for the design rationale and a live demo test.

## Recording & playback

The authoring workflow (Record → Auto-generate → Add Verifications →
Refactor to Reusable Modules → Finalize) is implemented in `recorder/`.
See `docs/RECORDER_GUIDE.md`.

## Mock application vs production

Everything above Layer 5 is production code as-is. Layer 5 in this
repository targets a mock WPF application (`drivers/mock_wpf_app/`) so the
framework can run anywhere without Windows/.NET. See
`docs/PRODUCTION_DEPLOYMENT.md` for exactly what changes when pointing
this framework at a real WPF application.
