# Recorder & Playback Guide

## The authoring workflow this implements

```
1. Record            Run the app manually with Recorder ON
2. Auto-generate      Converter creates raw Layer-3 script + repository entries
3. Add Verifications  Tester inserts verification keywords between action steps
4. Refactor           Repeating sequences promoted to Layer 2 reusable modules
5. Finalize           Clean Layer 1 script committed; repository entries reviewed
```

## What's real vs simulated here

Real UI Automation event hooking requires a live WPF process on Windows.
`recorder/recorder_engine.py` instead replays a **scripted list of
interactions** (`_scripted_interactions()`) standing in for "what a
tester actually clicked/typed", and produces the exact same three JSON
artifacts a real UIA-event recorder would. Everything downstream —
the Converter, the generated repository entries, the generated test
script — is exactly what you'd get from a real recording.

To make this a genuine recorder against a real WPF app, replace
`_scripted_interactions()` with a real UI Automation event subscriber
(FlaUI exposes `Automation.RegisterEventHandler` and friends for this).

## Step 1 & 2: Record + Auto-generate

```bash
python3 recorder/recorder_engine.py     # writes recorder/sample_recorded/*.json
python3 recorder/converter.py           # writes draft repository entries + test script
```

Output (see `recorder/example_draft_output/` for a checked-in example):

- `recorded_draft_elements.yaml` — one entry per recorded control, **FlaUI
  strategy only**. A human adds `WPFSpy`/`Sikuli` strategies later if the
  control turns out to need a fallback.
- `recorded_draft_steps.yaml` — step type inferred from the recorded
  action (`Invoke` → `InvokeStep`, `SetValue` → `ValueStep`).
- `draft_recorded_test.robot` — a Layer 1 script calling Layer 3
  (`Click Element` / `Set Element Value`) directly, in the exact recorded
  order. **This is intentionally raw** — no verifications, no reuse.

This draft script is fully runnable as-is:

```bash
python3 -m robot recorder/example_draft_output/draft_recorded_test.robot
```

## Step 3: Add Verifications

Open the generated `.robot` file and insert calls to existing (or new)
Layer 2 verification keywords between action steps, e.g. after the
generated `Click Element    OrdersPage.btnCreateOrder` line, add:

```robotframework
    Verify Order Confirmation Displayed    SKU-2002    3
```

## Step 4: Refactor to Reusable Modules

Replace repeated raw Layer 3 calls with existing Layer 2 keywords where
they match (e.g. the generated username/password/submit sequence becomes
a single `Login To Application    user1    Pass@123` call), or promote a
new repeating sequence into a new Layer 2 keyword in `modules/`.

## Step 5: Finalize

- Move the cleaned-up script from wherever it was drafted into `tests/`.
- Merge the draft repository YAML into the real
  `repository/elements/<page>.yaml` / `repository/steps/steps.yaml` files
  (rename aliases to the project's convention if the auto-generated ones
  were too literal, e.g. `OrdersPage.txtQty` → `OrdersPage.QuantityTextBox`
  to match the existing style).
- Run `./run_tests.sh` to confirm everything still passes.

## Regenerating the example output

`recorder/example_draft_output/` is checked in as a worked example. To
regenerate it from scratch:

```bash
rm -f recorder/example_draft_output/*.yaml recorder/example_draft_output/*.robot
python3 recorder/recorder_engine.py
python3 recorder/converter.py
mv repository/elements/recorded_draft_elements.yaml recorder/example_draft_output/
mv repository/steps/recorded_draft_steps.yaml recorder/example_draft_output/
mv tests/draft_recorded_test.robot recorder/example_draft_output/
```

(The converter writes directly into the live `repository/` and `tests/`
folders by design — that's where a real draft belongs during Steps 3–4 —
so the commands above move the freshly generated files back into the
example folder afterward.)
