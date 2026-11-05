# Multi-Application Automation Plan

## Current Architecture Limitations

| Aspect | Current State | Limitation |
|--------|--------------|------------|
| Driver instances | Global singletons (`_DRIVERS`) | Cannot target different apps simultaneously |
| Process management | Single `_SAMPLE_WPF_APP_PROCESS` | Only one app can be launched/managed |
| Pipe/connection | Single `WPFSpyAgentPipe` | WPFSpy limited to one WPF app |
| Element repository | Global aliases | No app context — aliases collide across apps |
| IDE attach flow | Single process selection | Cannot attach to multiple apps |
| Recording | Single app session | Cannot record cross-app workflows |

## Target Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                    Test Script (Layer 1)                      │
│  Robot test file with multi-app keywords                      │
└─────────────────────────────┬─────────────────────────────────┘
                              │
┌─────────────────────────────▼─────────────────────────────────┐
│              Reusable Modules (Layer 2)                        │
│  LoginModule, OrderModule — app-agnostic                       │
└─────────────────────────────┬─────────────────────────────────┘
                              │
┌─────────────────────────────▼─────────────────────────────────┐
│              Driver-Agnostic API (Layer 3)                     │
│  - MultiAppContext: registry of AppContext objects             │
│  - AppContext: per-app driver pool + element scope             │
│  - Keywords accept app_id parameter                            │
└─────────────────────────────┬─────────────────────────────────┘
                              │
        ┌─────────────────────┼─────────────────────┐
        │                     │                     │
┌───────▼──────┐      ┌───────▼──────┐      ┌───────▼──────┐
│  FlaUI Driver│      │ WPFSpy Driver│      │ Sikuli Driver│
│  (multi-app) │      │ (WPF only)   │      │ (any app)    │
└──────────────┘      └──────────────┘      └──────────────┘
```

## Phase 1: Framework Core (Python)

### 1.1 App Context Model

Add `AppContext` class in `api/app_context.py`:

```python
class AppContext:
    """Represents a single application under automation."""
    def __init__(self, app_id: str, app_name: str, driver: str, 
                 process_id: int = None, pipe_name: str = None):
        self.app_id = app_id           # Logical ID: "main", "helper", "db"
        self.app_name = app_name       # Human name: "SampleWpfApp"
        self.driver = driver           # "FlaUI" or "WPFSpy"
        self.process_id = process_id   # OS process ID
        self.pipe_name = pipe_name     # WPFSpy pipe name
        self.drivers = {}              # Initialized driver instances
        self.element_scope = None      # App-specific element repo scope
        
    def get_driver(self, driver_name: str):
        """Get or create driver instance for this app."""
        if driver_name not in self.drivers:
            self.drivers[driver_name] = _create_driver_for_app(driver_name, self)
        return self.drivers[driver_name]
```

### 1.2 Multi-App Registry

```python
class MultiAppContext:
    """Registry of all applications under automation."""
    def __init__(self):
        self.apps: Dict[str, AppContext] = {}
        self.default_app_id: Optional[str] = None
        
    def register_app(self, app_context: AppContext) -> str:
        """Register a new app context, return app_id."""
        self.apps[app_context.app_id] = app_context
        return app_context.app_id
        
    def get_app(self, app_id: str = None) -> AppContext:
        """Get app context, defaulting to default_app_id."""
        app_id = app_id or self.default_app_id
        if app_id not in self.apps:
            raise ValueError(f"App '{app_id}' not registered")
        return self.apps[app_id]
        
    def set_default_app(self, app_id: str):
        """Set the default app for keywords without explicit app_id."""
        self.default_app_id = app_id
```

### 1.3 Keyword Changes

Add app-aware keywords to `DriverAgnosticApi`:

| New Keyword | Purpose |
|-------------|---------|
| `Register Application` | Register a new app context with name, driver, PID/pipe |
| `Switch Application` | Change default app context |
| `Launch Application` | Launch and register an app |
| `Attach To Application` | Attach to existing process and register |
| `Close Application` | Close and unregister an app |
| `Get Application List` | List all registered apps |
| `Wait For Application` | Wait for app to be available (polling with timeout) |
| `Capture Screenshot` | Capture screenshot for specific app or full screen |

Modify existing keywords to accept optional `app_id` parameter:

| Keyword | Change |
|---------|--------|
| `Click Element` | `Click Element    alias    app_id=optional` |
| `Set Element Value` | `Set Element Value    alias    value    app_id=optional` |
| `Get Element Text` | `Get Element Text    alias    app_id=optional` |

### 1.4 Element Repository Changes

Add app scoping to element YAML:

```yaml
elements:
  # Global elements (existing behavior preserved)
  LoginPage.txtUsername:
    displayName: "Username"
    controlType: "TextBox"
    strategies:
      FlaUI:
        - searchBy: "AutomationId"
          value: "txtUsername"
          priority: 1
      
  # App-specific elements (new)
  HelperApp.btnConfirm:
    displayName: "Confirm Button"
    appId: "helper"              # New: scoped to app
    controlType: "Button"
    strategies:
      FlaUI:
        - searchBy: "AutomationId"
          value: "btnConfirm"
          priority: 1
