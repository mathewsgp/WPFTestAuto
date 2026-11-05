using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Threading;
using System.Collections.Generic;
using System.Linq;
using WpfSpyAgent.Protocol;

namespace WpfSpyAgent
{
    /// <summary>
    /// P/Invoke declarations for screen capture.
    /// </summary>
    internal static class NativeMethods
    {
        [DllImport("gdi32.dll")]
        internal static extern bool BitBlt(IntPtr hdcDest, int xDest, int yDest, int wDest, int hDest,
            IntPtr hdcSource, int xSrc, int ySrc, int rop);

        [DllImport("gdi32.dll")]
        internal static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        internal static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int width, int height);

        [DllImport("gdi32.dll")]
        internal static extern IntPtr SelectObject(IntPtr hdc, IntPtr hObject);

        [DllImport("gdi32.dll")]
        internal static extern bool DeleteObject(IntPtr hObject);

        [DllImport("gdi32.dll")]
        internal static extern bool DeleteDC(IntPtr hdc);

        [DllImport("user32.dll")]
        internal static extern IntPtr GetDesktopWindow();

        [DllImport("user32.dll")]
        internal static extern IntPtr GetWindowDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        internal static extern bool ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("user32.dll")]
        internal static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        internal const int SRCCOPY = 0x00CC0020;
    }

    /// <summary>
    /// Parses one JSON request line, finds the target element fresh from
    /// the live visual tree, performs the requested action, and returns
    /// one JSON response line. Called on the WPF UI thread by
    /// SpyAgentHost — all visual-tree access must happen there.
    /// </summary>
    public static class CommandDispatcher
    {
        private static readonly string _logPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "agent_probe_log.txt");
        private static UiaEventRecorder? _recorder;

        private static void Log(string message)
        {
            try
            {
                System.IO.File.AppendAllText(_logPath, $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}");
            }
            catch { }
        }

        static CommandDispatcher()
        {
            try
            {
                System.IO.File.AppendAllText(_logPath, $"[{DateTime.Now:HH:mm:ss.fff}] CommandDispatcher static ctor, BaseDirectory={AppDomain.CurrentDomain.BaseDirectory}{Environment.NewLine}");
            }
            catch { }
        }

        public static string Dispatch(string requestJson)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Log($"Dispatch start: {requestJson.Substring(0, Math.Min(200, requestJson.Length))}");
            SpyRequest? request;
            try
            {
                request = JsonHelper.Deserialize<SpyRequest>(requestJson);
            }
            catch (Exception ex)
            {
                sw.Stop();
                Log($"malformed JSON in {sw.ElapsedMilliseconds}ms: {ex.Message}");
                return Serialize(SpyResponse.Fail($"Malformed request JSON: {ex.Message}"));
            }

            if (request is null)
            {
                sw.Stop();
                Log($"null request in {sw.ElapsedMilliseconds}ms");
                return Serialize(SpyResponse.Fail("Malformed request: empty payload"));
            }

            try
            {
                var response = Execute(request);
                sw.Stop();
                Log($"{request.Command} completed in {sw.ElapsedMilliseconds}ms");
                return Serialize(response);
            }
            catch (Exception ex)
            {
                sw.Stop();
                Log($"{request.Command} exception after {sw.ElapsedMilliseconds}ms: {ex.GetType().Name}: {ex.Message}");
                return Serialize(SpyResponse.Fail(ex.Message));
            }
        }

        private static SpyResponse Execute(SpyRequest request)
        {
            switch (request.Command)
            {
                case "Find":
                {
                    var element = RequireElement(request.Name, request.XPath);
                    return SpyResponse.Ok();
                }
                case "Invoke":
                {
                    var element = RequireElement(request.Name, request.XPath);
                    VisualTreeInspector.Invoke(element);
                    return SpyResponse.Ok();
                }
                case "SetValue":
                {
                    var element = RequireElement(request.Name, request.XPath);
                    VisualTreeInspector.SetValue(element, request.Value ?? "");
                    return SpyResponse.Ok();
                }
                case "GetText":
                {
                    var element = RequireElement(request.Name, request.XPath);
                    return SpyResponse.Ok(VisualTreeInspector.GetText(element));
                }
                case "IsVisible":
                {
                    FrameworkElement? element = null;
                    string? error = null;
                    try
                    {
                        element = RequireElement(request.Name, request.XPath);
                    }
                    catch (Exception ex)
                    {
                        error = ex.Message;
                    }
                    if (element is null)
                    {
                        return SpyResponse.Fail(error ?? "Element not found");
                    }
                    bool isVisible = VisualTreeInspector.IsVisible(element);
                    return SpyResponse.Ok(isVisible ? "true" : "false");
                }
                case "IsEnabled":
                {
                    FrameworkElement? element = null;
                    string? error = null;
                    try
                    {
                        element = RequireElement(request.Name, request.XPath);
                    }
                    catch (Exception ex)
                    {
                        error = ex.Message;
                    }
                    if (element is null)
                    {
                        return SpyResponse.Fail(error ?? "Element not found");
                    }
                    bool isEnabled = VisualTreeInspector.IsEnabled(element);
                    return SpyResponse.Ok(isEnabled ? "true" : "false");
                }
                case "GetAttribute":
                {
                    var element = RequireElement(request.Name, request.XPath);
                    var value = VisualTreeInspector.GetAttribute(element, request.AttributeName ?? "");
                    return SpyResponse.Ok(value);
                }
                case "DoubleClick":
                {
                    var element = RequireElement(request.Name, request.XPath);
                    VisualTreeInspector.DoubleClick(element);
                    return SpyResponse.Ok();
                }
                case "RightClick":
                {
                    var element = RequireElement(request.Name, request.XPath);
                    VisualTreeInspector.RightClick(element);
                    return SpyResponse.Ok();
                }
                case "PressKeys":
                {
                    var element = RequireElement(request.Name, request.XPath);
                    VisualTreeInspector.PressKeys(element, request.Value ?? "");
                    return SpyResponse.Ok();
                }
                case "DragDrop":
                {
                    var element = RequireElement(request.Name, request.XPath);
                    string? targetName = request.TargetName;
                    string? targetXPath = request.TargetXPath;
                    FrameworkElement? targetElement = null;
                    if (!string.IsNullOrEmpty(targetXPath))
                    {
                        targetElement = VisualTreeInspector.FindByXPath(targetXPath);
                    }
                    if (targetElement == null && !string.IsNullOrEmpty(targetName))
                    {
                        targetElement = VisualTreeInspector.FindByName(targetName);
                    }
                    if (targetElement == null)
                    {
                        return SpyResponse.Fail("DragDrop target not found");
                    }
                    VisualTreeInspector.DragDrop(element, targetElement);
                    return SpyResponse.Ok();
                }
                case "Hover":
                {
                    var element = RequireElement(request.Name, request.XPath);
                    VisualTreeInspector.Hover(element);
                    return SpyResponse.Ok();
                }
                case "Scroll":
                {
                    var element = RequireElement(request.Name, request.XPath);
                    VisualTreeInspector.Scroll(element, request.Value ?? "");
                    return SpyResponse.Ok();
                }
                case "CaptureScreenshot":
                {
                    var element = RequireElement(request.Name, request.XPath);
                    var bytes = VisualTreeInspector.CaptureScreenshot(element);
                    var base64 = Convert.ToBase64String(bytes);
                    return SpyResponse.Ok(base64);
                }
                case "Toggle":
                {
                    var element = RequireElement(request.Name, request.XPath);
                    VisualTreeInspector.Toggle(element);
                    return SpyResponse.Ok();
                }
                case "ProbeAt":
                {
                    if (request.X is null || request.Y is null)
                    {
                        return SpyResponse.Fail("ProbeAt requires 'x' and 'y'");
                    }
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    var element = VisualTreeInspector.FindByScreenPoint(request.X.Value, request.Y.Value);
                    sw.Stop();
                    Log($"FindByScreenPoint({request.X},{request.Y}) -> {element?.GetType().Name} name={element?.Name} in {sw.ElapsedMilliseconds}ms");
                    if (element is null)
                    {
                        return SpyResponse.Fail($"No named element found at screen point ({request.X}, {request.Y})");
                    }
                    string? automationId = System.Windows.Automation.AutomationProperties.GetAutomationId(element);
                    string? text = null;
                    sw.Restart();
                    try { text = VisualTreeInspector.GetText(element); } catch { /* not all controls have text */ }
                    sw.Stop();
                    Log($"GetText completed in {sw.ElapsedMilliseconds}ms");

                    sw.Restart();
                    var xpath = VisualTreeInspector.BuildXPath(element);
                    sw.Stop();
                    Log($"BuildXPath completed in {sw.ElapsedMilliseconds}ms");

                    var probe = new ProbeResult
                    {
                        Name = element.Name,
                        AutomationId = string.IsNullOrEmpty(automationId) ? null : automationId,
                        ControlType = element.GetType().Name,
                        Text = text,
                        XPath = xpath,
                    };
                    return SpyResponse.Ok(JsonHelper.Serialize(probe));
                }
                case "FindByXPath":
                {
                    if (string.IsNullOrEmpty(request.XPath))
                    {
                        return SpyResponse.Fail("FindByXPath requires 'xpath'");
                    }
                    var element = VisualTreeInspector.FindByXPath(request.XPath);
                    if (element is null)
                    {
                        return SpyResponse.Fail($"No element found for XPath: {request.XPath}");
                    }
                    return SpyResponse.Ok();
                }
                case "FindByAutomationId":
                {
                    if (string.IsNullOrEmpty(request.AutomationId))
                    {
                        return SpyResponse.Fail("FindByAutomationId requires 'automationId'");
                    }
                    var element = VisualTreeInspector.FindByAutomationId(request.AutomationId);
                    if (element is null)
                    {
                        return SpyResponse.Fail($"No element found for AutomationId: {request.AutomationId}");
                    }
                    return SpyResponse.Ok();
                }
                case "GetMainWindowTitle":
                {
                    string title = "(no main window)";
                    foreach (Window w in Application.Current.Windows)
                    {
                        if (w.IsVisible && !string.IsNullOrEmpty(w.Title))
                        {
                            title = w.Title;
                            break;
                        }
                    }
                    return SpyResponse.Ok(title);
                }
                case "GetMainWindow":
                {
                    // Returns JSON with the main window's AutomationId, Name,
                    // and Title so the recorder can pick the best window-level
                    // identifier for alias generation. Format:
                    //   { "automationId": "...", "name": "...", "title": "..." }
                    string title = "(no main window)";
                    string? automationId = null;
                    string? name = null;
                    foreach (Window w in Application.Current.Windows)
                    {
                        if (!w.IsVisible) continue;
                        if (string.IsNullOrEmpty(title) || title == "(no main window)")
                        {
                            title = w.Title ?? "";
                        }
                        if (automationId is null)
                        {
                            try { automationId = System.Windows.Automation.AutomationProperties.GetAutomationId(w); } catch { }
                        }
                        if (name is null)
                        {
                            try { name = w.Name; } catch { }
                        }
                        if (!string.IsNullOrEmpty(title) && title != "(no main window)"
                            && !string.IsNullOrEmpty(automationId))
                        {
                            break;
                        }
                    }
                    var payload = new Dictionary<string, object?>
                    {
                        ["automationId"] = string.IsNullOrEmpty(automationId) ? null : automationId,
                        ["name"] = string.IsNullOrEmpty(name) ? null : name,
                        ["title"] = title,
                    };
                    return SpyResponse.Ok(JsonHelper.Serialize(payload));
                }
                case "GetBounds":
                {
                    FrameworkElement? element = null;
                    if (!string.IsNullOrEmpty(request.Name))
                    {
                        element = VisualTreeInspector.FindByName(request.Name);
                    }
                    if (element is null && !string.IsNullOrEmpty(request.XPath))
                    {
                        element = VisualTreeInspector.FindByXPath(request.XPath);
                    }
                    if (element is null)
                    {
                        return SpyResponse.Fail("Element not found for bounds");
                    }

                    try
                    {
                        var bounds = GetElementScreenBounds(element);
                        var json = JsonHelper.Serialize(bounds);
                        return SpyResponse.Ok(json);
                    }
                    catch (Exception ex)
                    {
                        Log($"GetBounds exception: {ex.GetType().Name}: {ex.Message}");
                        return SpyResponse.Fail($"GetBounds failed: {ex.Message}");
                    }
                }
                case "Highlight":
                {
                    FrameworkElement? element = null;
                    if (!string.IsNullOrEmpty(request.Name))
                    {
                        element = VisualTreeInspector.FindByName(request.Name);
                    }
                    if (element is null && !string.IsNullOrEmpty(request.XPath))
                    {
                        element = VisualTreeInspector.FindByXPath(request.XPath);
                    }
                    if (element is null)
                    {
                        return SpyResponse.Fail("Element not found for highlight");
                    }

                    try
                    {
                        var bounds = GetElementScreenBounds(element);
                        DrawHighlightRect(bounds);
                    }
                    catch (Exception ex)
                    {
                        Log($"Highlight exception: {ex.GetType().Name}: {ex.Message}");
                    }

                    return SpyResponse.Ok($"Highlighted {element.GetType().Name} name={element.Name}");
                }
case "GetDataGridContent":
                 {
                     var element = RequireElement(request.Name, request.XPath);
                     var json = VisualTreeInspector.GetDataGridContent(element);
                     return SpyResponse.Ok(json);
                 }
                 case "GetDataGridScreenshot":
                 {
                     var element = RequireElement(request.Name, request.XPath);
                     var base64 = VisualTreeInspector.GetDataGridScreenshot(element);
                     return SpyResponse.Ok(base64);
                 }
                 case "GetDataGridContentOcr":
                 {
                     return SpyResponse.Fail(
                         "OCR is not available. Tesseract package is not installed. " +
                         "To enable OCR, add the Tesseract NuGet package and ensure tessdata folder exists.");
                 }
                 case "ResetState":
                {
                    ResetAppState();
                    return SpyResponse.Ok();
                }
                // --- UIA Event Recording Commands ---
                case "StartRecording":
                {
                    if (_recorder == null)
                    {
                        _recorder = new UiaEventRecorder();
                    }
                    _recorder.StartRecording();
                    return SpyResponse.Ok("Recording started");
                }
                case "StopRecording":
                {
                    if (_recorder != null)
                    {
                        _recorder.StopRecording();
                    }
                    return SpyResponse.Ok("Recording stopped");
                }
                case "GetRecordedEvents":
                {
                    if (_recorder == null)
                    {
                        return SpyResponse.Fail("No recorder initialized. Call StartRecording first.");
                    }
                    var export = _recorder.Export();
                    return SpyResponse.Ok(JsonHelper.Serialize(export));
                }
                case "GetRecordingStatus":
                {
                    if (_recorder == null)
                    {
                        return SpyResponse.Ok(JsonHelper.Serialize(new { isRecording = false, eventCount = 0 }));
                    }
                    var status = new
                    {
                        isRecording = _recorder.IsRecording,
                        eventCount = _recorder.EventCount
                    };
                    return SpyResponse.Ok(JsonHelper.Serialize(status));
                }
                case "ClearRecording":
                {
                    if (_recorder != null)
                    {
                        _recorder.ClearEvents();
                    }
                    return SpyResponse.Ok("Recording cleared");
                }
                case "GetFullTree":
                {
                    // Find the active/topmost visible window for this process.
                    // Application.Current.MainWindow is the startup window and
                    // does not change when secondary windows (e.g. OrdersWindow)
                    // are opened, so we resolve the foreground window instead.
                    Window? targetWindow = null;
                    
                    try
                    {
                        var foregroundHwnd = NativeMethods.GetForegroundWindow();
                        if (foregroundHwnd != IntPtr.Zero)
                        {
                            uint targetPid;
                            NativeMethods.GetWindowThreadProcessId(foregroundHwnd, out targetPid);
                            int currentPid = System.Diagnostics.Process.GetCurrentProcess().Id;
                            
                            if ((int)targetPid == currentPid)
                            {
                                foreach (Window window in System.Windows.Application.Current.Windows)
                                {
                                    if (window.IsVisible && new System.Windows.Interop.WindowInteropHelper(window).Handle == foregroundHwnd)
                                    {
                                        targetWindow = window;
                                        break;
                                    }
                                }
                            }
                        }
                    }
                    catch { }

                    if (targetWindow == null)
                    {
                        foreach (Window window in System.Windows.Application.Current.Windows)
                        {
                            if (window.IsVisible)
                            {
                                targetWindow = window;
                                break;
                            }
                        }
                    }

                    if (targetWindow == null)
                    {
                        return SpyResponse.Fail("No visible window found");
                    }

                    var treeData = new
                    {
                        nodes = new[] { VisualTreeInspector.BuildElementTree(targetWindow) }
                    };
                    return SpyResponse.Ok(JsonHelper.Serialize(treeData));
                }
                case "CaptureArea":
                {
                    var x = (int)(request.X ?? 0);
                    var y = (int)(request.Y ?? 0);
                    var width = (int)(request.Width ?? 100);
                    var height = (int)(request.Height ?? 100);

                    try
                    {
                        var cap = CaptureScreenRegion(x, y, width, height);
                        return SpyResponse.Ok(cap.Base64);
                    }
                    catch (Exception ex)
                    {
                        return SpyResponse.Fail($"CaptureArea failed: {ex.Message}");
                    }
                }
                case "CaptureElement":
                {
                    // Used by the WpfTestIde recorder when RecordSikuli is
                    // on. Locates the element by name (FrameworkElement.Name)
                    // or by XPath, computes its on-screen rect (with a few
                    // pixels of padding so the template matcher has
                    // whitespace margin), captures that rect, and returns
                    // both the PNG (base64) and the rect in one JSON
                    // payload.
                    int padding = (int)(request.Width ?? 4);
                    try
                    {
                        var element = ResolveElementForCapture(request);
                        if (element is null)
                        {
                            return SpyResponse.Fail(
                                "CaptureElement: element not found (need name or xpath)");
                        }

                        var rect = ComputeElementScreenRect(element, padding);
                        if (rect.Width <= 0 || rect.Height <= 0)
                        {
                            return SpyResponse.Fail("CaptureElement: element has zero size on screen");
                        }

                        var cap = CaptureScreenRegion(rect.X, rect.Y, rect.Width, rect.Height);
                        var payload = new
                        {
                            pngBase64 = cap.Base64,
                            x = rect.X,
                            y = rect.Y,
                            width = rect.Width,
                            height = rect.Height,
                            controlType = element.GetType().Name,
                            name = element is FrameworkElement fe ? fe.Name : "",
                            automationId = (element is FrameworkElement fe2
                                ? System.Windows.Automation.AutomationProperties.GetAutomationId(fe2)
                                : null),
                        };
                        return SpyResponse.Ok(WpfSpyAgent.JsonHelper.Serialize(payload));
                    }
                    catch (Exception ex)
                    {
                        return SpyResponse.Fail($"CaptureElement failed: {ex.GetType().Name}: {ex.Message}");
                    }
                }
                // --- End UIA Event Recording Commands ---
                default:
                    return SpyResponse.Fail($"Unknown command '{request.Command}'");
            }
        }

        private static System.Windows.FrameworkElement RequireElement(string? name, string? xpath)
        {
            if (!string.IsNullOrEmpty(xpath))
            {
                var element = VisualTreeInspector.FindByXPath(xpath);
                if (element is null)
                {
                    throw new InvalidOperationException($"No element found for XPath: {xpath}");
                }
                return element;
            }

            if (string.IsNullOrEmpty(name))
            {
                throw new InvalidOperationException("Request is missing both 'name' and 'xpath'");
            }
            var namedElement = VisualTreeInspector.FindByName(name);
            if (namedElement is null)
            {
                throw new InvalidOperationException($"No element with Name='{name}' found in the current visual tree");
            }
            return namedElement;
        }

        /// <summary>
        /// Resolves the element addressed by a SpyRequest for the
        /// CaptureElement command. Prefers XPath when supplied, otherwise
        /// falls back to the element's WPF Name. Returns null when neither
        /// resolves (the caller turns that into a clean Fail).
        /// </summary>
        private static System.Windows.FrameworkElement? ResolveElementForCapture(SpyRequest request)
        {
            if (!string.IsNullOrEmpty(request.XPath))
            {
                return VisualTreeInspector.FindByXPath(request.XPath);
            }
            if (!string.IsNullOrEmpty(request.Name))
            {
                return VisualTreeInspector.FindByName(request.Name);
            }
            return null;
        }

        /// <summary>
        /// Returns the on-screen bounding rect of a FrameworkElement with
        /// `padding` pixels of whitespace on every side so the resulting
        /// template has margin around the control — that's the single
        /// biggest accuracy boost for TM_CCOEFF_NORMED matching.
        /// </summary>
        private static System.Windows.Rect ComputeElementScreenRect(
            System.Windows.FrameworkElement element,
            int padding)
        {
            try
            {
                var topLeft = element.PointToScreen(new System.Windows.Point(0, 0));
                var bottomRight = element.PointToScreen(
                    new System.Windows.Point(element.ActualWidth, element.ActualHeight));
                double x = topLeft.X - padding;
                double y = topLeft.Y - padding;
                double w = (bottomRight.X - topLeft.X) + (padding * 2);
                double h = (bottomRight.Y - topLeft.Y) + (padding * 2);
                if (w < 1) w = 1;
                if (h < 1) h = 1;
                return new System.Windows.Rect(x, y, w, h);
            }
            catch
            {
                return new System.Windows.Rect(0, 0, 0, 0);
            }
        }

        /// <summary>
        /// Captures the given screen rectangle via the same GDI path used
        /// by CaptureArea and returns the PNG as base64 plus the raw
        /// byte count. The two-output shape lets callers embed the bytes
        /// in JSON without re-decoding.
        /// </summary>
        private static CaptureResult CaptureScreenRegion(
            double x, double y, double width, double height)
        {
            int ix = (int)System.Math.Round(x);
            int iy = (int)System.Math.Round(y);
            int iw = (int)System.Math.Round(width);
            int ih = (int)System.Math.Round(height);

            var hDesk = NativeMethods.GetDesktopWindow();
            var hSrce = NativeMethods.GetWindowDC(hDesk);
            var hDest = NativeMethods.CreateCompatibleDC(hSrce);
            var hBmp = NativeMethods.CreateCompatibleBitmap(hSrce, iw, ih);
            var hOld = NativeMethods.SelectObject(hDest, hBmp);

            NativeMethods.BitBlt(hDest, 0, 0, iw, ih, hSrce, ix, iy, NativeMethods.SRCCOPY);
            NativeMethods.SelectObject(hDest, hOld);

            var sourceBitmap = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                hBmp, IntPtr.Zero, System.Windows.Int32Rect.Empty,
                System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());

            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(sourceBitmap));

            byte[] png;
            using (var ms = new System.IO.MemoryStream())
            {
                encoder.Save(ms);
                png = ms.ToArray();
            }

            NativeMethods.DeleteObject(hBmp);
            NativeMethods.DeleteDC(hDest);
            NativeMethods.ReleaseDC(hDesk, hSrce);

            return new CaptureResult
            {
                Base64 = Convert.ToBase64String(png),
                Bytes = png.Length,
            };
        }

        private sealed class CaptureResult
        {
            public string Base64 { get; set; } = "";
            public int Bytes { get; set; }
        }

        private static void ResetAppState()
        {
            // If OrdersWindow is open, click Logout to return to login page
            foreach (Window window in Application.Current.Windows)
            {
                var ordersWindowType = window.GetType();
                if (ordersWindowType.Name == "OrdersWindow")
                {
                    try
                    {
                        var logoutButton = window.FindName("btnLogout") as System.Windows.Controls.Button;
                        if (logoutButton != null)
                        {
                            logoutButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                            Thread.Sleep(1000); // Wait for logout to complete
                        }
                    }
                    catch { /* ignore logout errors */ }
                    break;
                }
            }

            // Ensure MainWindow exists
            var mainWindow = Application.Current.MainWindow;
            if (mainWindow == null || !mainWindow.IsVisible)
            {
                try
                {
                    var mainWindowType = Type.GetType("SampleWpfApp.MainWindow, SampleWpfApp");
                    if (mainWindowType != null)
                    {
                        var newMainWindow = (Window)Activator.CreateInstance(mainWindowType);
                        newMainWindow.Show();
                        Application.Current.MainWindow = newMainWindow;
                        mainWindow = newMainWindow;
                    }
                }
                catch { /* ignore recreation errors */ }
            }

            // Close any remaining secondary windows
            foreach (Window window in Application.Current.Windows)
            {
                if (window != mainWindow)
                {
                    try
                    {
                        window.Close();
                    }
                    catch { /* ignore close errors */ }
                }
            }

            // Bring main window to front
            if (mainWindow != null)
            {
                mainWindow.WindowState = WindowState.Normal;
                mainWindow.Activate();
                mainWindow.Topmost = true;
                mainWindow.Topmost = false;
            }
        }

        [DllImport("user32.dll")]
        private static extern bool FlashWindowEx(ref FLASHWINFO pwfi);

        [StructLayout(LayoutKind.Sequential)]
        private struct FLASHWINFO
        {
            public uint cbSize;
            public IntPtr hwnd;
            public uint dwFlags;
            public uint uCount;
            public uint dwTimeout;
        }

        private const uint FLASHW_ALL = 3;
        private const uint FLASHW_TIMERNOFG = 12;

        private static void FlashMainWindow()
        {
            var mainWindow = Application.Current.MainWindow;
            if (mainWindow == null)
            {
                return;
            }

            var helper = new System.Windows.Interop.WindowInteropHelper(mainWindow);
            IntPtr hWnd = helper.Handle;
            if (hWnd == IntPtr.Zero)
            {
                return;
            }

            var fi = new FLASHWINFO
            {
                cbSize = (uint)Marshal.SizeOf<FLASHWINFO>(),
                hwnd = hWnd,
                dwFlags = FLASHW_ALL | FLASHW_TIMERNOFG,
                uCount = 2,
                dwTimeout = 0,
            };

            FlashWindowEx(ref fi);
        }

        
        private class ElementBounds
        {
            public int X { get; set; }
            public int Y { get; set; }
            public int Width { get; set; }
            public int Height { get; set; }
        }

        private static ElementBounds GetElementScreenBounds(FrameworkElement element)
        {
            var bounds = new ElementBounds();
            
            // Get element's position relative to its window
            var position = element.PointToScreen(new Point(0, 0));
            
            // Get the actual size
            bounds.X = (int)position.X;
            bounds.Y = (int)position.Y;
            bounds.Width = (int)element.ActualWidth;
            bounds.Height = (int)element.ActualHeight;
            
            return bounds;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreatePen(int fnPenStyle, int nWidth, uint crColor);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

        [DllImport("gdi32.dll")]
        private static extern bool Rectangle(IntPtr hdc, int nLeftRect, int nTopRect, int nRightRect, int nBottomRect);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        [DllImport("user32.dll")]
        private static extern bool InvalidateRect(IntPtr hWnd, IntPtr lpRect, bool bErase);

        private const int PS_SOLID = 0;
        private const uint RGB_RED = 0x000000FF;

        private static void DrawHighlightRect(ElementBounds bounds)
        {
            // Use a transparent topmost overlay window to show the highlight.
            // This avoids raw GDI screen drawing, which gets erased by repaints.
            var overlay = new System.Windows.Window
            {
                WindowStyle = System.Windows.WindowStyle.None,
                AllowsTransparency = true,
                Background = System.Windows.Media.Brushes.Transparent,
                Topmost = true,
                ShowInTaskbar = false,
                Width = bounds.Width + 6,
                Height = bounds.Height + 6,
                Left = bounds.X - 3,
                Top = bounds.Y - 3,
            };

            var border = new System.Windows.Controls.Border
            {
                BorderBrush = System.Windows.Media.Brushes.Red,
                BorderThickness = new System.Windows.Thickness(3),
                Background = System.Windows.Media.Brushes.Transparent
            };
            overlay.Content = border;

            overlay.Show();
            
            // Close the overlay after a short delay without blocking the UI thread.
            System.Windows.Threading.Dispatcher dispatcher = overlay.Dispatcher;
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(1200)
            };
            timer.Tick += (_, __) =>
            {
                timer.Stop();
                overlay.Close();
            };
            timer.Start();
        }

        private static string Serialize(SpyResponse response) => JsonHelper.Serialize(response);
    }
}



















