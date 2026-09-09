# DriverAgnosticApi Refactoring Plan
## Phase-wise Implementation

---

## Phase 0: Foundation & Safety (Week 1)
**Goal**: Establish safety net before structural changes

### 0.1 Add Contract Tests for Driver Interface
- **File**: `tests/contract/test_driver_contract.py`
- Verify all 3 drivers implement `BaseDriver` abstract methods with matching signatures
- Run in CI to prevent interface drift

### 0.2 Add Integration Test Suite for `_resolve_and_execute`
- **File**: `tests/integration/test_resolution_fallback.py`
- Test multi-driver fallback, circuit breaker, healing metadata recording
- Test legacy vs multi-app code paths
- Mock drivers for deterministic tests

### 0.3 Introduce Typed Configuration (Pydantic)
- **File**: `TestAutoLayer/api/config.py` (replace `FrameworkConfig`)
- Use `pydantic-settings` with `.env` support
- Validate all env vars at startup
- Backward compatible: same attribute names

### 0.4 Repository Schema Validation
- **File**: `TestAutoLayer/api/repository_access.py`
- Add Pydantic models for `ElementEntry`, `ElementStrategy`
- Validate on load; fail fast on malformed YAML
- Add `hasAutomationId` field to model

---

## Phase 1: Core Architecture - Split God Class (Week 2-3)
**Goal**: Decompose `DriverAgnosticApi` into single-responsibility services

### 1.1 Extract Strategy Executor
- **New**: `TestAutoLayer/api/resolution/strategy_executor.py`
- Single implementation for both multi-app and legacy modes
- Pluggable `driver_provider` and `strategy_provider` callables
- Handles: circuit breaker, healing tracking, attempt logging, screenshot on failure

### 1.2 Extract Element Resolver with Caching
- **New**: `TestAutoLayer/api/resolution/element_resolver.py`
- `_resolve_strategy_with_parent()` + `_build_full_path_from_alias()`
- **Cache**: `resolved_xpath_cache: Dict[Tuple[alias, app_id, driver], str]`
- Invalidate on `reset_application()` or `load_elements(force_reload=True)`

### 1.3 Extract Healing Tracker
- **New**: `TestAutoLayer/api/resolution/healing_tracker.py`
- `record_attempt()`, `record_healing()`, `capture_baseline()`
- Decoupled from execution logic
- Unit-testable with fake healing store

### 1.4 Extract App Management Services
- **New**: `TestAutoLayer/api/app_management/`
  - `app_registry.py` — `MultiAppContext`, `AppContext` (move from `app_context.py`)
  - `app_launcher.py` — `launch_application`, `attach_to_application`, `_launch_app_for_context`
  - `window_activator.py` — `activate_window`, Win32 helpers
  - `app_terminator.py` — `terminate_application`, `_kill_pid`, `_find_pids_by_title_or_name`

### 1.5 Extract Keyword Groups (Facade Pattern)
- **New**: `TestAutoLayer/api/keywords/`
  - `interaction_keywords.py` — `click_element`, `set_element_value`, `toggle_element`, `drag_and_drop`, `double_click_element`, `right_click_element`, `press_keys`, `hover_over_element`, `scroll`, `sikuli_click`, `sikuli_type`
  - `verification_keywords.py` — `verify_element_text`, `verify_element_contains_text`, `verify_element_enabled`, `verify_element_visible`, `verify_element_text_matches_regex`, `verify_element_attribute`, `property_checkpoint`, `data_grid_checkpoint`, `count_checkpoint`, `attribute_checkpoint`, `area_checkpoint`, `image_checkpoint`
  - `wait_keywords.py` — `wait_until_element_exists`, `wait_until_element_visible`, `wait_until_element_enabled`, `wait_until_element_actionable`, `wait_until_element_text_contains`, `click_element_with_wait`, `set_element_value_with_wait`
  - `utility_keywords.py` — `set_driver`, `reset_drivers`, `set_mode`, `reset_mode`, `set_mode_and_driver`, `get_element_text`, `get_data_grid_content_ocr`, `capture_screenshot`, `copy_element_text`, `paste_clipboard_to_element`, `send_keys_to_window`, `set_clipboard_text`, `get_clipboard_text`, `is_pipe_ready`, `get_last_strategy_used`, `reset_application`, `register_application`, `switch_application`, `close_application`, `launch_application`, `attach_to_application`, `get_application_list`, `set_default_application`, `get_current_application`, `wait_for_application`, `activate_window`

