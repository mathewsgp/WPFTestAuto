# WPF Test Automation Framework - Technical Design Specification

## 1. Overview

### 1.1 Purpose
The WPF Test Automation Framework is a driver-agnostic, layered automation system for testing Windows Presentation Foundation (WPF) applications. It provides reliable UI test automation through multiple driver backends (FlaUI, WPFSpy, Sikuli) with automatic fallback capabilities.

### 1.2 Key Characteristics
- **Driver-Agnostic**: Test scripts are written once and execute against multiple automation engines
- **Self-Healing**: Automatic fallback to alternative drivers when primary strategies fail
- **Cross-Platform Execution**: Supports running tests on non-Windows platforms via mock drivers
- **Recording & Playback**: IDE for recording user interactions and auto-generating test artifacts

---

## 2. Architecture

### 2.1 Five-Layer Architecture

```
┌─────────────────────────────────────────────────────────────┐
│ Layer 1: Test Scripts (.robot)                               │
│ Business-readable test cases. Calls ONLY Layer 2.            │
├─────────────────────────────────────────────────────────────┤
│ Layer 2: Reusable Test Modules (.robot)                      │
│ Action modules + Verification modules. Calls Layer 3.        │
├─────────────────────────────────────────────────────────────┤
│ Layer 3: Driver-Agnostic API (Python RF library)            │
│ Resolves alias → locator + step via repositories.           │
│ Tries configured drivers in order; self-heals on failure.   │
├─────────────────────────────────────────────────────────────┤
│ Layer 4: Driver RF Wrappers                                  │
│ FlaUI.RF / WPFSpy.RF / Sikuli.RF — identical signatures.   │
├─────────────────────────────────────────────────────────────┤
│ Layer 5: Drivers                                             │
│ FlaUI (UIA) / WPFSpy (in-process agent + IPC) / Sikuli      │
└─────────────────────────────────────────────────────────────┘
```

### 2.2 Component Overview

| Component | Language | Layer | Description |
|-----------|----------|-------|-------------|
| `api/DriverAgnosticApi.py` | Python | 3 | Central resolution engine with self-healing |
| `api/repository_access.py` | Python | 3 | YAML repository loader and cache |
| `drivers_rf/flaui_robotframework/` | Python | 4 | FlaUI driver wrapper |
| `drivers_rf/wpfspy_robotframework/` | Python | 4 | WPFSpy driver wrapper |
| `drivers_rf/sikuli_robotframework/` | Python | 4 | Sikuli driver wrapper |
| `drivers/mock_wpf_app/` | Python | 5 | Cross-platform mock application |
| `WpfSpyAgent/` | C# (.NET) | 5 | In-process automation agent |
| `WpfSpyAgent.StartupHook/` | C# (.NET) | 5 | Modern .NET injection loader |
| `WpfSpyAgent.FrameworkHook/` | C# (.NET) | 5 | .NET Framework injection loader |
| `WpfTestIde/` | C# (WPF) | IDE | Recording and authoring IDE |

---

## 3. Component Specifications

### 3.1 Layer 3: Driver-Agnostic API

**File**: `api/DriverAgnosticApi.py`

#### Class: `DriverAgnosticApi`

The core Robot Framework library providing test automation keywords.

##### Core Method: `_resolve_and_execute(alias, action_name, *args)`

```
Input:  alias (str)      - Element alias from repository
        action_name (str) - Method name on driver (invoke, set_value, etc.)
        *args            - Action-specific arguments

Output: Driver-specific result or raises AllStrategiesFailedError

Behavior:
1. Retrieve strategies from Element Repository for alias
2. Check WPFSPY_MODE environment variable
3. Execute WPFSpy strategy with configured locator
4. On failure, raise AllStrategiesFailedError with attempt log
```

##### Public Keywords

| Keyword | Description |
|---------|-------------|
| `Click Element` | Invokes (clicks) element by alias |
| `Set Element Value` | Sets text/value on element by alias |
| `Get Element Text` | Returns element's current text |
| `Verify Element Text` | Asserts element text equals expected |
| `Toggle Element` | Toggles checkbox/toggle element |
| `Wait Until Element Visible` | Polls until element visible or timeout |
| `Reset Application` | Restarts app for test isolation |
| `Get Data Grid Content OCR` | Captures DataGrid screenshot, returns CSV via OCR |

##### Environment Variables

| Variable | Values | Default | Description |
|----------|--------|---------|-------------|
| `WPFSPY_MODE` | `mock`, `real` | `mock` | Use mock app or real WPFSpy agent |
| `WPFSPY_IDE_RUN` | `1`, unset | unset | IDE mode (keeps app running between tests) |

