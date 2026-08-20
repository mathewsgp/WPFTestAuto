# WPF Test Automation Framework - Documentation

Welcome! This folder contains all documentation for the WPF Test Automation Framework.

---

## Documentation Structure

```
docs/
├── README.md              ← You are here
│
├── guides/               ← User Guides (Step-by-step instructions)
│   ├── GETTING_STARTED.md
│   ├── IDE_GUIDE.md
│   ├── RECORDER_GUIDE.md
│   └── ELEMENT_REPOSITORY_GUIDE.md
│
├── features/            ← Feature Documentation (In-depth guides)
│   ├── SELF_HEALING.md    ← Self-healing + metadata store (combined)
│   ├── LOCATOR_STRATEGIES.md
│   ├── WILDCARD_XPATH.md
│   ├── SCREENSHOT_ON_FAILURE.md
│   ├── CHECKPOINT_WIZARD.md
│   ├── SPY_TOOL.md
│   └── VISUAL_TEST_BUILDER.md
│
├── technical/            ← Technical Documentation (For developers)
│   ├── ARCHITECTURE.md
│   ├── TECHNICAL_DESIGN.md
│   ├── PROTOCOL.md
│   ├── INJECTION_OPTIONS.md
│   ├── WPFSPY_MODULE.md
│   └── DRIVER_IMPLEMENTATION_ANALYSIS.md
│
├── planning/             ← Planning & Analysis
│   └── ROADMAP.md        ← Gap analysis + priorities + UI suggestions
│
├── deployment/           ← Deployment Guides
│   ├── PRODUCTION_DEPLOYMENT.md
│   └── CONTRIBUTING.md
│
└── testing/             ← Testing Documentation
    └── USE_CASE_TESTING.md  ← 21 test cases
```

---

## Quick Start

1. **[Getting Started](./guides/GETTING_STARTED.md)** - Set up and run your first test
2. **[Recorder Guide](./guides/RECORDER_GUIDE.md)** - Record test steps
3. **[IDE Guide](./guides/IDE_GUIDE.md)** - Learn the interface

---

## Feature Status

| Feature | Status | Documentation |
|---------|--------|---------------|
| Recording & Playback | Done | [Guide](./guides/RECORDER_GUIDE.md) |
| Spy Tool | Done | [Guide](./features/SPY_TOOL.md) |
| Element Tree View | Done | UI panel |
| Self-Healing | Done | [Guide](./features/SELF_HEALING.md) |
| Wild-Card XPath | Done | [Guide](./features/WILDCARD_XPATH.md) |
| Visual Test Builder | Done | [Guide](./features/VISUAL_TEST_BUILDER.md) |
| Checkpoint Wizard | Done | [Guide](./features/CHECKPOINT_WIZARD.md) |
| Screenshot on Failure | Done | [Guide](./features/SCREENSHOT_ON_FAILURE.md) |
| Element Import/Export (YAML) | Done | [IDE Guide](./guides/IDE_GUIDE.md) |
| Steps Import/Export (YAML) | Done | [IDE Guide](./guides/IDE_GUIDE.md) |
| Layout Persistence | Done | [IDE Guide](./guides/IDE_GUIDE.md) |
| Dark/Light Theme | Done | [IDE Guide](./guides/IDE_GUIDE.md) |
| Drag-to-Reorder Steps | Done | [IDE Guide](./guides/IDE_GUIDE.md) |

---

## Common Tasks

| Task | Documentation |
|------|---------------|
| First time setup | [Getting Started](./guides/GETTING_STARTED.md) |
| Recording tests | [Recorder Guide](./guides/RECORDER_GUIDE.md) |
| Finding elements | [Spy Tool](./features/SPY_TOOL.md) |
| Dealing with element not found | [Self-Healing](./features/SELF_HEALING.md) |
| Creating verifications | [Checkpoint Wizard](./features/CHECKPOINT_WIZARD.md) |
| Understanding architecture | [Architecture](./technical/ARCHITECTURE.md) |
| Planning new features | [Roadmap](./planning/ROADMAP.md) |

---

## Contributing

See [Contributing Guide](./deployment/CONTRIBUTING.md) for:
- Development setup
- Coding standards
- Pull request process

---

*Last updated: 2026-08-20*
