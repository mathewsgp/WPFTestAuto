# Automatic Screenshot on Failure

## Overview

The framework automatically captures screenshots when element operations fail, making it easier to debug test failures by providing visual context.

## Features

| Feature | Description |
|---------|-------------|
| **Automatic Capture** | Screenshots captured when all strategies fail |
| **Metadata Tracking** | JSON metadata with timestamp, error type, element alias |
| **Multi-Driver Support** | Works with FlaUI, WPFSpy, and Sikuli |
| **HTML Report** | Generate visual reports of all captured screenshots |
| **Configurable** | Enable/disable, set output directory, limit count |

## How It Works

When an element operation fails:

1. The framework tries all configured strategies (FlaUI → WPFSpy → Sikuli)
2. If all strategies fail, `ScreenshotManager.capture_on_failure()` is called
3. The active driver captures a screenshot
4. Screenshot is saved with metadata

## Usage

### Basic Usage (Automatic)

Screenshots are captured automatically when element operations fail in `DriverAgnosticApi`:

```python
from api import DriverAgnosticApi

api = DriverAgnosticApi()
api.click_element("LoginPage.btnSubmit")  # If this fails, screenshot is captured
```

### Manual Usage

```python
from screenshot_manager import ScreenshotManager, get_screenshot_manager

# Get global instance
screenshot_mgr = get_screenshot_manager()

# Or create your own
screenshot_mgr = ScreenshotManager(
    output_dir="test-output/screenshots",
    capture_on_failure=True,
    max_screenshots=50
)

# Capture manually
metadata = screenshot_mgr.capture(
    image_data=screenshot_bytes,
    alias="LoginPage.btnSubmit",
    error_type="ElementNotFoundError",
    error_message="Element not visible",
    driver_used="FlaUI"
)

# Capture on failure
try:
    # ... some operation
    pass
except Exception as e:
    screenshot_mgr.capture_on_failure("MyElement", e, "FlaUI")
```

### Generate HTML Report

```python
from screenshot_manager import get_screenshot_manager

screenshot_mgr = get_screenshot_manager()
html_report = screenshot_mgr.generate_html_report("screenshots/report.html")
```

## Output Structure

```
test-output/screenshots/
├── failure_20240115_143022_001.png
├── failure_20240115_143025_002.png
├── screenshots_20240115_143000.json    # Session metadata
└── report.html                          # HTML report (if generated)
```

## Screenshot Metadata JSON

```json
{
  "session_id": "20240115_143000",
  "captured_at": "2024-01-15T14:30:25.123456",
  "output_directory": "test-output/screenshots",
  "total_screenshots": 2,
  "screenshots": [
    {
      "timestamp": "2024-01-15T14:30:22.123456",
      "alias": "LoginPage.btnSubmit",
      "error_type": "ElementNotFoundError",
      "error_message": "Element not found with XPath...",
      "driver_used": "FlaUI",
      "screenshot_path": "test-output/screenshots/failure_20240115_143022_001.png",
      "filename": "failure_20240115_143022_001.png"
    }
  ]
}
```

## Configuration

| Parameter | Default | Description |
|-----------|---------|-------------|
| `output_dir` | `test-output/screenshots` | Directory for screenshots |
| `capture_on_failure` | `True` | Whether to auto-capture |
| `max_screenshots` | `50` | Maximum screenshots per session |

## Integration with Healing Store

Screenshots are linked with the healing metadata store:

```python
# Both healing data and screenshots are captured on failure
# The healing metadata includes which strategies failed
# Screenshots provide visual context
```

## Best Practices

1. **Review screenshots after failures** - They show what the UI looked like
2. **Use consistent naming** - Element aliases in screenshots help identify issues
3. **Set reasonable limits** - `max_screenshots` prevents disk bloat
4. **Generate reports** - HTML reports make it easy to review failures

## Troubleshooting

### No screenshots captured

- Check that `capture_on_failure=True` is set
- Verify the driver supports screenshots (FlaUI does by default)
- Check write permissions to output directory

### Screenshot is blank

- The driver may not be capturing the correct window
- Try capturing manually with `driver.capture_screenshot()`

### Too many screenshots

- Reduce `max_screenshots` limit
- Increase retry logic to reduce flaky failures