#### Class: `AllStrategiesFailedError`

Raised when all configured driver strategies fail. Carries the complete attempt log for diagnosis.

```python
AllStrategiesFailedError: "WPFSpy strategy failed for alias 'OrdersPage.PriorityCheckbox': ..."
```

---

### 3.2 Layer 4: Robot Framework Driver Wrappers

#### 3.2.1 FlaUI Wrapper
**Path**: `drivers_rf/flaui_robotframework/`

Wraps FlaUI (UI Automation) for standard WPF control interaction.

#### 3.2.2 WPFSpy Wrapper  
**Path**: `drivers_rf/wpfspy_robotframework/`

Two driver modes:
- `WPFSpyRealDriver`: Named Pipe client for real in-process agent
- `WPFSpyMockDriver`: Mock fallback for cross-platform execution

#### 3.2.3 Sikuli Wrapper
**Path**: `drivers_rf/sikuli_robotframework/`

Image-based driver for visual matching (screen region capture and OCR).

---

### 3.3 Layer 5: WPFSpy Agent

#### 3.3.1 Agent Host
**File**: `WpfSpyAgent/SpyAgentHost.cs`

```csharp
public static class SpyAgentHost
{
    public static void Start(string pipeName = "WPFSpyAgentPipe")
    public static void Stop()
}
```

**Named Pipe Server**:
- Transport: Byte-mode UTF-8, one JSON per line
- Connection: Per-call (new connection per request)
- Threading: Listener thread + per-client handler thread
- UI Thread Dispatch: All visual tree access marshalled to WPF dispatcher thread

#### 3.3.2 Command Dispatcher
**File**: `WpfSpyAgent/CommandDispatcher.cs`

Parses JSON requests and dispatches to `VisualTreeInspector`.

**Supported Commands**:

| Command | Parameters | Description |
|---------|------------|-------------|
| `Find` | `name`, `xpath` | Succeeds iff element exists |
| `Invoke` | `name`, `xpath` | Raises ButtonBase.Click or ISpyInteractable.SpyInvoke() |
| `SetValue` | `name`, `xpath`, `value` | Sets TextBox.Text or ISpyInteractable.SpySetValue() |
| `GetText` | `name`, `xpath` | Reads control text or ISpyInteractable.SpyGetText() |
| `IsVisible` | `name`, `xpath` | Returns element.IsVisible |
| `Toggle` | `name`, `xpath` | Toggles ToggleButton or ISpyInteractable.SpyInvoke() |
| `ProbeAt` | `x`, `y` | Screen hit-test, returns element details |
| `FindByXPath` | `xpath` | XPath-based element lookup |
| `GetMainWindowTitle` | - | Returns main window title |
| `GetBounds` | `name`, `xpath` | Returns element screen coordinates |
| `Highlight` | `name`, `xpath` | Shows element highlight overlay |
| `ResetState` | - | Resets app to login page |

#### 3.3.3 Visual Tree Inspector
**File**: `WpfSpyAgent/VisualTreeInspector.cs`

Directly walks WPF visual tree without UI Automation.

**Key Capabilities**:
- `FindByName(name)`: Finds element by FrameworkElement.Name
- `FindByXPath(xpath)`: Evaluates XPath against visual tree
- `FindByScreenPoint(x, y)`: Hit-test at screen coordinates
- `BuildXPath(element)`: Generates XPath path to element
- `Invoke/SetValue/GetText/Toggle/IsVisible`: Control interactions

**XPath Syntax** (WPF visual-tree subset):
```
/Window[@Name='MainWindow']/Grid/Button[@Name='btnSubmit']
/Window[@Name='Orders']/CheckBox[2]
```

#### 3.3.4 ISpyInteractable Interface
**File**: `WpfSpyAgent/ISpyInteractable.cs`

For custom WPF controls that need explicit automation support:

```csharp
public interface ISpyInteractable
{
    void SpyInvoke();
    void SpySetValue(string value);
    string SpyGetText();
}
```

---

### 3.4 Injection Mechanisms

#### 3.4.1 Modern .NET (Core 3.0+/.NET 5+)
**Path**: `WpfSpyAgent.StartupHook/`

Uses `DOTNET_STARTUP_HOOKS` environment variable:

```powershell
$env:DOTNET_STARTUP_HOOKS = "C:\path\to\WpfSpyAgent.StartupHook.dll"
$env:WPFSPY_AGENT_ENABLED = "1"
dotnet run
```

