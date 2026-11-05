# Spy Tool Guide

## Overview

The Spy Tool provides visual element inspection with a hierarchical tree view, property grid, and XPath editor - similar to the Spy tools in TestComplete and Ranorex.

## Features

| Feature | Description |
|---------|-------------|
| **Visual Tree View** | Hierarchical display of all UI elements |
| **Property Grid** | Shows all properties of selected element |
| **XPath Editor** | Edit and validate XPath expressions |
| **Search** | Filter elements by name or AutomationId |
| **Copy to Clipboard** | Copy XPath or individual properties |
| **Add to Repository** | Add inspected elements to element repository |

## Using the Spy Tool

### From WpfTestIde

1. **Open Spy Tool**
   - Click the **🔍 Spy Tool** button in the toolbar
   - Or press `Ctrl+Shift+S`

2. **Browse Element Tree**
   - Click the **Refresh Tree** button to load elements
   - Navigate the tree by expanding/collapsing nodes
   - Elements with AutomationId are highlighted in **bold blue**

3. **Inspect Element**
   - Click any element in the tree
   - View all properties in the Property Grid on the right
   - See the generated XPath in the XPath Editor

4. **Search Elements**
   - Type in the Search box to filter
   - Matching elements are highlighted in bold

5. **Copy Properties**
   - Click 📋 next to any property to copy
   - Click **📋 Copy XPath** to copy the XPath
   - Click **📋 Copy All Props** to copy all properties

6. **Add to Repository**
   - Select an element and click **Add to Repository**
   - The element will be added to the Element Repository

## Property Grid

The property grid shows all properties of the selected element:

| Property | Description |
|----------|-------------|
| **AutomationId** | The AutomationId property (primary locator) |
| **Name** | FrameworkElement.Name property |
| **ControlType** | WPF control type (Button, TextBox, etc.) |
| **ClassName** | Full .NET class name |
| **Text** | Text content (for text controls) |
| **XPath** | Generated XPath expression |
| **IsEnabled** | Whether element is enabled |
| **IsVisible** | Whether element is visible |
| **BoundingRectangle** | Screen coordinates and size |

## XPath Editor

The XPath editor shows the XPath expression for locating the selected element:

- **Auto-generated**: XPath is generated from the element's path in the tree
- **Editable**: You can modify the XPath directly
- **Validatable**: Click **✓ Validate** to check XPath syntax
- **Copyable**: Click **📋 Copy XPath** to copy to clipboard

### XPath Syntax

The Spy Tool supports standard WPF XPath syntax:

```
/Window[@AutomationId='MainWindow']/Grid/TextBox[@AutomationId='txtUsername']
```

- `/` — Absolute path from root
- `ElementName` — Match by type
- `[@AutomationId='value']` — Match by AutomationId
- `[@Name='value']` — Match by Name property
- `[N]` — N-th child of that type (1-based)

## Icons in Tree View

| Icon | Control Type |
|------|--------------|
| 🔘 | Button |
| 📝 | TextBox |
| ☑️ | CheckBox |
| 📋 | ListBox |
| 🗂️ | DataGrid |
| 📜 | Menu |
| 🪟 | Window |
| 📇 | Tab |
| 🖼️ | Image |
| 🌳 | TreeView |

## Keyboard Shortcuts

| Shortcut | Action |
|----------|--------|
| `Ctrl+F` | Focus search box |
| `Ctrl+C` | Copy XPath |
| `Escape` | Close dialog |

## Integration with Recording

The Spy Tool integrates with the recording workflow:

1. **During Recording**: Use Spy Tool to inspect elements before adding them
2. **After Recording**: Use Spy Tool to verify element properties
3. **Repository Building**: Inspect and add elements directly from the Spy Tool

## Example Workflow

```
1. Attach to application
2. Open Spy Tool
3. Click "Refresh Tree"
4. Navigate to desired element
5. Verify properties in Property Grid
6. Click "Add to Repository"
7. Element is added with XPath
```

## Troubleshooting

### "No App Connected" message
- Ensure you've attached to a process first
- Check that the WpfSpyAgent pipe is active

### Tree is empty
- Click "Refresh Tree" to reload
- The application may have changed since last refresh

### XPath validation fails
- Ensure XPath starts with `/`
- Check for matching brackets in predicates
- Use `//` for descendant matching anywhere in tree
