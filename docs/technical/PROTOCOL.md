# WPFSpy IPC Wire Protocol (Named Pipes)

This is the authoritative reference for the JSON messages exchanged
between the WPFSpy driver (test-runner side, Python —
`drivers_rf/wpfspy_robotframework/WPFSpyLibrary.py`'s `WPFSpyRealDriver`)
and the in-process Spy Agent (`WpfSpyAgent/`), loaded into an otherwise
unmodified target process via one of the injection mechanisms in
`docs/INJECTION_OPTIONS.md`.

## Transport

- **Named Pipe**, name `WPFSpyAgentPipe` by default (override via the
  `WPFSPY_PIPE_NAME` environment variable on both the app and the driver
  side — they must match).
- Byte-mode stream (`PipeTransmissionMode.Byte`), UTF-8 text.
- **One JSON object per line** in each direction (`\n`-terminated). A new
  pipe connection is opened by the client per call in the current
  implementation (`WPFSpyRealDriver._send`) — simple and robust, at the
  cost of a small per-call connect overhead. A persistent connection is a
  possible future optimization (kept single-request-per-connection here
  for clarity and resilience to the WPF app restarting between calls).

## Request

```json
{"command": "Invoke", "name": "btnCreateOrder"}
{"command": "SetValue", "name": "txtQty", "value": "3"}
{"command": "GetText", "name": "lblConfirmation"}
{"command": "IsVisible", "name": "lblConfirmation"}
{"command": "Toggle", "name": "chkPriority"}
{"command": "Find", "name": "chkPriority"}
{"command": "ProbeAt", "x": 412.5, "y": 268.0}
{"command": "FindByXPath", "xpath": "/Window[@Name='MainWindow']/Grid/Button[@Name='btnSubmit']"}
{"command": "Invoke", "xpath": "/Window[@Name='MainWindow']/Grid/Button[@Name='btnSubmit']"}
```

| Field | Type | Required | Notes |
|---|---|---|---|
| `command` | string | yes | One of `Find`, `Invoke`, `SetValue`, `GetText`, `IsVisible`, `Toggle`, `ProbeAt`, `FindByXPath` |
| `name` | string | yes (except `ProbeAt`, `FindByXPath`) | The target element's WPF `Name` (`FrameworkElement.Name`) |
| `xpath` | string | `FindByXPath` only, optional for others | An XPath expression locating the element from the root window. When supplied, `name` is ignored. |
| `value` | string | `SetValue` only | The value to set |
| `x`, `y` | number | `ProbeAt` only | Screen coordinates to hit-test |

**XPath syntax** (simplified WPF visual-tree subset):

```xpath
/Window[@Name='MainWindow']/Grid/Button[@Name='btnSubmit']
/Window[@Name='Orders']/CheckBox[2]
```

- `/` — absolute path from the root window.
- `ElementName` — matches by WPF type name (`Window`, `Grid`, `Button`, `TextBox`, ...).
- `[@Name='value']` — matches by `FrameworkElement.Name`.
- `[N]` — matches the N-th child of that type among its siblings (1-based).

**Elements are resolved fresh on every request** by walking the live
visual tree (`VisualTreeInspector.FindByName` or `VisualTreeInspector.FindByXPath`) — there is no server-side
element cache/handle. This means a `Find` call only confirms existence;
it does not need to be paired with a later "release" call, and there's
no stale-handle failure mode across page/window navigation.

## Response

```json
{"success": true, "data": null, "error": null}
{"success": true, "data": "Order confirmed: SKU-1001 x2", "error": null}
{"success": true, "data": "true", "error": null}
{"success": false, "data": null, "error": "No element with Name='chkPriority_typo' found in the current visual tree"}
```

`ProbeAt`'s `data` is itself a nested JSON string (not a plain value like the other
commands): `{"name": "chkPriority", "automationId": null, "controlType": "PriorityToggleControl", "text": "Off", "xpath": "/Window[@Name='Orders']/CheckBox[@Name='PriorityToggle']"}`
— everything the recorder needs to create one Element Repository entry from a
single click, in one round trip. `automationId` is `null` exactly when the
element has none set — the signal the recorder uses to flag "needs WPFSpy"
(see `docs/IDE_GUIDE.md`). `xpath` is the full visual-tree path to the element,
used when AutomationId/Name alone are not unique enough in a deep hierarchy.

| Field | Type | Notes |
|---|---|---|
| `success` | bool | `false` for any error (element not found, wrong control type for the requested action, malformed request, ...) |
| `data` | string or null | Command-specific payload: `GetText` → the text; `IsVisible` → `"true"`/`"false"`; everything else → `null` |
| `error` | string or null | Human-readable error message when `success` is `false` |

A `success: false` response is treated by Layer 3
(`api/DriverAgnosticApi.py`'s `_resolve_and_execute`) as "this strategy
failed" and triggers the runtime self-healing fallback to the next
configured driver, exactly like a FlaUI `ElementNotFoundError` would —
see `docs/SELF_HEALING.md`.

## Threading

Every request is dispatched onto the WPF UI (dispatcher) thread inside
the agent (`SpyAgentHost.DispatchOnUiThread`) before touching the visual
tree — the Named Pipe listener itself runs on its own background thread.

## Commands reference

| Command | Calls | Behavior |
|---|---|---|
| `Find` | `VisualTreeInspector.FindByName` / `FindByXPath` | Succeeds iff an element matching `name` or `xpath` currently exists |
| `Invoke` | `VisualTreeInspector.Invoke` | Raises `ButtonBase.Click`, or calls `ISpyInteractable.SpyInvoke()` |
| `SetValue` | `VisualTreeInspector.SetValue` | Sets `TextBox.Text` / `PasswordBox.Password` / `ComboBox.Text`, or calls `ISpyInteractable.SpySetValue()` |
| `GetText` | `VisualTreeInspector.GetText` | Reads the control's text/content, or calls `ISpyInteractable.SpyGetText()` |
| `IsVisible` | `VisualTreeInspector.IsVisible` | Returns `element.IsVisible` |
| `Toggle` | `VisualTreeInspector.Toggle` | Flips a `ToggleButton`, or calls `ISpyInteractable.SpyInvoke()` |
| `ProbeAt` | `VisualTreeInspector.FindByScreenPoint` | Hit-tests screen coordinates, walks up to the nearest named element, returns its Name/AutomationId/ControlType/text/XPath |
| `FindByXPath` | `VisualTreeInspector.FindByXPath` | Succeeds iff an element matching the XPath expression currently exists |

## Extending the protocol

Adding a new command (e.g. for `RangeValueStep`/sliders) requires three
matching changes: a new `case` in `CommandDispatcher.Execute` (agent
side), a new method on `VisualTreeInspector`, and a new method on
`WPFSpyRealDriver` (+ `WPFSpyMockDriver`, for parity) on the Python side.

Adding XPath support required:
- Agent side: `VisualTreeInspector.BuildXPath`, `VisualTreeInspector.FindByXPath`, updated `ProbeAt` to include `xpath` in response, and all action commands now accept optional `xpath` instead of `name`.
- Python side: `WPFSpyRealDriver` and `WPFSpyMockDriver` updated to pass `xpath` through existing commands, plus `FindByXPath` for discovery.
- Mock app: `MockWpfApp.find_by_xpath` with a simple XPath evaluator over the virtual visual tree.
