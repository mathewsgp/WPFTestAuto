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

Swap `_scripted_interactions()` for a real UIA event subscriber to make
this a genuine recorder against a real WPF app.
"""

import json
import os
import time

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


def record(page_prefix=None):
    """Runs the scripted interaction list and writes the three recorder
    output files, exactly as a live UIA-event recorder would. Page prefix
    is inferred per control (Login vs Orders) the way a real recorder
    would infer it from the active window at the time of the interaction.
    """
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


if __name__ == "__main__":
    record()
