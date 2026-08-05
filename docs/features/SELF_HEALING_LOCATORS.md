# Runtime Self-Healing Locators

## Design-time vs runtime

The Element Repository's `strategies` block is a **design-time** choice —
which drivers *can* find a given control at all. Runtime self-healing is
a separate, additional safety net: if the strategy that would normally
win **fails at execution time** (element not found, stale reference,
timing issue), the framework automatically retries the next configured
strategy **before failing the step** — rather than failing immediately on
the first miss.

## Implementation

`api/DriverAgnosticApi._resolve_and_execute` (used by every Layer 3
keyword) does the following for every call:

```python
for driver_name, locator in repository_access.get_strategies(alias).items():
    try:
        element = driver.find_element(locator)
        return driver.<action>(element, ...)   # success — stop here
    except (ElementNotFoundError, ElementNotInteractableError, KeyError) as exc:
        log(f"strategy '{driver_name}' failed: {exc} — trying next")
        continue
raise AllStrategiesFailedError(full_attempt_log)
```

Strategy order is fixed at `FlaUI → WPFSpy → Sikuli` (matching the
architecture's driver-selection preference — fastest/most-standard
first, image-matching last resort), skipping any driver not configured
for that alias.

## Full diagnostics on failure

If *every* configured strategy fails, the raised
`AllStrategiesFailedError` carries the complete attempt log, e.g.:

```
All configured strategies failed for alias 'OrdersPage.PriorityCheckbox'.
Attempts: [('FlaUI', "FAILED: ..."), ('WPFSpy', "FAILED: ..."), ('Sikuli', "FAILED: ...")]
```

— not just the first attempt, so failures are diagnosable without
re-running with extra logging enabled.

## Live demo in this repository

`repository/elements/orders_page.yaml`'s `OrdersPage.PriorityCheckbox`
entry deliberately points its `FlaUI` strategy at an AutomationId the
mock application does **not** expose (simulating a custom-rendered
control not properly surfaced via UI Automation), while its `WPFSpy`
strategy correctly matches the control's `Name`.

`tests/self_healing_locators_demo.robot` exercises this and asserts the
fallback actually happened:

```robotframework
Toggle Order Priority
${strategy}=    Get Last Strategy Used
Should Be Equal As Strings    ${strategy}    WPFSpy
```

Run it directly to see the fallback log live:

```bash
python3 -m robot tests/self_healing_locators_demo.robot
```

```
[Self-Healing] 'OrdersPage.PriorityCheckbox': strategy 'FlaUI' failed
  (FlaUI: no element with AutomationId='chkPriority_NOT_EXPOSED') —
  trying next configured strategy if any
[Self-Healing] 'OrdersPage.PriorityCheckbox': primary strategy failed,
  succeeded via fallback strategy 'WPFSpy'. Attempts: [...]
```

## Extending this

- **Retry count / backoff for transient failures** (vs immediately
  falling to the next *driver*): not yet implemented — currently a
  failure moves straight to the next configured strategy. Add a
  short retry-with-delay loop inside `_resolve_and_execute` per driver
  if flaky timing (not a genuinely wrong locator) turns out to be the
  dominant failure mode in your application.
- **Centralized reporting** of self-healing events across a whole run
  (today it's per-test console/log output only) is listed as a
  Suggested Design Improvement — see the architecture deck's
  "Suggested Design Improvements" slide.
