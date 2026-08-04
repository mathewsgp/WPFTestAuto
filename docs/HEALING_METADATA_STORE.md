# Locator Healing Metadata Store — Phase 1 Implementation

## Overview

Phase 1 implements a **Locator Healing Metadata Store** that captures element interaction data during test execution and enables **post-run repository updates** when UI changes cause test failures.

This is the foundation for reducing test rework when application UIs change between versions. Unlike commercial tools with AI-based healing, Phase 1 focuses on:

1. **Capturing baseline properties** during successful element interactions
2. **Tracking healing attempts** when driver fallback occurs
3. **Analyzing patterns** to suggest repository updates
4. **Applying updates** to add alternative strategies where needed

---

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                     Test Execution                           │
│  ┌───────────────────────────────────────────────────────┐  │
│  │         DriverAgnosticApi._resolve_and_execute()      │  │
│  │  ┌─────────────────────────────────────────────────┐  │  │
│  │  │  Phase 1 Enhancement                            │  │  │
│  │  │  • Capture baseline on success                  │  │  │
│  │  │  • Record healing on driver fallback            │  │  │
│  │  │  • Track strategy success/failure stats         │  │  │
│  │  └─────────────────────────────────────────────────┘  │  │
│  └───────────────────────────────────────────────────────┘  │
│                            │                               │
│                            ▼                               │
│  ┌───────────────────────────────────────────────────────┐  │
│  │           HealingMetadataStore (JSON files)           │  │
│  │  • Element baselines                                  │  │
│  │  • Healing history                                   │  │
│  │  • Strategy statistics                               │  │
│  └───────────────────────────────────────────────────────┘  │
│                            │                               │
│                            ▼                               │
│  ┌───────────────────────────────────────────────────────┐  │
│  │                   CLI Tool (healing_cli.py)           │  │
│  │  • --status       Show health of tracked elements    │  │
│  │  • --suggestions  Generate repository update ideas   │  │
│  │  • --apply        Apply updates to YAML files       │  │
│  │  • --report       Export healing report              │  │
│  └───────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────┘
```

---

## Components

### 1. healing_metadata_store.py

Core module providing the `HealingMetadataStore` class.

**Key Classes:**

| Class | Purpose |
|-------|---------|
| `ElementBaseline` | Stores properties captured during successful interactions |
| `HealingAttempt` | Records when primary strategy fails but fallback succeeds |
| `StrategyStats` | Tracks success/failure rates per strategy |
| `ElementMetadata` | Complete metadata for an element |
| `HealingMetadataStore` | Manages all healing metadata |

**Key Methods:**

| Method | Purpose |
|--------|---------|
| `capture_baseline()` | Store element properties on successful interaction |
| `record_healing()` | Log when driver fallback is used |
| `record_strategy_attempt()` | Track success/failure for statistics |
| `generate_update_suggestions()` | Analyze data and suggest repository changes |
| `apply_updates()` | Modify YAML files based on suggestions |
| `get_element_health()` | Get stability metrics for an element |
| `export_healing_report()` | Generate JSON report of all healing data |

**Storage Location:** `repository/healing_metadata/`

### 2. healing_cli.py

Command-line tool for managing healing metadata and applying updates.

---

## How It Works

### Baseline Capture

Every time an element is successfully interacted with, the following properties are captured:

```python
{
    "alias": "LoginPage.btnSubmit",
    "automation_id": "btnSubmit",
    "name": "Submit",
    "control_type": "Button",
    "xpath": "/Window/Button[@AutomationId='btnSubmit']",
    "text": "Submit",
    "position": {"x": 100, "y": 200, "width": 80, "height": 30},
    "is_visible": True,
    "is_enabled": True,
    "driver_used": "FlaUI",
    "search_method": "AutomationId",
    "search_value": "btnSubmit",
    "captured_at": "2025-01-15T10:30:00",
    "verification_count": 1
}
```

### Healing Tracking

When the primary driver fails but a fallback succeeds:

```
Scenario: App upgrade changes btnSubmit's AutomationId to "btnLogin"

1. Primary attempt: FlaUI:AutomationId='btnSubmit' → FAILED
2. Fallback attempt: WPFSpy:XPath='/Window/Button[@Name="Submit"]' → SUCCESS
3. Healing recorded:
   - Primary: (FlaUI, AutomationId, btnSubmit) - Element not found
   - Healing: (WPFSpy, XPath, /Window/Button[@Name="Submit"]) - SUCCESS
