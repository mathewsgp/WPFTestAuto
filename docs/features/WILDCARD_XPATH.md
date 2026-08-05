# Wild-Card XPath Support

## Overview

The framework supports wild-card patterns in XPath expressions, similar to Ranorex's RanoreXPath. This allows for more resilient element locating when UI attributes change.

## Supported Patterns

### 1. Type Wild-Cards
Match any element type:

```xpath
//Window/?/Button      - Any Button directly under Window
//Window//*            - Any descendant under Window
//*[@AutomationId='btnSubmit']  - Any element with this ID
```

### 2. AutomationId Wild-Cards
Flexible matching on AutomationId:

```xpath
//Button[@AutomationId='btn*']      - Starts with 'btn'
//Button[@AutomationId='*Submit']  - Ends with 'Submit'
//Button[@AutomationId='*Save*']   - Contains 'Save'
//Button[@AutomationId='btn?New']   - btn + any char + New
```

### 3. Regex Patterns
Full regex matching on element properties:

```xpath
//Button[regex:btn[0-9]+]    - Matches 'btn1', 'btn2', etc.
//*[regex:txt[A-Z]{3}]       - Matches 'txtABC', etc.
```

## Usage

### In Element Repository

```yaml
elements:
  LoginPage.btnSubmit:
    automationId: btnSubmit
    strategies:
      FlaUI:
        - searchBy: AutomationId
          value: btnSubmit
          priority: 1
        - searchBy: XPath
          value: "//Button[@AutomationId='btn*']"  # Wild-card fallback
          priority: 5
          wildcard: true
```

### Programmatic Usage

```python
from wildcard_xpath import WildcardXPathMatcher

matcher = WildcardXPathMatcher()

# Parse a wild-card XPath
result = matcher.parse_wildcard_xpath("//Button[@AutomationId='btn*']")
print(result)

# Generate flexible alternatives
alternatives = matcher.expand_xpath_alternatives("", {
    "tag": "Button",
    "automation_id": "btnSubmit"
})
print(alternatives)
```

### In C# (WPF)

```csharp
// Find element with wild-card XPath
var element = VisualTreeInspector.FindByXPath("//Button[@AutomationId='btn*']");

// Build flexible XPath for element
var flexible = VisualTreeInspector.BuildFlexibleXPath(buttonElement);
```

## Pattern Matching Rules

| Pattern | Matches | Example |
|---------|---------|---------|
| `*` | Any characters | `btn*` matches `btnSubmit`, `btnCancel` |
| `?` | Single character | `btn?` matches `btn1`, `btnA` |
| `*[text]*` | Contains | `*Save*` matches `QuickSave`, `SaveAs` |
| `[regex:pattern]` | Regex | `[regex:btn[0-9]+]` matches `btn1`, `btn123` |

## Benefits

1. **UI Change Resilience** - Matches elements even when IDs change slightly
2. **Dynamic IDs** - Handles IDs with timestamps or random suffixes
3. **Flexible Matching** - Multiple ways to match the same element
4. **Fallback Strategies** - Use wild-cards as fallbacks for primary locators

## Best Practices

### DO ✅
```yaml
# Primary: exact match
- searchBy: AutomationId
  value: btnSubmit
  priority: 1

# Fallback: prefix match
- searchBy: XPath
  value: "//Button[@AutomationId='*Submit']"
  priority: 5
  wildcard: true
```

### DON'T ❌
```yaml
# Too generic - matches many elements
- searchBy: XPath
  value: "//Button[@AutomationId='*']"
  priority: 99
```

## CLI Tool

```bash
# Test XPath parsing
python -m wildcard_xpath "//Button[@AutomationId='btn*']"

# Generate alternatives
python -c "
from wildcard_xpath import WildcardXPathMatcher
matcher = WildcardXPathMatcher()
alts = matcher.expand_xpath_alternatives('', {
    'tag': 'Button',
    'automation_id': 'btnSubmit'
})
for alt in alts:
    print(alt)
"
```

## Integration with Self-Healing

Wild-card XPath is automatically used by the self-healing engine:

1. When primary locator fails, system searches with wild-card patterns
2. Matches are scored by specificity
3. Best match is used if confidence > threshold
4. Match is logged for future reference

See also: [Healing Metadata Store](./SELF_HEALING.md)
