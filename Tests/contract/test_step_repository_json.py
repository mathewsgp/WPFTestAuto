"""
Unit tests for repository_access.py — step repository loading from JSON.
"""

import json
import os
import sys
import tempfile
import unittest

# Add api directory to path
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "TestAutoLayer", "api"))

from repository_access import load_steps, get_step, _REPO_ROOT


class TestStepRepositoryJson(unittest.TestCase):
    """Tests that step repository is loaded from JSON files."""

    def test_load_steps_from_json(self):
        """load_steps() should load steps from *.json files in the steps directory."""
        steps = load_steps(force_reload=True)
        self.assertIsInstance(steps, dict)
        self.assertGreater(len(steps), 0, "Expected at least one step entry")

    def test_get_step_returns_expected_fields(self):
        """get_step() should return a dict with 'step' and 'parameters' keys."""
        steps = load_steps(force_reload=True)
        # Pick the first alias
        alias = next(iter(steps))
        step = get_step(alias)
        self.assertIn("step", step)
        self.assertIn("parameters", step)

    def test_steps_json_has_correct_structure(self):
        """The committed steps.json should have the expected structure."""
        steps_path = os.path.join(_REPO_ROOT, "steps", "steps.json")
        self.assertTrue(os.path.exists(steps_path), f"steps.json not found at {steps_path}")
        with open(steps_path, "r") as f:
            data = json.load(f)
        self.assertIn("steps", data)
        self.assertIsInstance(data["steps"], dict)
        # Every step entry should have 'step' and 'parameters'
        for alias, entry in data["steps"].items():
            self.assertIn("step", entry, f"Entry {alias} missing 'step'")
            self.assertIn("parameters", entry, f"Entry {alias} missing 'parameters'")

    def test_sample_steps_json_has_correct_structure(self):
        """The committed sample_steps.json should have the expected structure."""
        sample_path = os.path.join(_REPO_ROOT, "samples", "sample_steps.json")
        self.assertTrue(os.path.exists(sample_path), f"sample_steps.json not found at {sample_path}")
        with open(sample_path, "r") as f:
            data = json.load(f)
        self.assertIn("steps", data)
        self.assertIsInstance(data["steps"], list)
        # Every step entry should have 'kind', 'alias'. Action steps also
        # have 'action'; Verify steps have 'value' instead.
        for entry in data["steps"]:
            self.assertIn("kind", entry, f"Entry missing 'kind': {entry}")
            self.assertIn("alias", entry, f"Entry missing 'alias': {entry}")
            if entry["kind"] == "Action":
                self.assertIn("action", entry, f"Action entry missing 'action': {entry}")

    def test_no_step_yaml_files_remain(self):
        """No .yaml step files should remain after conversion."""
        steps_dir = os.path.join(_REPO_ROOT, "steps")
        samples_dir = os.path.join(_REPO_ROOT, "samples")
        yaml_files = []
        for d in (steps_dir, samples_dir):
            if os.path.exists(d):
                for fname in os.listdir(d):
                    if fname.endswith(".yaml") or fname.endswith(".yml"):
                        yaml_files.append(os.path.join(d, fname))
        self.assertEqual(yaml_files, [], f"YAML step files still present: {yaml_files}")


if __name__ == "__main__":
    unittest.main()
