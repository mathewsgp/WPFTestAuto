# Sikuli Driver — Image-Based Fallback

## What it is

Sikuli is the **last-resort driver** in the WPFTestAuto fallback chain:
`FlaUI (UIA3) -> WPFSpy (in-process visual tree) -> Sikuli (image match)`.

It is used for:
- Custom-rendered controls (DirectX, GDI, third-party skins) that UI Automation cannot see.
- WPF apps whose developers did not set `AutomationProperties.AutomationId`.
- Situations where the existing drivers are blocked by a transient rendering glitch.

The driver works by:
1. Capturing a screenshot of the screen (or the AUT window) via `mss` or Win32 `PrintWindow`.
2. Matching a reference PNG (captured at recording time) against that screenshot using OpenCV `TM_CCOEFF_NORMED`.
3. Clicking / typing at the matched center using `pyautogui` (with `pyperclip` + Ctrl-V for Unicode-safe text entry).

## Install

```bat
pip install opencv-python numpy mss pyautogui pyperclip pytesseract pillow
```

| Package | Why |
|---|---|
| `opencv-python` | Template matching (`cv2.matchTemplate`). |
| `numpy` | Array format for BGR images (transitive from opencv). |
| `mss` | Cross-platform full-desktop screen capture. |
| `pyautogui` | Mouse/keyboard input at matched coordinates. |
| `pyperclip` | Clipboard-based text entry (Unicode-safe, faster than `pyautogui.write`). |
| `pytesseract` | OCR fallback for `get_text` and DataGrid content. |
| `pillow` | PIL Image bridge required by pytesseract. |

Optional: `tesseract` OCR engine binary from https://github.com/tesseract-ocr/tesseract.

## Recording a Sikuli template

1. In the IDE status bar, check **Record Sikuli** (`chkRecordSikuli`).
2. Click any element in the target app.
3. The IDE calls `WpfSpyAgent.CaptureElement`, which:
   - resolves the element by XPath (or WPF Name),
   - computes its on-screen rect with a small padding (default 4 px),
   - captures that rect via GDI `BitBlt`,
   - returns the PNG as base64 inside a JSON payload.
4. The IDE writes the PNG to `repository/sikuli/<safe-alias>.png` and emits a Sikuli strategy in the repository YAML:

```yaml
strategies:
  Sikuli:
    - searchBy: Image
      value: sikuli/SampleWpfApp.OrdersWindow.btnLogout.png
      imagePath: sikuli/SampleWpfApp.OrdersWindow.btnLogout.png
      similarity: 0.85
```

## Runtime config

All knobs are environment variables (so they work from Robot `*** Variables ***` or the shell):

| Variable | Default | Meaning |
|---|---|---|
| `SIKULI_MATCHER` | `opencv` (falls back to `stub`) | `stub`, `opencv`, or `multi`. |
| `SIKULI_SCALES` | `0.9,1.0,1.1` | Comma-separated scales for `MultiScaleImageMatcher`. |
| `SIKULI_SCREEN_CAPTURE` | `mss` (falls back to `stub`) | `mss`, `printwindow`, or `stub`. |
| `SIKULI_USE_MOCK` | auto | `1` forces the in-repo mock-app stub; `0` forces the real driver. |

### Per-element similarity override

The YAML strategy can carry a `similarity` field (default `0.85`):

```yaml
Sikuli:
  - searchBy: Image
    value: sikuli/MyIcon.png
    similarity: 0.92   # stricter — use for noisy backgrounds
```

The driver descends a threshold ladder on miss:
`similarity` → `similarity - 0.05` → `similarity - 0.10`.

### Pre-click stability

Before every real click the driver waits up to 250 ms for the element region to stabilize (two consecutive grabs differ by < 2.0 mean-abs pixel value). This avoids the "clicked during repaint" race on WPF.

## Playback

At runtime, the framework resolves the Sikuli strategy, loads the template PNG, captures the screen, runs `cv2.matchTemplate`, and clicks the best match above threshold.

If the match score drops across runs, the healing store records `last_image_match_score` and `min_image_match_score` per strategy so regressions are diagnosable from the HTML report.

## Known limits

- Screen-resolution / DPI changes between recording and replay can break the match. Use `MultiScaleImageMatcher` (the default) or retrain the template at the target DPI.
- The driver is screen-dependent: minimized or occluded windows won't match. Use `PrintWindowScreenCapture` (set `SIKULI_SCREEN_CAPTURE=printwindow` on Windows) when the AUT is often hidden.
- OCR (`pytesseract`) is best-effort; accuracy depends on font, size, and anti-aliasing. For production-grade DataGrid reading, prefer `FlaUI` or `WPFSpy`.

## File layout

```
repository/
  sikuli/
    SampleWpfApp.OrdersWindow.btnLogout.png
    SampleWpfApp.LoginPage.txtUsername.png
```

Template names are the sanitized alias plus `.png`. The recorder creates the directory automatically.
