"""
Recorder Engine (simulated)
=============================
In production, this hooks into the WPF application's UI Automation events
(click, key input, focus/selection changes) live, while a tester exercises
the app manually, and writes out recorded_elements.json,
recorded_steps.json, and recorded_sequence.json.

Real UIA event hooking requires Windows + a running WPF process, so this
sandbox version instead REPLAYS a scripted list of "recorded interactions"
against the mock app and produces the exact same three JSON artifacts a
real recorder would — so the rest of the authoring pipeline (Converter,
draft-script generation, Playback) is fully demonstrable end-to-end.

For real WPF recording, use the LiveRecorder class from live_recorder.py
or run with WPFSPY_MODE=real.
"""

import json
import os
import time
from typing import Optional

_THIS_DIR = os.path.dirname(os.path.abspath(__file__))
_OUT_DIR = os.path.join(_THIS_DIR, "sample_recorded")


def _scripted_interactions():
    """Stands in for "what a tester actually clicked/typed", captured live
    by a real UIA event hook. Each entry mirrors what FlaUI's UIA event
    args would expose: automation id, name, control type, action, value.
    """
    return [
        {"automationId": "txtUsername", "name": "UsernameInput", "controlType": "TextBox",
         "action": "SetValue", "value": "user1"},
        {"automationId": "txtPassword", "name": "PasswordInput", "controlType": "TextBox",
         "action": "SetValue", "value": "Pass@123"},
        {"automationId": "btnSubmit", "name": "SubmitBtn", "controlType": "Button",
         "action": "Invoke", "value": None},
        {"automationId": "cmbSku", "name": "SkuCombo", "controlType": "ComboBox",
         "action": "SetValue", "value": "SKU-2002"},
        {"automationId": "txtQty", "name": "QtyInput", "controlType": "TextBox",
         "action": "SetValue", "value": "3"},
        {"automationId": "btnCreateOrder", "name": "CreateOrderBtn", "controlType": "Button",
         "action": "Invoke", "value": None},
    ]


_LOGIN_CONTROLS = {"txtUsername", "txtPassword", "btnSubmit"}


def record(page_prefix: Optional[str] = None, mode: str = "mock"):
    """Runs the scripted interaction list and writes the three recorder
    output files, exactly as a live UIA-event recorder would. Page prefix
    is inferred per control (Login vs Orders) the way a real recorder
    would infer it from the active window at the time of the interaction.
    
    Args:
        page_prefix: Optional page prefix for element aliases.
        mode: Recording mode - "mock" for simulated, "live" for real UIA events.
        
    Returns:
        Tuple of (recorded_elements, recorded_steps, recorded_sequence)
    """
    if mode == "live":
        # Use live recording
        return _record_live(page_prefix)
    else:
        # Use simulated recording
        return _record_simulated(page_prefix)


def _record_simulated(page_prefix: Optional[str] = None):
    """Simulated recording using scripted interactions."""
    os.makedirs(_OUT_DIR, exist_ok=True)
    interactions = _scripted_interactions()

    recorded_elements = {}
    recorded_steps = {}
    recorded_sequence = []

    base_ts = time.time()
    for i, event in enumerate(interactions):
        inferred_prefix = "LoginPage" if event["automationId"] in _LOGIN_CONTROLS else "OrdersPage"
        alias = f"{page_prefix or inferred_prefix}.{event['automationId']}"

        recorded_elements[alias] = {
            "automationId": event["automationId"],
            "name": event["name"],
            "controlType": event["controlType"],
        }

        step_type = "InvokeStep" if event["action"] == "Invoke" else "ValueStep"
        recorded_steps[alias] = {
            "step": step_type,
            "value": event["value"],
        }

        recorded_sequence.append({
            "alias": alias,
            "step": step_type,
            "value": event["value"],
            "timestamp": round(base_ts + i * 0.5, 3),
        })

    with open(os.path.join(_OUT_DIR, "recorded_elements.json"), "w") as f:
        json.dump(recorded_elements, f, indent=2)
    with open(os.path.join(_OUT_DIR, "recorded_steps.json"), "w") as f:
        json.dump(recorded_steps, f, indent=2)
    with open(os.path.join(_OUT_DIR, "recorded_sequence.json"), "w") as f:
        json.dump(recorded_sequence, f, indent=2)

    print(f"[Recorder] Wrote {len(recorded_elements)} elements, "
          f"{len(recorded_steps)} steps, {len(recorded_sequence)} sequence "
          f"entries to {_OUT_DIR}")
    return recorded_elements, recorded_steps, recorded_sequence


def _record_live(page_prefix: Optional[str] = None):
    """Live recording using UIA event hooks.
    
    Requires WPF application running with Spy Agent enabled.
    """
    from live_recorder import LiveRecorder
    
    print("[Recorder] Live recording mode")
    print("[Recorder] Starting recording - interact with the WPF application...")
    print("[Recorder] Press Ctrl+C or call stop_recording() when done")
    
    recorder = LiveRecorder(mode="real")
    recorder.start_recording()
    
    # Wait for user to interact with app
    input("Press Enter when recording is complete...")
    
    recorder.stop_recording()
    recorded_data = recorder.get_recorded_events()
    
    # Convert to legacy format
    recorded_elements = recorded_data.get("elements", {})
    recorded_steps = recorded_data.get("steps", [])
    recorded_sequence = recorded_data.get("sequence", [])
    
    # Convert steps to dict format
    steps_dict = {}
    for step in recorded_steps:
        alias = step.get("alias", "")
        steps_dict[alias] = {
            "step": step.get("stepType", ""),
            "value": step.get("value")
        }
    
    # Export
    recorder.export_to_json(_OUT_DIR)
    
    print(f"[Recorder] Live recording complete: {len(recorded_elements)} elements, "
          f"{len(steps_dict)} steps")
    
    return recorded_elements, steps_dict, recorded_sequence


def record_interactive():
    """Interactive recording mode.
    
    Allows user to start/stop recording and see status.
    """
    from live_recorder import LiveRecorder, RecordingContext
    
    print("=" * 60)
    print("WPFTestAuto Interactive Recorder")
    print("=" * 60)
    print()
    print("1. Simulated recording (mock mode)")
    print("2. Live recording (requires real WPF app with Spy Agent)")
    print("3. Exit")
    print()
    
    choice = input("Select mode (1/2/3): ").strip()
    
    if choice == "1":
        print("\nRunning simulated recording...")
        record(mode="mock")
    elif choice == "2":
        print("\nStarting live recording...")
        print("Start the WPF application with Spy Agent enabled first!")
        print()
        
        try:
            recorder = LiveRecorder(mode="real")
            recorder.start_recording()
            
            print("Recording... Press Enter when done.")
            input()
            
            recorder.stop_recording()
            recorder.get_recorded_events()
            recorder.export_to_json(_OUT_DIR)
            
            print(f"\nRecording complete! Files in: {_OUT_DIR}")
        except Exception as e:
            print(f"Error: {e}")
            print("Make sure the WPF application is running with Spy Agent enabled.")
    else:
        print("Exiting...")


if __name__ == "__main__":
    import sys
    if len(sys.argv) > 1:
        if sys.argv[1] == "--interactive":
            record_interactive()
        elif sys.argv[1] == "--live":
            record(mode="live")
        else:
            record(mode="mock")
    else:
        record(mode="mock")
