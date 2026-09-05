# Recorder & Playback Guide

## The authoring workflow this implements

```
1. Record            Run the app manually with Recorder ON
2. Auto-generate      Converter creates raw Layer-3 script + repository entries
3. Add Verifications  Tester inserts verification keywords between action steps
4. Refactor           Repeating sequences promoted to Layer 2 reusable modules
5. Finalize           Clean Layer 1 script committed; repository entries reviewed
```

## Recording Modes

### Mock Mode (Current Default)
The recorder replays a **scripted list of interactions** against the mock app.
Use this for testing and development without a Windows/WPF environment.

```bash
python3 src/python/recorder/recorder_engine.py     # Mock mode
```

### Live Mode (Windows + WPF Required)
For real WPF applications, use the LiveRecorder to capture actual UI Automation events:

```bash
python3 src/python/recorder/recorder_engine.py --live
```

Or use the LiveRecorder class directly:

```python
from recorder.live_recorder import LiveRecorder, RecordingContext

# Simple usage
recorder = LiveRecorder(mode="real")
recorder.start_recording()
# ... interact with WPF app ...
recorder.stop_recording()
events = recorder.get_recorded_events()
recorder.export_to_json()

# Context manager usage
with RecordingContext(mode="real", output_dir="recording") as recorder:
    # Recording starts automatically
    # ... interact with WPF app ...
# Recording stops and exports automatically
```

## Step 1 & 2: Record + Auto-generate

```bash
# Mock mode (simulated)
python3 src/python/recorder/recorder_engine.py

# Live mode (real WPF app with Spy Agent)
python3 src/python/recorder/recorder_engine.py --live

# Interactive mode
python3 src/python/recorder/recorder_engine.py --interactive

# Then run converter
python3 src/python/recorder/converter.py
```

### What Gets Recorded

The live recorder captures:
- **Invoke events** (button clicks, menu selections)
- **TextChanged events** (text input in text boxes)
- **Selection events** (combo box selections, list box selections)
- **FocusChanged events** (navigation between fields)

Each event includes:
- Timestamp
- Element properties (AutomationId, Name, ControlType)
- XPath for reliable identification
- Event value (for text input, selections)

### Output Files

- `recorded_elements.json` — Element definitions with properties
- `recorded_steps.json` — Step definitions (InvokeStep, ValueStep, SelectionStep)
- `recorded_sequence.json` — Full event sequence with timestamps

See `src/python/recorder/example_draft_output/` for checked-in examples.

### Converter Output

The converter creates:

- `recorded_draft_elements.yaml` — Element repository entries with **FlaUI
  strategy only**. Add `WPFSpy`/`Sikuli` strategies later if needed.
- `recorded_draft_steps.yaml` — Step definitions inferred from actions.
- `draft_recorded_test.robot` — Layer 1 script calling Layer 3 keywords
  directly in recorded order. **Intentionally raw** — no verifications, no reuse.

This draft script is fully runnable:

```bash
python3 -m robot src/python/recorder/example_draft_output/draft_recorded_test.robot
```

## Step 3: Add Verifications

Open the generated `.robot` file and insert Layer 2 verification keywords:

```robotframework
    Click Element    OrdersPage.btnCreateOrder
    
    # Add verification
    Verify Order Confirmation Displayed    SKU-2002    3
```

## Step 4: Refactor to Reusable Modules

Replace raw Layer 3 calls with Layer 2 keywords:

```robotframework
# Before (raw Layer 3)
Set Element Value    LoginPage.txtUsername    user1
Set Element Value    LoginPage.txtPassword    Pass@123
Click Element       LoginPage.btnSubmit

# After (refactored to Layer 2)
Login To Application    user1    Pass@123
```

## Step 5: Finalize

1. Move script from draft to `src/python/tests/`
2. Merge draft repository YAML into real files
3. Run `./run_tests.sh`

## Regenerating the Example Output

```bash
rm -f src/python/recorder/example_draft_output/*.yaml src/python/recorder/example_draft_output/*.robot
python3 src/python/recorder/recorder_engine.py
python3 src/python/recorder/converter.py
mv repository/elements/recorded_draft_elements.yaml src/python/recorder/example_draft_output/
mv repository/steps/recorded_draft_steps.yaml src/python/recorder/example_draft_output/
mv src/python/tests/draft_recorded_test.robot src/python/recorder/example_draft_output/
```

## Live Recording with Spy Agent

For live recording, the WPF application must be running with the Spy Agent enabled:

1. Start the WPF application with `WPFSPY_AGENT_ENABLED=1`
2. Ensure the named pipe is accessible
3. Run the recorder with `mode="real"` or `--live`

The Spy Agent (`WpfSpyAgent/UiaEventRecorder.cs`) hooks into UI Automation events:

```csharp
// Events captured by UiaEventRecorder
Automation.AddAutomationEventHandler(
    InvokePattern.InvokedEvent,
    AutomationElement.RootElement,
    TreeScope.Descendants,
    handler);

Automation.AddAutomationEventHandler(
    TextPattern.TextChangedEvent,
    AutomationElement.RootElement,
    TreeScope.Descendants,
    handler);
```

## Architecture

```
Python (src/python/recorder/live_recorder.py)
    └── WPFSpyRealDriver (src/python/drivers_rf/wpfspy_robotframework/)
            └── Named Pipe IPC (Windows)
                    └── WpfSpyAgent (C#)
                            └── UiaEventRecorder
                                    └── UI Automation Events
                                            └── WPF Application
```

## API Reference

### LiveRecorder

```python
from recorder.live_recorder import LiveRecorder

recorder = LiveRecorder(mode="real")  # or "mock"

# Recording control
recorder.start_recording()           # Start capturing events
recorder.stop_recording()            # Stop capturing
recorder.get_recording_status()     # Get status (isRecording, eventCount)

# Get recorded data
recorder.get_recorded_events()      # Get all events
recorder.get_element_count()         # Number of unique elements
recorder.get_step_count()           # Number of steps
recorder.get_sequence_count()       # Number of sequence events

# Export
recorder.export_to_json()            # Export to JSON files
recorder.export_for_converter()      # Get data for converter
recorder.clear_recording()          # Clear recorded data
```

### RecordingContext

```python
from recorder.live_recorder import RecordingContext

with RecordingContext(mode="real", output_dir="recording") as recorder:
    # Recording starts automatically
    pass
# Recording stops and exports automatically
```
