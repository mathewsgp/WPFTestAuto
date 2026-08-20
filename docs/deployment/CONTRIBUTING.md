# Contributing

## Adding a new test (the common case)

1. Does the page/element already exist in `repository/elements/*.yaml`?
   If not, add it (see `docs/ELEMENT_REPOSITORY_GUIDE.md`).
2. Does a Layer 2 keyword already do what you need? If not, add one to
   the appropriate `modules/<page>_module.robot` (actions) or
   `modules/<page>_verifications.robot` (assertions) file.
3. Write the Layer 1 test in `tests/`, calling only Layer 2 keywords.
4. Run `./run_tests.sh` and confirm `N tests, N passed, 0 failed`.

You should almost never need to touch `api/`, `drivers_rf/`, or
`drivers/` to add a routine test.

## Adding a new driver strategy to an existing element

Add the strategy block to the element's entry in
`repository/elements/<page>.yaml` — that's it. Layer 3 picks it up
automatically (see `docs/ELEMENT_REPOSITORY_GUIDE.md`).

## Adding a new step type (e.g. RangeValueStep for sliders)

1. Add the step definition to `repository/steps/steps.yaml` for the
   relevant alias(es).
2. Add a corresponding keyword to `api/DriverAgnosticApi.py` (e.g.
   `set_range_value`) that calls a same-named method on each Layer 4
   driver.
3. Implement that method on `FlaUIDriver`, `WPFSpyDriver`, and
   `SikuliDriver` in `drivers_rf/*/`, keeping signatures identical across
   all three (API parity).
4. Add/extend a test proving it works.

## Coding conventions

- **Aliases**: `<Page>.<ElementName>`, PascalCase element name.
- **Layer 2 keyword names**: verb-first, business language
  (`Create New Order`, not `Click Create Order Button`).
- **Layer 3/4 method names**: snake_case Python, mapped to Title Case
  Robot keywords automatically (`click_element` → `Click Element`).
- **File-to-class naming (Robot Framework requirement)**: when a Python
  file is imported as a Robot `Library`, the file name must match its
  main class name exactly (e.g. `DriverAgnosticApi.py` /
  `class DriverAgnosticApi`). Robot Framework's library discovery does
  **not** reliably match differently-cased/underscored names — this bit
  us once during initial development; keep file and class names
  identical for every RF library in this repo.

## Before opening a PR

- [ ] `./run_tests.sh` passes locally (`N tests, N passed, 0 failed`)
- [ ] New aliases follow the naming convention and have both an Element
      Repository entry and a Step Repository entry
- [ ] New Layer 2 keywords have a `[Documentation]` line
- [ ] No test reaches below Layer 2 (no direct `Library
      ../api/DriverAgnosticApi.py` calls from `tests/*.robot` — go through
      a module keyword instead, even a thin one) — this keeps Layer 1
      business-readable
- [ ] Docs updated if you changed the schema, added a driver, or changed
      the authoring workflow

## Development setup (Windows)

After cloning, run `setup_env.bat` from the repo root to create a Python
virtual environment and install all dependencies. Then build the .NET
side with `build_and_run_vs2022.bat` or by opening
`WpfTestFramework.sln` in Visual Studio.

## Proposed (not yet implemented) tooling

See the architecture deck's "Suggested Design Improvements" slide for a
fuller list. Two most relevant to contributors:

- **Repository schema validation in CI** — would catch a malformed
  `strategies` block or missing Step Repository entry before merge.
  Not implemented yet; review YAML changes carefully by hand for now.
- **Parallel execution support** — the mock application's global
  `APP_INSTANCE` singleton (`drivers/mock_wpf_app/mock_app.py`) is **not**
  thread-safe as written. If you parallelize the suite (e.g. via Pabot),
  either give each worker its own app instance or add locking.
