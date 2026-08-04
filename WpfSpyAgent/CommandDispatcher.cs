using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Threading;
using System.Collections.Generic;
using System.Linq;
using Tesseract;
using WpfSpyAgent.Protocol;

namespace WpfSpyAgent
{
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
                    var element = RequireElement(request.Name, request.XPath);
                    return SpyResponse.Ok(VisualTreeInspector.IsVisible(element) ? "true" : "false");
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
                case "GetMainWindowTitle":
                {
                    string title = Application.Current.MainWindow?.Title ?? "(no main window)";
                    return SpyResponse.Ok(title);
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
                     var element = RequireElement(request.Name, request.XPath);
                     var base64 = VisualTreeInspector.GetDataGridScreenshot(element);
                     if (base64.StartsWith("{\"error\""))
                         return SpyResponse.Ok(base64);
                     try
                     {
                         var imageBytes = Convert.FromBase64String(base64);
                         using var img = Pix.LoadFromMemory(imageBytes);
                         var tessdataPath = System.IO.Path.Combine(
                             AppDomain.CurrentDomain.BaseDirectory, "tessdata");
                         if (!System.IO.Directory.Exists(tessdataPath))
                             tessdataPath = System.IO.Path.Combine(
                                 AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "tessdata");
                         using var engine = new TesseractEngine(tessdataPath, "eng", EngineMode.Default);
                         using var page = engine.Process(img);
                         var ocrText = page.GetText();
                         var csv = VisualTreeInspector.OcrTextToCsv(ocrText);
                         return SpyResponse.Ok(csv);
                     }
                     catch (Exception ex)
                     {
                         return SpyResponse.Fail(
                             "OCR failed: " + ex.Message +
                             "\nEnsure tessdata/eng.traineddata is available.");
                     }
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
                case "CaptureArea":
                {
                    var x = request.X ?? 0;
                    var y = request.Y ?? 0;
                    var width = request.Width ?? 100;
                    var height = request.Height ?? 100;
                    
                    try
                    {
                        // Capture screenshot of the specified area
                        using var bitmap = new System.Drawing.Bitmap(width, height);
                        using var graphics = System.Drawing.Graphics.FromImage(bitmap);
                        graphics.CopyFromScreen((int)x, (int)y, 0, 0, new System.Drawing.Size(width, height));
                        
                        using var ms = new System.IO.MemoryStream();
                        bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                        var base64 = Convert.ToBase64String(ms.ToArray());
                        return SpyResponse.Ok(base64);
                    }
                    catch (Exception ex)
                    {
                        return SpyResponse.Fail($"CaptureArea failed: {ex.Message}");
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



















