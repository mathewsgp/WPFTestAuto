# Self-Healing Locators

## Overview

Self-healing locators automatically recover from element location failures by trying alternative strategies. This reduces test maintenance when UIs change.

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│               DriverAgnosticApi._resolve_and_execute()       │
│  ┌─────────────────────────────────────────────────────┐  │
│  │  • Try each configured strategy in priority order    │  │
│  │  • Capture baseline on success                       │  │
│  │  • Record healing on driver fallback                  │  │
│  │  • Track strategy success/failure stats               │  │
│  └─────────────────────────────────────────────────────┘  │
│                            │                               │
│                            ▼                               │
│  ┌───────────────────────────────────────────────────────┐  │
│  │         HealingMetadataStore (JSON files)             │  │
│  │  • Element baselines                                  │  │
│  │  • Healing history                                   │  │
│  │  • Strategy statistics                               │  │
│  └───────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────┘
```

---

## Runtime Self-Healing

### How It Works

When a keyword executes (`Click Element`, `Input Text`, etc.):

```python
for driver_name, locator in repository_access.get_strategies(alias).items():
    try:
        element = driver.find_element(locator)
        return driver.<action>(element, ...)   # success
    except (ElementNotFoundError, ElementNotInteractableError) as exc:
        log(f"strategy '{driver_name}' failed: {exc} — trying next")
        continue
raise AllStrategiesFailedError(full_attempt_log)
```

### Strategy Order

Strategies are tried in order:
1. **FlaUI** — Fastest, standard WPF controls
2. **WPFSpy** — Custom controls, non-standard elements
3. **Sikuli** — Image-based fallback (last resort)

### Failure Output

If all strategies fail:

```
All configured strategies failed for alias 'OrdersPage.PriorityCheckbox'.
Attempts: [('FlaUI', "FAILED: ..."), ('WPFSpy', "FAILED: ..."), ('Sikuli', "FAILED: ...")]
```

---

## Healing Metadata Store

### Purpose

Captures element interaction data to enable post-run repository updates when UIs change.

### Key Classes

| Class | Purpose |
|-------|---------|
| `ElementBaseline` | Properties captured during successful interactions |
| `HealingAttempt` | When primary strategy fails but fallback succeeds |
| `StrategyStats` | Success/failure rates per strategy |
| `HealingMetadataStore` | Manages all healing metadata |

### Key Methods

| Method | Purpose |
|--------|---------|
| `capture_baseline()` | Store element properties on success |
| `record_healing()` | Log when driver fallback is used |
| `record_strategy_attempt()` | Track success/failure for statistics |
| `generate_update_suggestions()` | Suggest repository changes |
| `apply_updates()` | Modify YAML files based on suggestions |
| `get_element_health()` | Get stability metrics |
| `export_healing_report()` | Generate JSON report |

### Storage Location

`repository/healing_metadata/`

---

## CLI Tool

```bash
# Show health of tracked elements
python -m api.healing_metadata_store --status

# Generate repository update suggestions
python -m api.healing_metadata_store --suggestions

# Apply updates to YAML files
python -m api.healing_metadata_store --apply

# Export healing report
python -m api.healing_metadata_store --report
```

---

## Demo

`tests/self_healing_locators_demo.robot` demonstrates the feature:

```robotframework
Toggle Order Priority
${strategy}=    Get Last Strategy Used
Should Be Equal As Strings    ${strategy}    WPFSpy
```

Run it:

```bash
python3 -m robot tests/self_healing_locators_demo.robot
```

Expected output:

```
[Self-Healing] 'OrdersPage.PriorityCheckbox': strategy 'FlaUI' failed
  (FlaUI: no element with AutomationId='chkPriority_NOT_EXPOSED') —
  trying next configured strategy if any
[Self-Healing] 'OrdersPage.PriorityCheckbox': primary strategy failed,
  succeeded via fallback strategy 'WPFSpy'. Attempts: [...]
```

---

## Configuration

### Element Repository Entry

```yaml
elements:
  PageName.elementName:
    automationId: elementId
    controlType: Button
    strategies:
      - searchBy: AutomationId
        value: elementId
        priority: 1
        driver: FlaUI
      - searchBy: XPath
        value: "//Button[@Name='Submit']"
        priority: 2
        driver: WPFSpy
      - searchBy: Image
        value: button_submit.png
        priority: 3
        driver: Sikuli
```

---

## Extending

- **Retry count / backoff** for transient failures
- **Machine learning** for predictive healing
- **Visual similarity** scoring for AI-based matching

---

## See Also

- [Locator Strategies](./LOCATOR_STRATEGIES.md)
- [Wild-Card XPath](./WILDCARD_XPATH.md)
- [Healing Metadata CLI](../api/healing_metadata_store.py)
