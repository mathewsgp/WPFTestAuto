# Expanded Locator Strategies

## Overview

The framework supports multiple locator strategies per element, allowing robust element identification that can fall back to alternative methods when the primary locator fails.

## Supported Search Methods

| Method | Description | Stability | Priority |
|--------|-------------|-----------|----------|
| **AutomationId** | Unique identifier assigned to element | ⭐⭐⭐ Highest | 1 |
| **Name** | FrameworkElement.Name property | ⭐⭐ High | 2 |
| **ClassName** | .NET class name (useful for custom controls) | ⭐⭐ Medium | 3 |
| **Text** | Text content (for text controls) | ⭐ Medium | 4 |
| **XPath** | Full XPath expression | ⭐ Low | 5 |
| **Index** | Position among siblings | ⭐ Lowest | 99 |

## Strategy Expansion CLI

### View Suggestions

```bash
# Show all suggested strategies for all elements
python api/expand_strategies_cli.py --suggest

# Show suggestions for a specific element
python api/expand_strategies_cli.py --element LoginPage.btnSubmit

# List supported methods
python api/expand_strategies_cli.py --methods
```

### Expand All Elements

```bash
# Preview expansion (dry run)
python api/expand_strategies_cli.py --expand

# Apply expansion
python api/expand_strategies_cli.py --expand --no-dry-run
```

### Add Single Strategy

```bash
# Add Name strategy
python api/expand_strategies_cli.py --add LoginPage.btnSubmit \
    --driver FlaUI --method Name --value "Submit"

# Add with custom priority
python api/expand_strategies_cli.py --add LoginPage.btnSubmit \
    --driver FlaUI --method ClassName --value "System.Windows.Controls.Button" \
    --priority 3
```

### Export Repository

```bash
# Export with expanded strategies
python api/expand_strategies_cli.py --export expanded_repository.yaml
```

## Element Repository Format

Elements in the repository can define multiple strategies per driver:

```yaml
elements:
  LoginPage.txtUsername:
    displayName: Username TextBox
    controlType: TextBox
    automationId: txtUsername
    name: Username
    strategies:
      FlaUI:
        - searchBy: AutomationId
          value: txtUsername
          priority: 1
        - searchBy: Name
          value: Username
          priority: 2
        - searchBy: ClassName
          value: System.Windows.Controls.TextBox
          priority: 3
      WPFSpy:
        - searchBy: AutomationId
          value: txtUsername
          priority: 1
        - searchBy: Name
          value: Username
          priority: 2
```

## Priority System

Strategies are tried in priority order (lowest first):

1. **Priority 1**: Primary locator (AutomationId)
2. **Priority 2-3**: Fallback locators (Name, ClassName)
3. **Priority 5**: XPath (more flexible but fragile)
4. **Priority 99**: Last resort (Index)

## How It Works

1. **Strategy Resolution**: When finding an element, the framework tries strategies in priority order
2. **Self-Healing**: If a strategy fails, it automatically tries the next one
3. **Driver Fallback**: If all strategies for one driver fail, it falls back to the next driver

```python
# Example flow for element "LoginPage.btnSubmit"
try:
    # 1. Try FlaUI with AutomationId (priority 1)
    element = flaui.find_by_automation_id("btnSubmit")
except ElementNotFoundError:
    # 2. Try FlaUI with Name (priority 2)
    element = flaui.find_by_name("Submit")
except ElementNotFoundError:
    # 3. Try WPFSpy with AutomationId (priority 1)
    element = wpfspy.find_by_automation_id("btnSubmit")
# ... and so on
```

## Best Practices

### 1. Always Set AutomationId When Possible

```xaml
<!-- In XAML, set AutomationId for stable identification -->
<Button AutomationId="btnSubmit" Content="Submit"/>
```

### 2. Use Multiple Strategies for Critical Elements

```yaml
elements:
  CheckoutPage.btnPayNow:
    strategies:
      FlaUI:
        - searchBy: AutomationId
          value: btnPayNow
          priority: 1
        - searchBy: Name
          value: "Pay Now"
          priority: 2
        - searchBy: XPath
          value: "//Button[@AutomationId='btnPayNow']"
          priority: 5
```

### 3. Avoid Index-Based Strategies

Index-based strategies are fragile and should only be used as last resort:

```yaml
# Bad - breaks when UI changes
strategies:
  FlaUI:
    - searchBy: Index
      value: Button
      priority: 99

# Good - use meaningful identifiers first
strategies:
  FlaUI:
    - searchBy: AutomationId
      value: btnSubmit
      priority: 1
```

### 4. Keep XPath Expressions Short

Long XPath expressions are fragile:

```yaml
# Bad - very fragile
strategies:
  FlaUI:
    - searchBy: XPath
      value: "/Window[@AutomationId='Main']/Grid/Panel/StackPanel[2]/Button[1]"

# Good - shorter, more resilient
strategies:
  FlaUI:
    - searchBy: XPath
      value: "//Button[@AutomationId='btnSubmit']"
```

## Integration with Healing Store

The healing metadata store tracks which strategies work:

```bash
# Show strategy effectiveness
python api/healing_cli.py --element LoginPage.btnSubmit
```

This helps identify which strategies to prioritize.

## Troubleshooting

### Element Still Not Found

1. Check if element exists in the application
2. Verify AutomationId/Name is correct
3. Use Spy Tool to inspect the element
4. Check healing metadata for recent failures

### Strategies Not Being Tried

1. Verify the driver is initialized
2. Check circuit breaker status
3. Review element repository YAML syntax

### Too Many Screenshots

- Element operations failing repeatedly
- Reduce retries or improve element stability