### 1.6 Thin Facade: `DriverAgnosticApi` (New)
- **File**: `TestAutoLayer/api/driver_agnostic_api.py` (replace current)
- ~200 lines: delegates to keyword services + strategy executor
- Maintains exact same Robot Framework keyword surface
- `ROBOT_LIBRARY_SCOPE = "GLOBAL"`

---

## Phase 2: Reliability & Performance (Week 4)
**Goal**: Eliminate flakiness, improve speed

### 2.1 Standardized ElementHandle with Staleness Detection
- **Modify**: `TestAutoLayer/api/base_driver.py`
- `ElementHandle` becomes mandatory return type for `find_element`/`find_elements`
- Add `refresh(driver)` method that re-resolves if `is_stale()`
- All drivers return `ElementHandle(locator, driver_name, xpath_or_handle, found_at=time.time())`

### 2.2 Exponential Backoff with Jitter
- **New**: `TestAutoLayer/api/retry_policy.py`
- `RetryPolicy` dataclass: `max_attempts`, `base_delay`, `max_delay`, `exponential_base`, `jitter`
- `retry_with_backoff(func, policy)` helper
- Apply to: `is_element_visible` retries, WPFSpy real driver retries, wait keywords
- Configurable via `FrameworkConfig.RETRY_POLICY`

### 2.3 Strategy Resolution Caching (Integration)
- Wire `ElementResolver` cache into `StrategyExecutor`
- Cache key: `(alias, app_id or "default", driver_name)`
- Invalidate on: `reset_application()`, repository reload, app context switch

### 2.4 Circuit Breaker Enhancements
- Add metrics export: `failure_rate`, `state_changes`, `rejected_calls`
- Add `half_open_max_calls` config per driver
- Log state transitions with structured logging

---

## Phase 3: Observability & Developer Experience (Week 5)
**Goal**: Production-grade debugging, faster iteration

### 3.1 Structured Logging with Correlation IDs
- **Modify**: `TestAutoLayer/api/logging_utils.py`
- Use `structlog` with `correlation_id` per `_resolve_and_execute` call
- Log: `driver`, `strategy`, `attempt_number`, `duration_ms`, `success`, `error_type`
- JSON output option for log aggregation

### 3.2 OpenTelemetry Integration (Optional)
- **New**: `TestAutoLayer/api/telemetry.py`
- Spans for: `resolve_and_execute`, `find_element`, `driver_call`
- Attributes: `alias`, `driver`, `strategy`, `app_id`
- Export to console/Jaeger/OTLP via env var

### 3.3 Health Check / Diagnostics Keywords
- **Add to** `utility_keywords.py`:
  - `Get Driver Status` — circuit breaker states, driver availability
  - `Get Healing Stats` — success rates per alias/driver
  - `Get Cache Stats` — resolver cache hit/miss
  - `Dump App Contexts` — registered apps, PIDs, pipes

### 3.4 IDE Support: Type Stubs for Keywords
- **Generate**: `TestAutoLayer/api/driver_agnostic_api.pyi`
- Full type hints for all 50+ public keywords
- Enables autocomplete in Robot Framework Language Server

---

## Phase 4: Security & Modernization (Week 6)
**Goal**: Harden, remove legacy risks

### 4.1 Remove `shell=True` in `send_keys_to_window`
- **Modify**: `utility_keywords.py`
- Replace PowerShell `WScript.Shell` with `comtypes` or `pywinauto`
- No string interpolation in subprocess calls

### 4.2 Async Variants for I/O Operations
- **New**: `TestAutoLayer/api/async_keywords.py`
- `click_element_async`, `set_element_value_async`, `wait_until_element_actionable_async`
- Use `asyncio` + `async` drivers (WPFSpy pipe, FlaUI COM, Sikuli)
- Keep sync API for Robot Framework compatibility

