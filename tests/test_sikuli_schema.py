"""Schema-level smoke tests for the Sikuli repository YAML shape.

Phase 2 self-test: when the recorder captures a template PNG it
should appear in the emitted YAML as a `Sikuli` strategy with both
`value` and `imagePath` set, and a sensible `similarity` default.

We mirror the C# RepositoryWriter shape in pure Python so the test
runs without dotnet / a desktop, then validate the rendered YAML
matches the expected schema.
"""

import os
import sys
import tempfile
import unittest

try:
    import yaml  # type: ignore
except ImportError:
    yaml = None  # type: ignore


THIS_DIR = os.path.dirname(os.path.abspath(__file__))


def _render_sikuli_strategy(image_path=None, alias="SampleWpfApp.LoginPage.txtUsername"):
    """Mirror of WpfTestIde/Recording/RepositoryWriter.cs Sikuli block."""
    strategy = {
        "searchBy": "Image",
        "value": image_path or f"sikuli/{alias.split('.')[-1].lower()}.png",
        "similarity": 0.85,
    }
    if image_path:
        strategy["imagePath"] = image_path
    return strategy


class TestSikuliStrategySchema(unittest.TestCase):
    def test_placeholder_when_no_image_captured(self):
        s = _render_sikuli_strategy(image_path=None)
        self.assertEqual(s["searchBy"], "Image")
        self.assertEqual(s["value"], "sikuli/txtusername.png")
        self.assertNotIn("imagePath", s)
        self.assertEqual(s["similarity"], 0.85)

    def test_image_path_emitted_when_captured(self):
        s = _render_sikuli_strategy(image_path="sikuli/SampleWpfApp.LoginPage.txtUsername.png")
        self.assertEqual(s["value"], "sikuli/SampleWpfApp.LoginPage.txtUsername.png")
        self.assertEqual(s["imagePath"], "sikuli/SampleWpfApp.LoginPage.txtUsername.png")
        self.assertEqual(s["similarity"], 0.85)

    def test_full_yaml_round_trip(self):
        if yaml is None:
            self.skipTest("pyyaml not installed")

        strategy = _render_sikuli_strategy(image_path="sikuli/SampleWpfApp.btnSubmit.png")
        element = {
            "displayName": "Submit Button",
            "controlType": "Button",
            "parentAlias": "SampleWpfApp.LoginPage",
            "defaultTimeout": 10,
            "tags": ["recorded"],
            "strategies": {
                "FlaUI": [{"searchBy": "XPath", "value": "/Window[@Name='LoginPage']/Button[@AutomationId='btnSubmit']"}],
                "WPFSpy": [{"searchBy": "XPath", "value": "/Window[@AutomationId='LoginPage']/Button[@AutomationId='btnSubmit']"}],
                "Sikuli": [strategy],
            },
        }
        with tempfile.TemporaryDirectory() as tmp:
            path = os.path.join(tmp, "element.yaml")
            with open(path, "w") as f:
                yaml.safe_dump({"elements": {"SampleWpfApp.LoginPage.btnSubmit": element}}, f, sort_keys=False)
            with open(path) as f:
                loaded = yaml.safe_load(f)
        sik = loaded["elements"]["SampleWpfApp.LoginPage.btnSubmit"]["strategies"]["Sikuli"][0]
        self.assertEqual(sik["searchBy"], "Image")
        self.assertEqual(sik["imagePath"], "sikuli/SampleWpfApp.btnSubmit.png")
        self.assertEqual(sik["similarity"], 0.85)


if __name__ == "__main__":
    unittest.main()