#### 3.4.2 .NET Framework
**Path**: `WpfSpyAgent.FrameworkHook/`

Uses custom `AppDomainManager`:

```powershell
$env:COMPLUS_AppDomainManagerAssembly = "WpfSpyAgent.FrameworkHook"
$env:COMPLUS_AppDomainManagerType = "WpfSpyAgent.FrameworkHook.SpyAppDomainManager"
$env:WPFSPY_AGENT_ENABLED = "1"
```

Or via `app.exe.config`:
```xml
<configuration>
  <runtime>
    <appDomainManagerAssembly value="WpfSpyAgent.FrameworkHook" />
    <appDomainManagerType value="WpfSpyAgent.FrameworkHook.SpyAppDomainManager" />
  </runtime>
</configuration>
```

---

### 3.5 Element & Step Repositories

**Location**: `repository/`

```
repository/
├── elements/
│   ├── login_page.yaml
│   └── orders_page.yaml
└── steps/
    └── steps.yaml
```

#### Element Repository Schema

```yaml
elements:
  <Alias>:
    displayName: <string>
    controlType: <string>          # TextBox | Button | ComboBox | Label | DataGrid | CheckBox | ...
    parentAlias: <string>           # For scoped/relative lookups
    defaultTimeout: <int seconds>
    tags: [<string>, ...]
    strategies:
      FlaUI:
        searchBy: "AutomationId"
        value: <string>
      WPFSpy:
        searchBy: "Name"
        value: <string>
      Sikuli:
        imagePath: <string>
        similarity: <float 0-1>
```

#### Step Repository Schema

```yaml
steps:
  <Alias>:
    step: "InvokeStep" | "ValueStep" | "ToggleStep" | "TextStep" | "RangeValueStep"
    parameters:
      - name: <string>
        type: "string" | "boolean" | "double"
        required: <bool>
```

---

### 3.6 Mock WPF Application

**File**: `drivers/mock_wpf_app/mock_app.py`

Cross-platform simulation of a two-screen WPF application (Login, Orders).

**State Machine**:
```
[Login Page] --valid credentials--> [Orders Page]
[Orders Page] --logout button----> [Login Page]
```

**Mock Controls**:

| Key | AutomationId | Name | Type | Notes |
|-----|--------------|------|------|-------|
| `txtUsername` | `txtUsername` | `UsernameInput` | TextBox | |
| `txtPassword` | `txtPassword` | `PasswordInput` | TextBox | |
| `btnSubmit` | `btnSubmit` | `SubmitBtn` | Button | Triggers login |
| `lblError` | `lblError` | `ErrorLabel` | Label | Shows on failed login |
| `cmbSku` | `cmbSku` | `SkuCombo` | ComboBox | SKU selection |
| `txtQty` | `txtQty` | `QtyInput` | TextBox | Quantity input |
| `btnCreateOrder` | `btnCreateOrder` | `CreateOrderBtn` | Button | Creates order |
| `lblConfirmation` | `lblConfirmation` | `ConfirmationLabel` | Label | Order confirmation |
| `gridOrders` | `gridOrders` | `OrdersGrid` | DataGrid | Order list |
| `chkPriority` | `None` | `PriorityToggle` | CheckBox | **No AutomationId** (custom control) |

---

## 4. IPC Protocol Specification

### 4.1 Transport Layer

| Property | Value |
|----------|-------|
| Protocol | Named Pipes |
| Pipe Name | `WPFSpyAgentPipe` (configurable via `WPFSPY_PIPE_NAME`) |
| Transmission | Byte-mode, UTF-8 text |
| Framing | One JSON object per line (`\n`-terminated) |
| Connection | Per-request (new connection per call) |

### 4.2 Request Format

```json
{"command": "<CommandName>", "<param>": "<value>"}
```

### 4.3 Response Format

```json
{"success": true|false, "data": "<result>|null", "error": "<message>|null"}
```

### 4.4 ProbeAt Response

```json
{
  "success": true,
  "data": "{\"name\":\"PriorityToggle\",\"automationId\":null,\"controlType\":\"CheckBox\",\"text\":\"Off\",\"xpath\":\"/Window[@Name='Orders']/CheckBox[@Name='PriorityToggle']\"}",
  "error": null
}
```

---

## 5. Self-Healing Locator System

### 5.1 Strategy Resolution Order

```
FlaUI → WPFSpy → Sikuli
```

Only configured strategies are attempted. Skip order: `FlaUI` → `WPFSpy` → `Sikuli`.