```

### Post-Run Analysis

After test execution, run:

```bash
python api/healing_cli.py --suggestions
```

This analyzes healing history and suggests repository updates:

```
📝 Add Strategy (1 suggestion(s))
──────────────────────────────────────────────────
  Element: LoginPage.btnSubmit
  Reason: Element has healed successfully 3 times using WPFSpy:XPath
  Confidence: 60%
  Action: Add WPFSpy strategy
    searchBy: XPath
    value: /Window/Button[@Name='Submit']
    priority: 2
```

---

## Usage

### Run Tests (Automatic Metadata Collection)

Tests run normally. Metadata is captured automatically:

```bash
python3 -m robot tests/order_tests.robot
```

During execution:
- ✅ Successful interactions → Baseline captured
- 🔄 Driver fallback → Healing recorded
- ❌ Failed attempts → Stats updated

### Check Element Health

```bash
# Show overall status
python api/healing_cli.py --status

# Show specific element details
python api/healing_cli.py --element LoginPage.btnSubmit
```

### Generate Update Suggestions

```bash
# Generate suggestions (minimum 2 healing successes)
python api/healing_cli.py --suggestions

# Lower threshold to 1 healing success
python api/healing_cli.py --suggestions --min-heals 1
```

### Apply Updates

```bash
# Preview what would change (default)
python api/healing_cli.py --apply

# Actually apply changes
python api/healing_cli.py --apply --no-dry-run
```

### Export Report

```bash
# Export to JSON file
python api/healing_cli.py --report healing_report.json

# Print to stdout
python api/healing_cli.py --report
```

### Clear Metadata

```bash
# Clear specific element
python api/healing_cli.py --clear LoginPage.btnSubmit

# Clear all metadata
python api/healing_cli.py --clear-all
```

---

## Output Files

### Stored Metadata

Files are stored in `repository/healing_metadata/`:

```
repository/healing_metadata/
├── LoginPage/
│   ├── btnSubmit.json      # Element baseline and healing history
│   ├── txtUsername.json
│   └── txtPassword.json
├── OrdersPage/
│   ├── btnCreateOrder.json
│   └── gridOrders.json
└── _config.json           # Store configuration
```

### Example healing_metadata/LoginPage/btnSubmit.json:

```json
{
  "alias": "LoginPage.btnSubmit",
  "baseline": {
    "automation_id": "btnSubmit",
    "name": "Submit",
    "control_type": "Button",
    "driver_used": "FlaUI",
    "search_method": "AutomationId",
    "captured_at": "2025-01-15T10:30:00"
  },
  "healing_history": [
    {
      "timestamp": "2025-01-20T14:00:00",
      "primary_driver": "FlaUI",
      "primary_search_method": "AutomationId",
      "primary_search_value": "btnSubmit",
      "failure_reason": "Element not found",
      "healing_driver": "WPFSpy",
      "healing_search_method": "XPath",
      "healing_search_value": "/Window/Button[@Name='Submit']",
      "healing_successful": true
    }
  ],
  "strategy_stats": {
    "FlaUI:AutomationId": {
      "success_count": 45,
      "failure_count": 1,
      "success_rate": 0.978
    },
    "WPFSpy:XPath": {
      "success_count": 1,
      "failure_count": 0,
      "success_rate": 1.0
    }
  },
  "total_interactions": 46,
  "first_seen": "2025-01-15T10:30:00",
  "last_interaction": "2025-01-20T14:00:00"
}
```

---

## Workflow Example

### Scenario: Application Upgrade Changes UI

**Before Upgrade:**
```
repository/elements/login_page.yaml:
elements:
  LoginPage.btnSubmit:
    strategies:
      FlaUI:
        - searchBy: AutomationId
          value: btnSubmit
          priority: 1
```

**After Upgrade:** Button's AutomationId changed to "btnLogin"

**Test Run After Upgrade:**
```
1. Click Element  LoginPage.btnSubmit
   → FlaUI:AutomationId='btnSubmit' FAILED
   → WPFSpy:XPath tried... SUCCESS
   
   [Healing] Element healed via WPFSpy:XPath
   ✓ Baseline captured
   ✓ Healing recorded
```

**Post-Run Analysis:**
```bash
$ python api/healing_cli.py --suggestions
Found 1 suggestion(s):

  Element: LoginPage.btnSubmit
  Reason: Element has healed successfully 1 times using WPFSpy:XPath
  Confidence: 20%
  Action: Add WPFSpy strategy
    searchBy: XPath
    value: /Window/Button[@Name='Submit']
    priority: 2