```

### 1.5 Driver Changes

**FlaUIDriver** (`drivers_rf/flaui_robotframework/flaui_driver.py`):
- Accept `app_context` in constructor
- Use `AutomationElement.FromHandle(process_id)` for app-specific UIA
- Support multiple concurrent app sessions

**WPFSpyDriver** (`drivers_rf/wpfspy_robotframework/WPFSpyLibrary.py`):
- Accept `pipe_name` per app context
- Only one WPFSpy app per pipe (limitation of named pipes)
- For multiple WPF apps: create multiple pipe names

**SikuliDriver** (`drivers_rf/sikuli_robotframework/SikuliLibrary.py`):
- Already screen-based, minimal changes needed
- May need region/region offset for multi-monitor

## Phase 2: IDE Support (C# WPF)

### 2.1 Multi-App Attach Dialog

New dialog `MultiAppAttachDialog.xaml`:
- List of attached apps with app_id, name, driver, PID
- "Attach Another" button
- "Set as Default" per app
- "Detach" button per app

### 2.2 Spy Tool Multi-App Mode

Update `SpyToolDialog.xaml`:
- App selector dropdown at top
- Mode selector (WPFSpy Visual Tree / FlaUI Automation Tree)
- Tree populates based on selected app
- When switching apps, reload tree from that app's context

### 2.3 Recording Session per App

Update `RecordingSession.cs`:
- Each recording session tied to one app context
- Support multiple concurrent recording sessions
- Steps tagged with app_id in recorded output

### 2.4 Element Repository per App

Update `ElementEntry.cs`:
- Add `AppId` property
- Filter element tree by app_id in IDE
- Export/import app-scoped repositories

## Phase 3: Robot Test Support

### 3.1 New Keywords

```robot
*** Settings ***
Library    api/DriverAgnosticApi.py

*** Test Cases ***
MultiApp Login And Verify
    # Register two applications
    Register Application    app_id=main    app_name=SampleWpfApp    driver=FlaUI    pid=${MAIN_PID}
    Register Application    app_id=helper    app_name=HelperApp    driver=FlaUI    pid=${HELPER_PID}
    
    # Set default app (optional — can pass app_id per keyword)
    Switch Application    app_id=main
    
    # Interact with main app
    Click Element    LoginPage.btnSubmit
    
    # Switch to helper app
    Switch Application    app_id=helper
    Click Element    HelperPage.btnConfirm
    
    # Switch back
    Switch Application    app_id=main
    Verify Order Confirmation Displayed
```

### 3.2 Implicit App Switching

For convenience, keywords can auto-switch if element belongs to different app:

```robot
Click Element    MainPage.btnSubmit    # Uses default app (main)
Click Element    HelperPage.btnConfirm  # Auto-switches to helper app
```

## Phase 4: Cross-App Workflows

### 4.1 Data Passing Between Apps

```robot
*** Test Cases ***
CrossApp DataFlow
    ${order_id}=    Get Element Text    MainPage.lblOrderId
    Switch Application    helper
    Set Element Value    HelperPage.txtOrderId    ${order_id}
    Click Element    HelperPage.btnLookup
