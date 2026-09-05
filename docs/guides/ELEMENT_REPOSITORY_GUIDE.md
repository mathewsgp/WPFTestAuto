# Element & Step Repository Guide

## Files

```
repository/
├── elements/
│   ├── login_page.yaml
│   ├── orders_page.yaml
│   └── <your_page>.yaml       <- add one file per page/screen
└── steps/
    └── steps.yaml              <- add entries here for every new alias
```

All `.yaml` files under `repository/elements/` are merged into one alias
→ element map at load time; same for `repository/steps/`. Split files by
page for readability — the loader doesn't care how many files there are.

## Alias convention

`<Page>.<ElementName>`, e.g. `OrdersPage.CreateOrderButton`. Dotted,
human-readable, globally unique. Avoid renaming an alias once tests
reference it — treat it like an API contract.

## Element Repository schema

```yaml
elements:
  <Alias>:
    displayName: <string>          # human label, shown in logs
    controlType: <string>          # TextBox | Button | ComboBox | Label | DataGrid | CheckBox | ...
    parentAlias: <string>           # for scoped/relative lookups
    defaultTimeout: <int seconds>
    tags: [<string>, ...]           # free-form, used for filtering/organization
    strategies:                      # only include drivers actually usable for this control
      FlaUI:
        searchBy: "AutomationId"
        value: <string>
        scope: "Descendant" | "Children" | ...
      WPFSpy:
        searchBy: "Name"
        value: <string>
      Sikuli:
        imagePath: <string>          # path or semantic tag; see docs/RECORDER_GUIDE.md
        similarity: <float 0-1>
```

**Only add the strategies that make sense.** A well-behaved standard WPF
control usually only needs `FlaUI`. A custom-rendered control that UIA
can't see needs `WPFSpy` and/or `Sikuli` instead — see
`OrdersPage.PriorityCheckbox` in `repository/elements/orders_page.yaml`
for a worked example (FlaUI deliberately absent/broken; WPFSpy + Sikuli
present).

**Strategy order = fallback order.** `api/repository_access.py`
always tries `FlaUI` → `WPFSpy` → `Sikuli`, skipping any not configured
for that alias. This is also the *runtime self-healing* order — see
`docs/SELF_HEALING.md`.

## Step Repository schema

```yaml
steps:
  <Alias>:
    step: "InvokeStep" | "ValueStep" | "ToggleStep" | "TextStep" | "RangeValueStep"
    parameters:
      - name: <string>
        type: "string" | "boolean" | "double"
        required: <bool>
        min: <number>     # RangeValueStep only
        max: <number>     # RangeValueStep only
```

| Step | Used for | Layer 3 keyword(s) |
|---|---|---|
| `InvokeStep` | Buttons, menu items | `Click Element` |
| `ValueStep` | TextBoxes, ComboBoxes | `Set Element Value`, `Get Element Text` |
| `ToggleStep` | CheckBoxes, toggle buttons | `Toggle Element` |
| `TextStep` | Labels, read-only text | `Get Element Text`, `Verify Element Text` |
| `RangeValueStep` | Sliders | *(extend `DriverAgnosticApi` if/when needed)* |

## Adding a new page

1. Create `repository/elements/<page>.yaml` with one entry per control.
2. Add matching entries to `repository/steps/steps.yaml`.
3. Add Layer 2 keywords in `src/python/modules/<page>_module.robot` /
   `src/python/modules/<page>_verifications.robot` that call Layer 3 keywords by
   alias.
4. Write your Layer 1 test in `src/python/tests/`.
5. Run `./run_tests.sh` — you should never need to touch
   `src/python/api/DriverAgnosticApi.py` or `src/python/drivers_rf/` for a routine new page.

## Validating repository files

There's no CI schema validator wired up yet — see
`docs/CONTRIBUTING.md` and the "Repository Schema Validation" item on the
architecture's Suggested Design Improvements list for a proposed addition
(YAML schema + lint step in CI, catching typos/missing strategies before
merge).