```

**Apply Update:**
```bash
$ python api/healing_cli.py --apply --no-dry-run
Applying Repository Updates

Changes were applied:
  Applied: 1
  Backups created: 1
    - repository/elements/login_page_backup_1705760400.yaml

Applied changes:
  - Added WPFSpy strategy for LoginPage.btnSubmit
```

**After Update:**
```yaml
elements:
  LoginPage.btnSubmit:
    strategies:
      FlaUI:
        - searchBy: AutomationId
          value: btnSubmit      # Kept for future compatibility
          priority: 1
      WPFSpy:
        - searchBy: XPath
          value: /Window/Button[@Name='Submit']
          priority: 2          # Fallback when AutomationId changes
```

**Future:** Tests will try FlaUI first, fall back to WPFSpy if needed — no test rewrites required!

---

## Benefits

| Before Phase 1 | After Phase 1 |
|----------------|---------------|
| Tests break when AutomationId changes | Tests heal automatically with WPFSpy fallback |
| Manual test updates for every UI change | Post-run suggestions + auto-update |
| No visibility into which elements are unstable | Health metrics and confidence scores |
| Driver fallback is invisible | Full healing history tracked |

---

## Limitations (Phase 1)

Phase 1 does NOT include:

- ❌ AI/ML-based similarity scoring (Phase 2)
- ❌ Visual-based element matching
- ❌ OCR for complex controls
- ❌ Cross-version learning

Phase 1 provides the **data foundation** for future AI-based healing.

---

## Configuration

### Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `HEALING_METADATA_DIR` | `repository/healing_metadata` | Custom metadata storage path |

### Enable/Disable

Healing metadata capture is **automatic** and **non-intrusive**. To disable:

```python
# In your test setup
import os
os.environ["HEALING_ENABLED"] = "false"
```

---

## Integration with Robot Framework

The healing store is automatically integrated into `DriverAgnosticApi`. No changes to test scripts needed.

**New RF Keywords (future phases):**

```robotframework
*** Test Cases ***
Example Test
    # Existing keywords work as before
    Click Element    LoginPage.btnSubmit
    
    # Future: New keywords for healing management
    ${health}=    Get Element Health    LoginPage.btnSubmit
    IF    ${health.status} == "degraded"
        Log    Warning: Element is unstable
    END
```

---

## Next Steps (Phase 2+)

1. **Vision AI Integration**
   - Visual-based element matching when properties fail
   - Screenshot comparison for custom controls

2. **Smart Suggestion Engine**
   - Analyze patterns across multiple elements
   - Suggest structural changes (e.g., "all buttons in header changed")

3. **Automatic Strategy Promotion**
   - Auto-elevate healing strategies to primary when consistent
   - Track healing confidence over time

---

## Troubleshooting

### Metadata Not Being Captured

1. Check metadata directory exists:
   ```bash
   ls -la repository/healing_metadata/
   ```

2. Verify write permissions:
   ```bash
   touch repository/healing_metadata/test.json
   ```

3. Check logs for healing store errors:
   ```bash
   python api/healing_cli.py --status 2>&1
   ```

### Suggestions Not Generated

- Minimum healing count required (default: 2)
- Lower threshold: `python api/healing_cli.py --suggestions --min-heals 1`
- Check healing history: `python api/healing_cli.py --element <alias>`

### Apply Fails

- Check backup was created: `repository/elements/*_backup_*.yaml`
- Review error message from CLI
- Manual intervention: Edit YAML files in `repository/elements/`

---

## API Reference

### HealingMetadataStore

```python
from healing_metadata_store import HealingMetadataStore

store = HealingMetadataStore()

# Capture baseline
store.capture_baseline(
    alias="Page.element",
    properties={"automation_id": "id", "name": "Name"},
    driver="FlaUI",
    search_method="AutomationId",
    search_value="id"
)

# Record healing
store.record_healing(
    alias="Page.element",
    primary_driver="FlaUI",
    primary_search_method="AutomationId",
    primary_search_value="old_id",
    failure_reason="Element not found",
    healing_driver="WPFSpy",
    healing_search_method="XPath",
    healing_search_value="//Button[@Name='Submit']",
    healing_successful=True,
    new_properties={"automation_id": None, "xpath": "//Button[@Name='Submit']"}
)

# Generate suggestions
suggestions = store.generate_update_suggestions(min_healing_count=2)

# Apply suggestions
results = store.apply_updates(suggestions, dry_run=True)

# Get health
health = store.get_element_health("Page.element")
```

---

*Phase 1 of the Self-Healing Locator Enhancement Project*
