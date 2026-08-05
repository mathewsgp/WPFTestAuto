# WPF Test Automation Framework - Documentation

Welcome to the documentation for the WPF Test Automation Framework.

---

## 📚 Documentation Structure

```
docs/
├── README.md                    ← You are here
│
├── guides/                      ← User Guides
│   ├── GETTING_STARTED.md       - Quick start guide
│   ├── IDE_GUIDE.md             - IDE interface overview
│   ├── RECORDER_GUIDE.md         - Recording test steps
│   └── ELEMENT_REPOSITORY_GUIDE.md - Managing elements
│
├── features/                    ← Feature Documentation
│   ├── SELF_HEALING_LOCATORS.md - Auto-heal failed locators
│   ├── HEALING_METADATA_STORE.md - Store healing data
│   ├── LOCATOR_STRATEGIES.md    - Element search strategies
│   ├── WILDCARD_XPATH.md        - Flexible XPath patterns
│   ├── SCREENSHOT_ON_FAILURE.md - Capture screenshots
│   ├── CHECKPOINT_WIZARD.md     - Verification point wizard
│   ├── SPY_TOOL.md              - Element inspection tool
│   └── VISUAL_TEST_BUILDER.md   - Visual test creation
│
├── technical/                   ← Technical Documentation
│   ├── ARCHITECTURE.md          - System architecture
│   ├── TECHNICAL_DESIGN.md      - Design specifications
│   ├── PROTOCOL.md              - Communication protocol
│   ├── INJECTION_OPTIONS.md     - Code injection methods
│   ├── WPFSPY_MODULE.md         - WPFSpy module details
│   └── DRIVER_IMPLEMENTATION_ANALYSIS.md - Driver analysis
│
├── planning/                    ← Planning & Analysis
│   ├── GAP_ANALYSIS.md          - Feature gap analysis
│   ├── IMPLEMENTATION_CHOICES.md - Implementation roadmap
│   └── UI_IMPROVEMENTS.md       - UI enhancement suggestions
│
├── deployment/                  ← Deployment Guides
│   ├── PRODUCTION_DEPLOYMENT.md - Production setup
│   └── CONTRIBUTING.md          - Contribution guidelines
│
└── testing/                     ← Testing Documentation
    └── USE_CASE_TESTING.md      - Use case test scenarios
```

---

## 🚀 Quick Start

1. **[Getting Started](./guides/GETTING_STARTED.md)** - Set up and run your first test
2. **[IDE Guide](./guides/IDE_GUIDE.md)** - Learn the interface
3. **[Recorder Guide](./guides/RECORDER_GUIDE.md)** - Record test steps
4. **[Use Case Testing](./testing/USE_CASE_TESTING.md)** - Verify functionality

---

## 📖 Key Guides

### For Users
| Guide | Description |
|-------|-------------|
| [Getting Started](./guides/GETTING_STARTED.md) | First time setup and basics |
| [IDE Guide](./guides/IDE_GUIDE.md) | Interface walkthrough |
| [Recorder Guide](./guides/RECORDER_GUIDE.md) | Recording tests |
| [Element Repository](./guides/ELEMENT_REPOSITORY_GUIDE.md) | Managing elements |

### For Developers
| Document | Description |
|----------|-------------|
| [Architecture](./technical/ARCHITECTURE.md) | System design |
| [Technical Design](./technical/TECHNICAL_DESIGN.md) | Detailed specifications |
| [Protocol](./technical/PROTOCOL.md) | IPC protocol |
| [Gap Analysis](./planning/GAP_ANALYSIS.md) | Feature comparison |

---

## 🔧 Features

### Core Features
- ✅ **Recording** - Capture user interactions
- ✅ **Playback** - Execute recorded scripts
- ✅ **Element Repository** - Manage element definitions
- ✅ **Spy Tool** - Inspect UI elements
- ✅ **Self-Healing** - Auto-recover from locator failures

### Advanced Features
- 🔧 **Wild-Card XPath** - Flexible element matching
- 🔧 **Checkpoint Wizard** - Visual verification creation
- 🔧 **Visual Test Builder** - Drag-drop test creation
- 🔧 **Screenshot on Failure** - Debug failed tests
- 🔧 **OCR Support** - Read custom controls

---

## 📊 Feature Status

| Feature | Status | Documentation |
|---------|--------|---------------|
| Recording | ✅ Complete | [Guide](./guides/RECORDER_GUIDE.md) |
| Spy Tool | ✅ Complete | [Guide](./features/SPY_TOOL.md) |
| Self-Healing | ✅ Complete | [Guide](./features/SELF_HEALING_LOCATORS.md) |
| Wild-Card XPath | ✅ Complete | [Guide](./features/WILDCARD_XPATH.md) |
| Visual Test Builder | ✅ Complete | [Guide](./features/VISUAL_TEST_BUILDER.md) |
| Checkpoint Wizard | ✅ Complete | [Guide](./features/CHECKPOINT_WIZARD.md) |
| Screenshot on Failure | ✅ Complete | [Guide](./features/SCREENSHOT_ON_FAILURE.md) |
| OCR DataGrid | 🔄 In Progress | - |

---

## 📝 Document Categories

### 1. Guides (User-Facing)
Step-by-step instructions for using the framework.

### 2. Features (In-Depth)
Detailed documentation of individual features.

### 3. Technical (Developer-Facing)
Architecture, design, and implementation details.

### 4. Planning (Strategic)
Gap analysis, roadmaps, and improvement suggestions.

### 5. Testing (QA)
Test scenarios and validation procedures.

### 6. Deployment (Operations)
Installation, configuration, and maintenance.

---

## 🔍 Search Tips

- **New to the framework?** Start with [Getting Started](./guides/GETTING_STARTED.md)
- **Recording not working?** See [Recorder Guide](./guides/RECORDER_GUIDE.md)
- **Elements not found?** Check [Self-Healing Locators](./features/SELF_HEALING_LOCATORS.md)
- **Want to contribute?** Read [Contributing](./deployment/CONTRIBUTING.md)

---

## 📞 Support

- **Issues**: Report bugs via GitHub Issues
- **Questions**: Use GitHub Discussions
- **Contributing**: See [Contributing Guide](./deployment/CONTRIBUTING.md)

---

## 📄 License

This project is open source. See the main repository for license details.

---

*Last updated: 2024*
