# Visual Test Builder

## Overview

The Visual Test Builder provides a drag-and-drop interface for creating and editing test cases. It allows you to:
- Visually arrange test steps in a flow
- Drag actions from an action palette
- Edit step properties in a properties panel
- Generate Robot Framework code automatically

## Opening the Visual Test Builder

1. Click **📋 Visual Test Builder** in the toolbar
2. Or use the keyboard shortcut (when assigned)

## Interface Layout

```
┌─────────────────────────────────────────────────────────┐
│  📋 Visual Test Builder                                 │
├──────────────┬─────────────────────┬────────────────────┤
│              │                     │                    │
│  Actions     │  Test Steps         │  Step Properties  │
│  Palette     │                     │                    │
│              │  ┌───────────────┐  │  Action Type:     │
│  🖱️ Mouse   │  │ 1. 🖱️ Click   │  │ [Click ▼]         │
│    Click     │  │ 2. ⌨️ SetText │  │                    │
│    DblClick  │  │ 3. 🔍 Verify  │  │ Element:          │
│              │  └───────────────┘  │ [txtUsername ▼]   │
│  ⌨️ Input    │                     │                    │
│    SetText   │  Filter: [______]   │ Value: [______]   │
│    GetText   │                     │                    │
│              │  ➕ Add Step        │ Robot Code:        │
│  🔍 Verify   │                     │ Click Element...   │
│    Verify    │                     │                    │
│              │                     │                    │
└──────────────┴─────────────────────┴────────────────────┘
```

## Features

### 1. Action Palette (Left Panel)

Organized actions by category:

| Category | Actions | Icon |
|----------|---------|------|
| Mouse | Click, DoubleClick, RightClick, Hover | 🖱️ |
| Input | SetText, GetText, Select | ⌨️ |
| Checkbox | Check, Uncheck | ☑️ |
| Verification | Verify Text, Verify Value, Verify Property | 🔍 |
| Control | Wait, Screenshot, KeyPress | ⚙️ |

**To add an action:**
1. Click an action button in the palette
2. The action is added to the step list
3. Edit the element and properties in the right panel

### 2. Test Steps (Center Panel)

**Step List:**
- Shows all test steps in order
- Each step displays: number, icon, description, status
- Click to select and edit
- Drag to reorder

**Step Status Colors:**
- ⬜ Pending - Not yet executed
- 🔄 Running - Currently executing
- ✅ Passed - Executed successfully
- ❌ Failed - Execution failed
- ⏭️ Skipped - Skipped during execution

**Actions:**
- ⬆️ Move Up - Move step up in list
- ⬇️ Move Down - Move step down in list
- 📋 Duplicate - Copy step
- 🗑️ Delete - Remove step
- 🧹 Clear All - Remove all steps

**Search/Filter:**
- Type to filter steps by description
- Useful for large test suites

### 3. Step Properties (Right Panel)

**For all steps:**
- Action Type - The action to perform
- Element Alias - The element to act on

**For text input steps:**
- Value - The text to enter

**For verification steps:**
- Checkpoint Type - What to verify
- Expected Value - Expected result

**Code Preview:**
- Shows Robot Framework code for the step
- Updates in real-time as you edit

### 4. Test Information (Top of Center Panel)

- Test Name - Name of the test case
- Description - Optional documentation

### 5. Generate Code

Click **📋 Generate Code** to preview the full Robot Framework test:

```robot
*** Settings ***
Library    WpfTestLibrary

*** Test Cases ***
Login Test
    [Documentation]    Test the login functionality
    Click Element    alias=txtUsername
    Set Text    alias=txtUsername    text=admin
    Set Text    alias=txtPassword    text=password
    Click Element    alias=btnLogin
    Verify Element Text    alias=lblWelcome    expected=Welcome
```

## Workflow

### Creating a New Test

1. Click **📋 Visual Test Builder** in the toolbar
2. Enter a test name and description
3. Click actions from the palette to add steps
4. Select each step and set the element and properties
5. Click **✅ Save Test** to save as a .robot file

### Editing an Existing Test

1. Click **📋 Visual Test Builder** in the toolbar
2. Click **📁 Load** to load an existing .robot file
3. Make changes to steps
4. Click **💾 Save** to save changes

### Importing from Recording

1. Record steps using the main IDE
2. Click **📋 Visual Test Builder**
3. Recorded steps are automatically loaded
4. Edit and arrange as needed
5. Click **✅ Save Test** to finalize

## Keyboard Shortcuts

| Shortcut | Action |
|----------|--------|
| Ctrl+N | New test |
| Ctrl+O | Open test |
| Ctrl+S | Save test |
| Delete | Delete selected step |
| Ctrl+D | Duplicate selected step |
| Ctrl+Up | Move step up |
| Ctrl+Down | Move step down |

## Flow Diagram View

Click **📊 Flow Diagram** to see a visual representation:

```
┌─────────────────────────────────────────────────────────┐
│  📋 Test: Login Test
├─────────────────────────────────────────────────────────┤
│  1. ⬜ 🖱️ Click [txtUsername]
│         │
│  2. ⬜ ⌨️ Set text 'admin' in [txtUsername]
│         │
│  3. ⬜ ⌨️ Set text '******' in [txtPassword]
│         │
│  4. ⬜ 🖱️ Click [btnLogin]
│         │
│  5. ⬜ 🔍 Verify text 'Welcome' in [lblWelcome]
│         │
└─────────┘
```

## Code Generation

The Visual Test Builder generates standard Robot Framework code:

```robot
*** Test Cases ***
{TestName}
    [Documentation]    {Description}
{Steps}
```

Each step translates to:
| Visual Action | Robot Keyword |
|--------------|---------------|
| Click | Click Element |
| DoubleClick | Double Click Element |
| RightClick | Click Element (button=right) |
| Hover | Mouse Over |
| SetText | Input Text |
| GetText | Get Text |
| Select | Select From List By Label |
| Check | Select Checkbox |
| Uncheck | Unselect Checkbox |
| Verify | Verify Element Text |
| Wait | Sleep |
| Screenshot | Capture Page Screenshot |
| KeyPress | Press Key |

## Tips

1. **Use descriptive names** - Element aliases like `btnLogin` are clearer than `btn1`
2. **Group related steps** - Keep related actions together for readability
3. **Add verifications** - Include checkpoints to validate results
4. **Use wild-card XPath** - For resilient element matching, use wild-card patterns

## See Also

- [Element Repository](./ELEMENT_REPOSITORY.md)
- [Wild-Card XPath](./WILDCARD_XPATH.md)
- [Checkpoint Wizard](./CHECKPOINT_WIZARD.md)
