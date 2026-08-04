# Checkpoint Wizard Guide

## Overview

The Checkpoint Wizard enables non-programmers to create test verifications through a point-and-click interface. Checkpoints capture expected state during recording and verify it during playback.

## Checkpoint Types

| Type | Description | Use Case |
|------|-------------|----------|
| **Property** | Verify element properties (Text, IsEnabled, IsVisible) | Most common verifications |
| **Area** | OCR-based text verification in screen area | Header text, status messages |
| **Image** | Visual comparison against baseline | Logo verification, layout checks |
| **DataGrid** | Verify DataGrid content | Table data validation |
| **Attribute** | Verify specific Automation attributes | Custom property checks |
| **Count** | Verify element count in container | List item count |

## Using the Checkpoint Wizard

### From WpfTestIde (WPF Application)

1. **Open the Checkpoint Wizard**
   - From the main menu: `Tools > Checkpoint Wizard`
   - Or press `Ctrl+W`

2. **Select an Element**
   - Choose from the element dropdown
   - Or click "Pick Element" to select from screen

3. **Choose Checkpoint Type**
   - Select the type from the dropdown
   - Each type shows its specific configuration panel

4. **Configure the Checkpoint**
   
   **Property Checkpoint:**
   - Select property (Text, IsEnabled, IsVisible, etc.)
   - Enter expected value or click "Get Current Value"
   
   **Area Checkpoint:**
   - Click "Select Area" to draw on screen
   - Or enter coordinates manually
   - OCR extracts text automatically
   
   **Image Checkpoint:**
   - Click "Capture Area" to take baseline
   - Set similarity threshold (default: 95%)
   
   **DataGrid Checkpoint:**
   - Click "Get Current Content" to populate
   - Enter expected CSV content

5. **Add Description** (optional)
   - Helps identify the checkpoint later

6. **Click "Add" to save**

### From Command Line

```bash
# Export checkpoints from a recording session
python3 -m api.healing_cli --export-checkpoints output.yaml
```

### Programmatically

```python
from api.checkpoint_verifier import create_checkpoint

checkpoint = create_checkpoint(
    checkpoint_type="Property",
    property_name="Text",
    expected_value="admin",
    element_alias="LoginPage.txtUsername",
    description="Verify username field"
)
```

## Robot Framework Usage

### Basic Usage

```robotframework
*** Settings ***
Library    modules.CheckpointLibrary

*** Test Cases ***
Verify Login Page
    Load Checkpoints    ${CURDIR}/checkpoints/login_checkpoints.yaml
    Verify All Checkpoints
    Log Verification Summary
```

### Dynamic Checkpoint Creation

```robotframework
*** Test Cases ***
Create And Verify Checkpoints
    # Create checkpoints dynamically
    Create Property Checkpoint    prop_001    LoginPage.txtUsername    Text    admin
    Create Property Checkpoint    prop_002    LoginPage.btnSubmit    IsEnabled    true
    Create Area Checkpoint        area_001    100    200    300    50    Welcome
    
    # Verify all created checkpoints
    Verify All Checkpoints
```

### Direct Property Verification

```robotframework
*** Test Cases ***
Direct Verification
    ${passed}=    Verify Element Property
    ...    LoginPage.txtUsername    Text    admin
    Should Be True    ${passed}
```

## Checkpoint YAML Format

Checkpoints are stored in YAML format:

```yaml
checkpoints:
  - id: prop_001
    type: Property
    elementAlias: LoginPage.txtUsername
    propertyName: Text
    expectedValue: "admin"
    description: "Verify username field"

  - id: area_001
    type: Area
    x: 100
    y: 200
    width: 300
    height: 50
    expectedValue: "Welcome to the app"
    description: "Verify welcome message"

  - id: img_001
    type: Image
    x: 0
    y: 0
    width: 1920
    height: 1080
    baselineImagePath: "checkpoints/baseline/homepage.png"
    parameters:
      threshold: "95"
```

## Checkpoint Properties

| Property | Type | Description |
|----------|------|-------------|
| `id` | string | Unique identifier |
| `type` | enum | Property, Area, Image, DataGrid, Attribute, Count |
| `elementAlias` | string | Target element reference |
| `propertyName` | string | Property to verify |
| `expectedValue` | string | Expected value |
| `operator` | enum | Equals, Contains, GreaterThan, etc. |
| `description` | string | Human-readable description |
| `x, y, width, height` | float | Area coordinates for Area/Image |
| `baselineImagePath` | string | Path to baseline image |
| `parameters` | dict | Additional parameters |

## Comparison Operators

| Operator | Description | Example |
|----------|-------------|---------|
| `Equals` | Exact match | "admin" == "admin" |
| `NotEquals` | Not equal | "admin" != "user" |
| `Contains` | Substring match | "admin" in "admin@example.com" |
| `StartsWith` | Prefix match | "admin" starts with "ad" |
| `EndsWith` | Suffix match | "admin" ends with "min" |
| `GreaterThan` | Numeric comparison | 10 > 5 |
| `LessThan` | Numeric comparison | 5 < 10 |
| `MatchesRegex` | Regex pattern | Matches "^[a-z]+$" |

## API Reference

### Python API

```python
from api.checkpoint_verifier import CheckpointVerifier

# Load and verify
verifier = CheckpointVerifier(driver_api)
verifier.load_checkpoints("checkpoints/test.yaml")
results = verifier.verify_all()

# Create dynamically
from api.checkpoint_verifier import create_checkpoint
cp = create_checkpoint(
    checkpoint_type="Property",
    property_name="Text",
    expected_value="admin",
    element_alias="LoginPage.txtUsername"
)
```

### Robot Framework Keywords

| Keyword | Description |
|---------|-------------|
| `Load Checkpoints` | Load from YAML file |
| `Load Checkpoints From Json` | Load from JSON string |
| `Verify All Checkpoints` | Verify all loaded checkpoints |
| `Verify Checkpoint` | Verify single checkpoint by ID |
| `Log Verification Summary` | Log summary to output |
| `Create Property Checkpoint` | Create property checkpoint |
| `Create Area Checkpoint` | Create area checkpoint |
| `Create Image Checkpoint` | Create image checkpoint |
| `Get Checkpoint Template` | Get YAML template |
| `Verify Element Property` | Direct property verification |

## Best Practices

1. **Use descriptive IDs**: `prop_login_username` instead of `p1`
2. **Add descriptions**: Explain what the checkpoint verifies
3. **Group related checkpoints**: Use naming convention (e.g., `login_*`, `checkout_*`)
4. **Set appropriate thresholds**: For image checkpoints, 95% is a good default
5. **Update baselines**: When UI legitimately changes, update expected values

## Troubleshooting

### Checkpoint fails with "element not found"
- Verify the element alias matches the repository
- Check if the element exists on the current page

### Image checkpoint always fails
- Ensure baseline image exists
- Check similarity threshold (lower if UI has minor variations)
- Verify coordinates are correct

### OCR returns wrong text
- Increase area size to include full text
- Adjust screen resolution (OCR works best at consistent DPI)
- Consider using Property checkpoint instead for exact text match

## Integration with Recording

The Checkpoint Wizard integrates with the recorder:

1. **During Recording**: Click "Add Checkpoint" to capture current element state
2. **After Recording**: Use Checkpoint Wizard to add verifications to recorded steps
3. **Export**: Save checkpoints alongside recorded elements

## Future Enhancements

- [ ] Baseline image management UI
- [ ] Checkpoint comparison/diff view
- [ ] Batch update expected values
- [ ] Checkpoint templates library
- [ ] Integration with test management tools