### 5.2 Fallback Logic

```python
def _resolve_and_execute(alias, action_name, *args):
    strategies = repo.get_strategies(alias)
    
    for driver_name, locator in strategies.items():
        try:
            element = driver.find_element(locator)
            return driver.action(element, *args)
        except (ElementNotFoundError, ElementNotInteractableError, KeyError):
            continue  # Try next driver
    
    raise AllStrategiesFailedError(full_attempt_log)
```

### 5.3 Failure Diagnostics

`AllStrategiesFailedError` includes complete attempt log:
```
All configured strategies failed for alias 'OrdersPage.PriorityCheckbox'.
Attempts: [('WPFSpy', "SUCCESS")]
```

---

## 6. Recording & IDE

### 6.1 WpfTestIde Components

| Component | Description |
|-----------|-------------|
| `GlobalMouseHook.cs` | Win32 WH_MOUSE_LL hook for cross-process click detection |
| `SpyAgentClient.cs` | Named Pipe client for element probing |
| `ElementProbe.cs` | FlaUI-first, WPFSpy-fallback resolution |
| `RecordingSession.cs` | Orchestrates recording into `RecordedStep` events |
| `ScriptGenerator.cs` | `RecordedStep` list → Robot Framework script |
| `RepositoryWriter.cs` | `ElementEntry` list → YAML repository |
| `RobotRunner.cs` | Executes tests via `python -m robot` |

### 6.2 Recording Workflow

1. **Attach**: Select running WPF process by window title
2. **Record**: Global hook captures clicks; focus-lost captures text input
3. **Auto-Generate**: Repository entries + script generated live
4. **Verify**: Add verification steps with pre-filled expected values
5. **Run**: Execute script, stream output to IDE
6. **Export**: Save to `repository/elements/` and `tests/`

---

## 7. Test Structure

### 7.1 Test Layers

**Layer 1** - Test Scripts (`tests/`):
```robotframework
*** Test Cases ***
My Test
    Login To Application    user1    Pass@123
    Create New Order    SKU-1001    2
```

**Layer 2** - Reusable Modules (`modules/`):
```robotframework
*** Keywords ***
Create New Order
    [Arguments]    ${sku}    ${qty}
    Select From ComboBox    OrdersPage.SkuComboBox    ${sku}
    Set Element Value    OrdersPage.QtyTextBox    ${qty}
    Click Element    OrdersPage.CreateOrderButton
```

### 7.2 Test Execution

```bash
# Full suite
python3 -m robot --outputdir results tests/

# By tag
python3 -m robot -i smoke tests/

# Single test
python3 -m robot -t "Create And Confirm New Order" tests/
```

---

## 8. Data Models

### 8.1 Control (Mock App)

```python
@dataclass
class Control:
    key: str                           # Internal identity
    automation_id: Optional[str]       # UIA AutomationId (None for custom controls)
    name: str                          # FrameworkElement.Name
    control_type: str                  # WPF type
    text: str                          # Current text content
    visible: bool                      # Visibility state
    enabled: bool                      # Enabled state
    image_tag: Optional[str]          # Sikuli match target
    xpath: Optional[str]              # Visual tree path
```

### 8.2 SpyRequest

```csharp
public class SpyRequest
{
    public string Command { get; set; }
    public string Name { get; set; }
    public string XPath { get; set; }
    public string Value { get; set; }
    public double? X { get; set; }
    public double? Y { get; set; }
}
```

### 8.3 SpyResponse

```csharp
public class SpyResponse
{
    public bool Success { get; set; }
    public string Data { get; set; }
    public string Error { get; set; }
}
```

---

## 9. Configuration

### 9.1 Environment Variables

| Variable | Required | Default | Description |
|----------|----------|---------|-------------|
| `WPFSPY_MODE` | No | `mock` | Execution mode: `mock` or `real` |
| `WPFSPY_IDE_RUN` | No | - | IDE mode flag |
| `WPFSPY_PIPE_NAME` | No | `WPFSpyAgentPipe` | Named pipe name |
| `DOTNET_STARTUP_HOOKS` | No* | - | Path to StartupHook DLL |
| `WPFSPY_AGENT_ENABLED` | No* | - | Enable agent flag |
| `COMPLUS_AppDomainManagerAssembly` | No* | - | Framework hook assembly |

*Required for real WPFSpy mode

### 9.2 Repository Configuration

Element and step repositories are loaded at suite setup from:
- `repository/elements/*.yaml` (merged into single element map)
- `repository/steps/*.yaml` (merged into single step map)