```

### 4.2 Synchronization

```robot
Wait For Application    app_id=helper    timeout=30
```

### 4.3 Screenshots per App

```robot
Capture Screenshot    app_id=main    filename=main.png
Capture Screenshot    app_id=helper    filename=helper.png
```

## Implementation Status

### Phase 1: Framework Core (Python) — COMPLETE

| Task | Status | Files Modified |
|------|--------|----------------|
| 1.1 App Context Model | ✅ Complete | `api/app_context.py` (new) |
| 1.2 Multi-App Registry | ✅ Complete | `api/app_context.py` |
| 1.3 Keyword Changes | ✅ Complete | `api/DriverAgnosticApi.py` |
| 1.4 Element Repository | ✅ Complete | `api/repository_access.py` |
| 1.5 Driver Changes | ✅ Complete | `drivers_rf/flaui_robotframework/flaui_driver.py` |

**Key features:**
- `AppContext` and `MultiAppContext` classes for per-app driver/process management
- New keywords: `Register Application`, `Switch Application`, `Launch Application`, `Attach To Application`, `Close Application`, `Get Application List`, `Wait For Application`, `Capture Screenshot`
- All existing keywords accept optional `app_id` parameter
- Backward-compatible legacy mode when no apps are registered
- Element repository supports optional `appId` field for app-scoped elements
- FlaUI driver attaches by PID via `AutomationElement.FromHandle`

### Phase 2: IDE Support (C# WPF) — COMPLETE

| Task | Status | Files Modified |
|------|--------|----------------|
| 2.1 IDE Multi-App Foundation | ✅ Complete | `WpfTestIde/Models/AppContext.cs` (new), `MainViewModel.cs`, `AttachToProcessDialog.xaml/.cs` |
| 2.2 Spy Tool Multi-App Mode | ✅ Complete | `SpyToolDialog.xaml/.cs` |
| 2.3 Recording per App | ✅ Complete | `RecordingSession.cs`, `RecordedStep.cs`, `MainViewModel.cs`, `ScriptGenerator.cs`, `TestFlowViewModel.cs` |
| 2.4 Element Repo per App | ✅ Complete | `RepositoryLookup.cs` |
| 2.5 Multi-App Management UI | ✅ Complete | `MultiAppDialog.xaml/.cs` (new), `MainWindow.xaml` |

**Key features:**
- C# `AppContext` model mirroring Python's `AppContext`
- `MainViewModel.AttachedApps` collection with `SelectedApp`
- `AttachToProcessDialog` returns `AppId` (auto-generated or user-specified)
- `SpyToolDialog` has `AppSelector` ComboBox for switching between attached apps
- `RecordingSession` tags steps with `AppId`
- `ScriptGenerator` emits `app_id=` per step (not global)
- `RepositoryLookup` filters elements by `appId` during YAML loading
- `MultiAppDialog` for managing attached apps (Detach/Set Default)
- IDE passes app registration env vars (`WPFSPY_APP_ID`, `WPFSPY_PROCESS_ID`, etc.) to Python

### Phase 3: Robot Test Support — COMPLETE

| Task | Status | Notes |
|------|--------|-------|
| 3.1 Robot Keywords | ✅ Complete | Implemented in Phase 1: `Register Application`, `Switch Application`, `Launch Application`, `Attach To Application`, `Close Application`, `Get Application List` |
| 3.2 Implicit Switching | ⏳ Deferred | Explicit `app_id` preferred for clarity; framework supports it via element `appId` field |

### Phase 4: Cross-App Workflows — COMPLETE

| Task | Status | Implementation |
|------|--------|----------------|
| 4.1 Data Passing | ✅ Complete | Works via existing Robot Framework variables + `Switch Application` keyword |
| 4.2 Sync Keywords | ✅ Complete | `Wait For Application    app_id=helper    timeout=30` |
| 4.3 Screenshots per App | ✅ Complete | `Capture Screenshot    app_id=main    filename=main.png` |

## Constraints and Considerations

### WPFSpy Limitations
- Only works with WPF applications
- Requires spy agent injection via startup hook
- One app per named pipe
- Cannot automate non-WPF apps

### FlaUI Advantages
- Works with any Windows application via UI Automation
- Can handle multiple apps simultaneously
- No injection required
- Better for cross-technology scenarios

### Sikuli Considerations
- Image-based, works with any visible app
- Multi-monitor support needed
- Slower than UIA-based drivers
- Good fallback for non-UIA-exposed controls

## Migration Path

1. **Backward compatibility**: All existing keywords work without `app_id`
2. **Default app**: First registered app becomes default automatically
3. **Gradual adoption**: Existing tests continue to work; multi-app features opt-in
4. **IDE enhancement**: Add multi-app features without breaking single-app workflow
5. **IDE-generated scripts**: Generated tests include `Attach To Application` Test Setup when multi-app attach is used
6. **Per-step app context**: Recorded steps carry `AppId` so generated scripts work correctly in multi-app scenarios