### 4.3 Dependency Audit & Minimization
- Remove unused imports (e.g., `signal` in `DriverAgnosticApi.py`)
- Pin transitive dependencies in `requirements.txt`
- Add `pip-audit` to CI

---

## Phase 5: Testing & Documentation (Week 7)
**Goal**: Validate, document, ship

### 5.1 End-to-End Test Suite
- **File**: `tests/e2e/test_multi_app_scenarios.py`
- Multi-app launch/attach/switch/terminate
- Driver fallback under simulated failures
- Healing metadata verification

### 5.2 Performance Benchmarks
- **Script**: `benchmarks/resolution_benchmark.py`
- Measure: cold vs cached resolution, fallback latency, memory
- Target: <50ms cached resolve, <200ms cold resolve

### 5.3 Architecture Decision Records (ADRs)
- **Dir**: `docs/adr/`
- ADR-001: Driver-Agnostic Layer Design
- ADR-002: Multi-App Context Management
- ADR-003: Self-Healing Strategy Fallback
- ADR-004: Circuit Breaker Policy

### 5.4 Migration Guide
- **File**: `docs/MIGRATION_GUIDE.md`
- Breaking changes (if any)
- New keyword references
- Configuration migration

---

## Dependency Graph

```
Phase 0 (Foundation)
    │
    ├── 0.1 Contract Tests ────────────────────────┐
    ├── 0.2 Integration Tests ─────────────────────┤
    ├── 0.3 Typed Config ──────────────────────────┤
    └── 0.4 Repo Schema ───────────────────────────┤
                                                    ▼
Phase 1 (Architecture) ◄────────────────────────────┘
    │
    ├── 1.1 StrategyExecutor ──────────────────────┐
    ├── 1.2 ElementResolver (with cache) ──────────┤
    ├── 1.3 HealingTracker ────────────────────────┤
    ├── 1.4 App Management Services ───────────────┤
    ├── 1.5 Keyword Groups ────────────────────────┤
    └── 1.6 Thin Facade ───────────────────────────┤
                                                    ▼
Phase 2 (Reliability) ◄─────────────────────────────┘
    │
    ├── 2.1 ElementHandle + Staleness ─────────────┐
    ├── 2.2 Exponential Backoff ───────────────────┤
    ├── 2.3 Cache Integration ─────────────────────┤
    └── 2.4 Circuit Breaker Metrics ───────────────┤
                                                    ▼
Phase 3 (Observability) ◄──────────────────────────┘
    │
    ├── 3.1 Structured Logging ────────────────────┐
    ├── 3.2 OpenTelemetry (optional) ──────────────┤
    ├── 3.3 Diagnostics Keywords ──────────────────┤
    └── 3.4 Type Stubs ────────────────────────────┤
                                                    ▼
Phase 4 (Security) ◄────────────────────────────────┘
    │
    ├── 4.1 Remove shell=True ─────────────────────┐
    ├── 4.2 Async Variants ────────────────────────┤
    └── 4.3 Dependency Audit ──────────────────────┤
                                                    ▼
Phase 5 (Validation) ◄──────────────────────────────┘
```

---

## Rollback Strategy
Each phase:
1. Feature flag / branch per phase
2. Run full test suite (contract + integration + e2e)
3. Compare benchmark baselines
4. Merge only if: all tests pass, no performance regression >10%

---

## Success Criteria
| Metric | Target |
|--------|--------|
| Test coverage (new code) | ≥90% |
| Cold element resolve | <200ms p95 |
| Cached element resolve | <50ms p95 |
| Flaky test rate | <1% |
| Circuit breaker false positives | 0 |
| Healing metadata capture rate | 100% on fallback |

---

## Estimated Effort
| Phase | Days | Risk |
|-------|------|------|
| 0 | 5 | Low |
| 1 | 10 | Medium (large refactor) |
| 2 | 5 | Low |
| 3 | 5 | Low |
| 4 | 5 | Medium (async) |
| 5 | 5 | Low |
| **Total** | **35** | |

---

## Next Steps
1. Review and approve plan
2. Create feature branch: `refactor/driver-agnostic-api-phased`
3. Start Phase 0.1 (Contract Tests) — establishes safety net for all subsequent phases