---

## 10. Error Handling

### 10.1 Exception Types

| Exception | Layer | Description |
|-----------|-------|-------------|
| `ElementNotFoundError` | 4/5 | Element not found in mock/app |
| `ElementNotInteractableError` | 4/5 | Element not visible/enabled |
| `AllStrategiesFailedError` | 3 | All configured strategies failed |
| `KeyError` | 3 | Missing alias in repository |

### 10.2 Error Response Format

WPFSpy agent returns:
```json
{"success": false, "data": null, "error": "No element with Name='chkPriority' found"}
```

---

## 11. Dependencies

### 11.1 Python Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| robotframework | 7.x | Test execution |
| pyyaml | - | YAML parsing |
| FlaUILibrary | - | UI Automation wrapper |
| robotframework-requests | - | HTTP client |
| pywin32 | - | Named Pipe client (Windows) |

### 11.2 .NET Dependencies

| Package | Purpose |
|---------|---------|
| FlaUI.Core | UI Automation core |
| FlaUI.UIA3 | UIA3 automation |
| YamlDotNet | YAML serialization |
| Grpc.AspNetCore | gRPC server (optional) |

---

## 12. Platform Support

| Platform | FlaUI | WPFSpy | Sikuli | Mock |
|----------|-------|--------|--------|------|
| Windows + .NET | ✅ | ✅ | ✅ | ✅ |
| Linux/macOS | ❌ | ❌ | ✅ | ✅ |

Mock mode enables full framework testing on non-Windows platforms.

---

## 13. Extension Points

### 13.1 Adding New Commands

To extend WPFSpy protocol:

1. **Agent side**: Add case in `CommandDispatcher.Execute()`
2. **Inspector**: Add method in `VisualTreeInspector`
3. **Driver**: Add method in `WPFSpyRealDriver` and `WPFSpyMockDriver`
4. **Layer 3**: Add keyword in `DriverAgnosticApi`

### 13.2 Adding New Drivers

1. Create driver wrapper in `drivers_rf/<driver>_robotframework/`
2. Implement: `find_element()`, `invoke()`, `set_value()`, `get_text()`, `is_visible()`, `toggle()`
3. Register in `_DRIVERS` dict in `DriverAgnosticApi.__init__()`
4. Add strategy entries to Element Repository

### 13.3 Adding New Step Types

1. Add step type to `StepType` enum
2. Add handling in `DriverAgnosticApi._resolve_and_execute()`
3. Document in `docs/ELEMENT_REPOSITORY_GUIDE.md`

---

## 14. Security Considerations

- Named Pipe uses Windows security (only same-user processes can connect)
- No secrets stored in repositories (locators only)
- Startup hooks require local machine access to set environment variables
- Agent injection documented as legitimate Windows extensibility mechanism

---

## 15. Glossary

| Term | Definition |
|------|------------|
| **Alias** | Unique identifier for UI element (e.g., `OrdersPage.CreateOrderButton`) |
| **Strategy** | Driver-specific locator configuration |
| **Self-Healing** | Automatic fallback to alternative strategies at runtime |
| **Driver** | Automation engine (FlaUI, WPFSpy, Sikuli) |
| **IPC** | Inter-Process Communication |
| **UIA** | UI Automation (Microsoft accessibility API) |
| **Visual Tree** | WPF runtime element hierarchy |
| **ProbeAt** | Screen coordinate hit-test command |

---

## 16. References

- [ARCHITECTURE.md](./ARCHITECTURE.md) - High-level architecture overview
- [WPFSPY_MODULE.md](./WPFSPY_MODULE.md) - WPFSpy agent details
- [PROTOCOL.md](./PROTOCOL.md) - IPC wire protocol reference
- [INJECTION_OPTIONS.md](./INJECTION_OPTIONS.md) - Agent injection mechanisms
- [SELF_HEALING_LOCATORS.md](./SELF_HEALING_LOCATORS.md) - Self-healing system
- [ELEMENT_REPOSITORY_GUIDE.md](./ELEMENT_REPOSITORY_GUIDE.md) - Repository schema
- [RECORDER_GUIDE.md](./RECORDER_GUIDE.md) - Recording workflow
- [IDE_GUIDE.md](./IDE_GUIDE.md) - WpfTestIde documentation
- [GETTING_STARTED.md](./GETTING_STARTED.md) - Quick start guide
- [PRODUCTION_DEPLOYMENT.md](./PRODUCTION_DEPLOYMENT.md) - Production deployment
