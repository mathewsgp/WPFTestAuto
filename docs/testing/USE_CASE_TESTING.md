# Use Case Testing Guide

## Overview

This document provides comprehensive use case testing scenarios for the WPF Test Automation Framework. Each use case includes:
- **Objective**: What the test aims to verify
- **Prerequisites**: What must be in place before testing
- **Steps**: Detailed actions to perform
- **Expected Results**: What should happen
- **Pass Criteria**: How to determine success

---

## Table of Contents

1. [Basic Workflow Tests](#1-basic-workflow-tests)
2. [Recording Tests](#2-recording-tests)
3. [Element Spy Tests](#3-element-spy-tests)
4. [Script Execution Tests](#4-script-execution-tests)
5. [Repository Management Tests](#5-repository-management-tests)
6. [Self-Healing Tests](#6-self-healing-tests)
7. [Advanced Features Tests](#7-advanced-features-tests)

---

## 1. Basic Workflow Tests

### UC-001: Application Launch

| Field | Value |
|-------|-------|
| **Use Case ID** | UC-001 |
| **Use Case Name** | Application Launch |
| **Priority** | High |
| **Test Type** | Smoke Test |

### Objective
Verify that the WPF Test IDE launches successfully and displays the main interface.

### Prerequisites
- WPF application under test is installed
- WpfTestIde.exe is built and available
- Windows with .NET 6.0 or higher

### Steps
1. Launch WpfTestIde.exe
2. Observe the main window loading
3. Verify the toolbar is visible
4. Verify the status bar shows "Not attached"

### Expected Results
- Main window appears within 5 seconds
- All toolbar buttons are visible
- Three-panel layout is displayed
- Status bar shows initial state

### Pass Criteria
✅ Application launches without errors  
✅ All UI elements render correctly  
✅ No unhandled exceptions

---

### UC-002: Process Attachment

| Field | Value |
|-------|-------|
| **Use Case ID** | UC-002 |
| **Use Case Name** | Attach to Process |
| **Priority** | High |
| **Test Type** | Integration Test |

### Objective
Verify that the IDE can attach to a running WPF application.

### Prerequisites
- WpfTestIde is running
- A WPF application (e.g., Notepad) is running

### Steps
1. Click "Attach to Process..."
2. Select the target application from the list
3. Click "Attach"
4. Observe the status bar update

### Expected Results
- Attach dialog opens
- Process list shows running WPF applications
- Successful attachment updates status text
- Pipe connection is established

### Pass Criteria
✅ Dialog opens correctly  
✅ Target process is listed  
✅ Status shows "Attached to [ProcessName]"  
✅ Pipe status shows connection status

---

## 2. Recording Tests

### UC-003: Start/Stop Recording

| Field | Value |
|-------|-------|
| **Use Case ID** | UC-003 |
| **Use Case Name** | Start and Stop Recording |
| **Priority** | Critical |
| **Test Type** | Functional Test |

### Objective
Verify that recording can be started and stopped, capturing user interactions.

### Prerequisites
- IDE is attached to a test application
- Target application has interactive controls

### Steps
1. Click "Record" button
2. Verify button changes to "Stop Recording"
3. Interact with the target application (click a button, type in a textbox)
4. Click "Stop Recording"
5. Observe the Recorded Steps panel

### Expected Results
- Record button shows "Stop Recording"
- Each interaction is captured as a step
- Steps appear in the Recorded Steps list
- Each step shows action type and element alias

### Pass Criteria
✅ Recording starts without errors  
✅ Interactions are captured in real-time  
✅ Recording stops cleanly  
✅ Steps are added to the list

### Test Data
```
Action 1: Click Button "OK"
Action 2: Set Text in TextBox "Username" = "admin"
Action 3: Click Button "Submit"
```

---

### UC-004: Recording Mouse Click

| Field | Value |
|-------|-------|
| **Use Case ID** | UC-004 |
| **Use Case Name** | Record Mouse Click |
| **Priority** | High |
| **Test Type** | Functional Test |

### Objective
Verify that mouse clicks on WPF controls are correctly recorded.

### Prerequisites
- Recording is active
- Target has clickable controls (Button, CheckBox, RadioButton)

### Steps
1. Start recording
2. Click a Button control
3. Click a CheckBox (toggle state)
4. Click a RadioButton
5. Stop recording

### Expected Results
| Control | Recorded Action | Expected Value |
|---------|-----------------|----------------|
| Button | Click Element | None |
| CheckBox | Toggle Element | On/Off |
| RadioButton | Click Element | None |

### Pass Criteria
✅ Click actions are recorded  
✅ Action types are correct  
✅ Element aliases are captured  

---

### UC-005: Recording Text Input

| Field | Value |
|-------|-------|
| **Use Case ID** | UC-005 |
| **Use Case Name** | Record Text Input |
| **Priority** | High |
| **Test Type** | Functional Test |

### Objective
Verify that text input into TextBox controls is correctly recorded.

### Prerequisites
- Recording is active
- Target has TextBox controls

### Steps
1. Start recording
2. Click on a TextBox
3. Type "testuser"
4. Type additional characters
5. Stop recording

### Expected Results
- Set Element Value action is recorded
- Value shows "testuser"
- Multiple keystrokes are consolidated

### Pass Criteria
✅ Text input is captured  
✅ Full text value is recorded  
✅ No duplicate entries for same field  

---

### UC-006: Recording Verification Point

| Field | Value |
|-------|-------|
| **Use Case ID** | UC-006 |
| **Use Case Name** | Add Verification Point |
| **Priority** | High |
| **Test Type** | Functional Test |

### Objective
Verify that verification points can be added to recorded steps.

### Prerequisites
- Steps have been recorded
- Target has verifiable content

### Steps
1. Record a step (e.g., click login)
2. Select the step in the list
3. Click "+ verify after"
4. Configure verification (e.g., text content)
5. Observe verification step added

### Expected Results
- Verification step appears after the action
- Verification type is recorded (Text, Value, Property)
- Expected value is captured

### Pass Criteria
✅ Verification is added  
✅ Correct position in step list  
✅ Verification type is correct  

---

## 3. Element Spy Tests

### UC-007: Launch Element Spy

| Field | Value |
|-------|-------|
| **Use Case ID** | UC-007 |
| **Use Case Name** | Launch Element Spy |
| **Priority** | High |
| **Test Type** | Functional Test |

### Objective
Verify that the Spy Tool can be launched to inspect elements.

### Prerequisites
- IDE is attached to application
- Spy Tool is available

### Steps
1. Click "Spy Tool" button in toolbar
2. Spy Tool dialog opens
3. Element tree populates
4. Select an element in the tree

### Expected Results
- Spy dialog opens
- Visual tree shows application hierarchy
- Properties panel shows element details
- XPath is displayed

### Pass Criteria
✅ Dialog opens without errors  
✅ Tree is populated  
✅ Properties are shown  

---

### UC-008: Element Tree Navigation

| Field | Value |
|-------|-------|
| **Use Case ID** | UC-008 |
| **Use Case Name** | Navigate Element Tree |
| **Priority** | Medium |
| **Test Type** | Usability Test |

### Objective
Verify that the element tree allows navigation and element selection.

### Prerequisites
- Spy Tool is open
- Target has nested elements

### Steps
1. Expand a tree node
2. Select a child element
3. Observe properties update
4. Search for an element using filter

### Expected Results
- Tree nodes expand/collapse
- Selection updates properties panel
- Search filters tree items

### Pass Criteria
✅ Expand/collapse works  
✅ Selection is highlighted  
✅ Search filters correctly  

---

### UC-009: Save Element to Repository

| Field | Value |
|-------|-------|
| **Use Case ID** | UC-009 |
| **Use Case Name** | Save Element to Repository |
| **Priority** | High |
| **Test Type** | Functional Test |

### Objective
Verify that elements can be saved to the element repository.

### Prerequisites
- Spy Tool is open
- Element is selected

### Steps
1. Select an element in the tree
2. View element properties
3. Click "Save to Repository"
4. Enter an alias (e.g., "LoginPage.btnSubmit")
5. Confirm save

### Expected Results
- Element appears in repository
- Element is added to Element Tree panel
- Repository YAML is updated

### Pass Criteria
✅ Element is saved  
✅ Alias is set correctly  
✅ Repository updates  

---

### UC-010: Wild-Card XPath Matching

| Field | Value |
|-------|-------|
| **Use Case ID** | UC-010 |
| **Use Case Name** | Wild-Card XPath Matching |
| **Priority** | Medium |
| **Test Type** | Feature Test |

### Objective
Verify that wild-card XPath patterns work for flexible element matching.

### Prerequisites
- Element repository has entries
- Wild-card XPath is supported

### Steps
1. Add element with wild-card XPath: `//Button[@AutomationId='btn*']`
2. Run test against application with `btnSubmit` button
3. Observe element is found

### Expected Results
| Pattern | Matches |
|---------|---------|
| `btn*` | btnSubmit, btnCancel |
| `*Submit` | btnSubmit, QuickSubmit |
| `*Save*` | QuickSave, SaveAs |

### Pass Criteria
✅ Wild-card patterns are parsed  
✅ Matching elements are found  
✅ Non-matching patterns don't match  

---

## 4. Script Execution Tests

### UC-011: Run Recorded Script

| Field | Value |
|-------|-------|
| **Use Case ID** | UC-011 |
| **Use Case Name** | Run Recorded Script |
| **Priority** | Critical |
| **Test Type** | Integration Test |

### Objective
Verify that recorded scripts can be executed against the target application.

### Prerequisites
- Steps are recorded
- Elements are in repository
- Target application is running

### Steps
1. Record steps (click button, type text)
2. Click "Run Script"
3. Observe execution
4. View Run Results tab

### Expected Results
- Script executes step by step
- Each step shows status (Running, Passed, Failed)
- Results are displayed in Run Results
- Summary shows pass/fail count

### Pass Criteria
✅ All steps execute  
✅ Pass/fail status is accurate  
✅ Results are logged  

---

### UC-012: Script Execution with Failure

| Field | Value |
|-------|-------|
| **Use Case ID** | UC-012 |
| **Use Case Name** | Script Failure Handling |
| **Priority** | High |
| **Test Type** | Error Handling Test |

### Objective
Verify that script handles element not found errors gracefully.

### Prerequisites
- Recorded steps exist
- Target element is intentionally missing

### Steps
1. Record step targeting a non-existent element
2. Run the script
3. Observe error handling

### Expected Results
- Error is logged with details
- Screenshot may be captured (if enabled)
- Script can continue or stop based on config

### Pass Criteria
✅ Error is reported clearly  
✅ Failure details are logged  
✅ Application remains stable  

---

### UC-013: Screenshot on Failure

| Field | Value |
|-------|-------|
| **Use Case ID** | UC-013 |
| **Use Case Name** | Capture Screenshot on Failure |
| **Priority** | Medium |
| **Test Type** | Feature Test |

### Objective
Verify that screenshots are captured when steps fail.

### Prerequisites
- Screenshot on failure is enabled
- A step is configured to fail

### Steps
1. Enable screenshot capture
2. Run script with failing step
3. Check for screenshot in results folder

### Expected Results
- Screenshot is saved to results directory
- Screenshot filename includes timestamp
- Image shows application state at failure

### Pass Criteria
✅ Screenshot is created  
✅ File is valid image  
✅ Location is accessible  

---

## 5. Repository Management Tests

### UC-014: Export Repository to YAML

| Field | Value |
|-------|-------|
| **Use Case ID** | UC-014 |
| **Use Case Name** | Export Repository |
| **Priority** | High |
| **Test Type** | Functional Test |

### Objective
Verify that element repository can be exported to YAML format.

### Prerequisites
- Elements are saved in repository

### Steps
1. Click "Export Repository (.yaml)"
2. Choose save location
3. Save file
4. Open saved file

### Expected Results
- YAML file is created
- Format matches schema
- All elements are included

### Pass Criteria
✅ File is created  
✅ Valid YAML syntax  
✅ All data preserved  

---

### UC-015: Import Repository

| Field | Value |
|-------|-------|
| **Use Case ID** | UC-015 |
| **Use Case Name** | Import Repository |
| **Priority** | Medium |
| **Test Type** | Functional Test |

### Objective
Verify that repository can be imported from YAML file.

### Prerequisites
- Valid YAML repository file exists

### Steps
1. Use File > Open or drag-drop
2. Select repository YAML file
3. Observe elements loading
4. Verify in Element Tree panel

### Expected Results
- Elements are parsed from YAML
- Tree structure is built
- Elements are usable in scripts

### Pass Criteria
✅ File is parsed  
✅ Elements appear in tree  
✅ No data loss  

---

### UC-016: Element Editor

| Field | Value |
|-------|-------|
| **Use Case ID** | UC-016 |
| **Use Case Name** | Edit Element Properties |
| **Priority** | Medium |
| **Test Type** | Functional Test |

### Objective
Verify that element properties can be edited.

### Prerequisites
- Element exists in repository

### Steps
1. Select element in Element Tree
2. Open Element Editor tab
3. Modify properties (Alias, AutomationId, XPath)
4. Save changes

### Expected Results
- Properties are editable
- Changes persist
- Repository updates

### Pass Criteria
✅ Edit fields are enabled  
✅ Changes are saved  
✅ Updates reflect in tree  

---

## 6. Self-Healing Tests

### UC-017: Self-Healing Locator

| Field | Value |
|-------|-------|
| **Use Case ID** | UC-017 |
| **Use Case Name** | Self-Healing Element Locator |
| **Priority** | High |
| **Test Type** | Feature Test |

### Objective
Verify that self-healing finds elements when primary locator fails.

### Prerequisites
- Element has multiple strategies defined
- Self-healing is enabled

### Steps
1. Define element with multiple strategies:
   - Primary: AutomationId = "btnSubmit"
   - Fallback: XPath = "//Button[contains(@Name,'Submit')]"
2. Remove AutomationId from target button
3. Run script

### Expected Results
- Primary locator fails
- Self-healing attempts fallback
- Element is found via XPath
- Step succeeds

### Pass Criteria
✅ Primary failure is detected  
✅ Fallback is attempted  
✅ Alternative locator succeeds  

---

### UC-018: Healing Metadata Store

| Field | Value |
|-------|-------|
| **Use Case ID** | UC-018 |
| **Use Case Name** | Healing Metadata Persistence |
| **Priority** | Medium |
| **Test Type** | Feature Test |

### Objective
Verify that healing metadata is stored and reused.

### Prerequisites
- Self-healing found alternative locator

### Steps
1. Run script where healing occurred
2. Check metadata store
3. Run script again
4. Observe if healing is faster

### Expected Results
- Metadata includes: original locator, healed locator, timestamp
- Subsequent runs use stored healing
- Metadata persists across sessions

### Pass Criteria
✅ Metadata is stored  
✅ Healing data is complete  
✅ Persistence works  

---

## 7. Advanced Features Tests

### UC-019: Visual Test Builder

| Field | Value |
|-------|-------|
| **Use Case ID** | UC-019 |
| **Use Case Name** | Visual Test Builder |
| **Priority** | Medium |
| **Test Type** | Feature Test |

### Objective
Verify that Visual Test Builder allows creating tests visually.

### Prerequisites
- Visual Test Builder is implemented

### Steps
1. Click "Visual Test Builder" button
2. Drag actions from palette to step list
3. Configure step properties
4. Generate Robot code
5. Save test

### Expected Results
- Builder dialog opens
- Actions can be dragged
- Properties panel edits steps
- Code is generated correctly

### Pass Criteria
✅ UI is functional  
✅ Steps can be added/configured  
✅ Code generation works  

---

### UC-020: Checkpoint Wizard

| Field | Value |
|-------|-------|
| **Use Case ID** | UC-020 |
| **Use Case Name** | Checkpoint Wizard |
| **Priority** | Medium |
| **Test Type** | Feature Test |

### Objective
Verify that Checkpoint Wizard helps create verification points.

### Prerequisites
- Checkpoint Wizard is implemented

### Steps
1. Click "Checkpoint Wizard"
2. Select target element
3. Choose checkpoint type (Text, Value, Property)
4. Configure expected value
5. Add to test

### Expected Results
- Wizard guides through creation
- Checkpoint is added to test
- Verification is accurate

### Pass Criteria
✅ Wizard is accessible  
✅ Configuration is intuitive  
✅ Checkpoint works  

---

### UC-021: OCR DataGrid Content

| Field | Value |
|-------|-------|
| **Use Case ID** | UC-021 |
| **Use Case Name** | OCR DataGrid Reading |
| **Priority** | Low |
| **Test Type** | Feature Test |

### Objective
Verify that OCR can extract content from DataGrid controls.

### Prerequisites
- Target has DataGrid with content
- OCR feature is enabled

### Steps
1. Click "OCR DataGrid"
2. Select DataGrid element
3. Observe OCR result
4. Verify content accuracy

### Expected Results
- DataGrid content is extracted
- OCR result appears in status bar
- Content is readable

### Pass Criteria
✅ OCR runs  
✅ Content is extracted  
✅ Results are usable  

---

## Test Execution Checklist

Before running tests, verify:

- [ ] Target application is installed and accessible
- [ ] All dependencies are in place
- [ ] Test environment is clean
- [ ] Results directory exists and is writable
- [ ] Logging is enabled

## Defect Reporting

When a test fails, document:

1. **Test ID**: UC-XXX
2. **Expected**: What should happen
3. **Actual**: What happened
4. **Environment**: OS, .NET version
5. **Steps to Reproduce**
6. **Logs/Screenshots**

---

## Appendix A: Test Data Templates

### Element Repository Template
```yaml
elements:
  PageName.elementName:
    automationId: elementId
    name: Element Name
    controlType: Button
    strategies:
      - searchBy: AutomationId
        value: elementId
        priority: 1
```

### Robot Test Template
```robot
*** Test Cases ***
Example Test
    Click Element    alias=PageName.elementName
    Input Text    alias=PageName.textBox    text=value
    Verify Element Text    alias=PageName.label    expected=Expected Text
```

---

## Appendix B: Cross-Reference

| Feature | Use Cases |
|---------|-----------|
| Recording | UC-003, UC-004, UC-005, UC-006 |
| Spy Tool | UC-007, UC-008, UC-009 |
| Repository | UC-014, UC-015, UC-016 |
| Self-Healing | UC-017, UC-018 |
| Script Execution | UC-011, UC-012, UC-013 |
| Advanced Features | UC-019, UC-020, UC-021 |
| XPath Features | UC-010 |

---

*Document Version: 1.0*  
*Last Updated: 2024*
