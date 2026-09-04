using System;
using System.Text;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;

namespace WpfSpyAgent
{
    /// <summary>
    /// Walks the LIVE WPF visual tree directly (VisualTreeHelper) and acts
    /// on controls via their own real WPF APIs — NOT via UI Automation.
    /// This is what lets WPFSpy reach controls FlaUI/UIA can't: it doesn't
    /// need an AutomationPeer to exist at all. Standard controls
    /// (TextBox, ButtonBase, ComboBox, ContentControl, DataGrid) are
    /// handled directly; any control implementing <see
    /// cref="ISpyInteractable"/> is handled via that contract instead,
    /// which is how custom-rendered controls (see
    /// SampleWpfApp/CustomControls/PriorityToggleControl.cs) opt in
    /// without needing a full AutomationPeer implementation.
    /// </summary>
    public static class VisualTreeInspector
    {
        private static readonly System.Reflection.MethodInfo? _spyInvokeMethod;
        private static readonly System.Reflection.MethodInfo? _spySetValueMethod;
        private static readonly System.Reflection.MethodInfo? _spyGetTextMethod;
        private static readonly string _logPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "agent_probe_log.txt");
        private static readonly object _logLock = new();
        private const int MaxLogFileSizeBytes = 5 * 1024 * 1024;
        private const int MaxBackupLogFiles = 3;
        public static bool VerboseLogging { get; set; }

        private static void Log(string message)
        {
            try
            {
                lock (_logLock)
                {
                    RotateLogIfNeeded();
                    System.IO.File.AppendAllText(_logPath, $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}");
                }
            }
            catch { }
        }

        private static void LogVerbose(string message)
        {
            if (VerboseLogging)
            {
                Log(message);
            }
        }

        private static void RotateLogIfNeeded()
        {
            try
            {
                if (!System.IO.File.Exists(_logPath)) return;
                var length = new System.IO.FileInfo(_logPath).Length;
                if (length < MaxLogFileSizeBytes) return;

                for (int i = MaxBackupLogFiles - 1; i >= 1; i--)
                {
                    string src = _logPath + "." + i;
                    string dst = _logPath + "." + (i + 1);
                    if (System.IO.File.Exists(src))
                    {
                        if (i == MaxBackupLogFiles - 1)
                            System.IO.File.Delete(dst);
                        System.IO.File.Move(src, dst);
                    }
                }
                System.IO.File.Move(_logPath, _logPath + ".1");
            }
            catch { }
        }

        static VisualTreeInspector()
        {
            try
            {
                System.IO.File.AppendAllText(_logPath, $"[{DateTime.Now:HH:mm:ss.fff}] VisualTreeInspector static ctor, BaseDirectory={AppDomain.CurrentDomain.BaseDirectory}{Environment.NewLine}");
            }
            catch { }
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var iface = asm.GetType("ISpyInteractable");
                if (iface != null)
                {
                    _spyInvokeMethod = iface.GetMethod("SpyInvoke");
                    _spySetValueMethod = iface.GetMethod("SpySetValue");
                    _spyGetTextMethod = iface.GetMethod("SpyGetText");
                    break;
                }
            }
        }

        // -----------------------------------------------------------------
        // Type classification (shared by BuildXPath generation AND
        // FindByXPath matching, so the two can never disagree about what
        // counts as an anchor, a repeating container, or a sibling index).
        // Matching is done purely by short type name (GetType().Name) --
        // no DevExpress assembly reference is required.
        // -----------------------------------------------------------------

        private static readonly System.Collections.Generic.HashSet<string> SkipLayouts =
            new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal)
        {
            "Grid", "StackPanel", "DockPanel", "WrapPanel", "Border", "ContentPresenter",
            "ScrollViewer", "ScrollContentPresenter", "AdornerDecorator", "DXBorder",
            "GridControlPanel", "CellContentPresenter", "GridCardPanel",
            "DataControlContentPresenter", "LayoutControlPanel", "EditStrategyContext",
            "Items2Panel", "DockLayoutManager", "LayoutPanel", "DocumentGroup",
            "DocumentPanel", "AutoHideGroup"
        };

        private static readonly System.Collections.Generic.HashSet<string> DevExpressRowTypes =
            new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal)
        { "RowControl", "GridRow", "RowContentPresenter" };

        private static readonly System.Collections.Generic.HashSet<string> DevExpressCellTypes =
            new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal)
        { "LightweightCellEditor", "GridCellContentPresenter", "CellEditor" };

        private static readonly System.Collections.Generic.HashSet<string> DevExpressLayoutItemTypes =
            new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal)
        { "LayoutItem", "LayoutGroup" };

        private static readonly System.Collections.Generic.HashSet<string> DevExpressTreeNodeTypes =
            new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal)
        { "TreeListNode", "TreeViewControlNode" };

        // Containers whose Name lives inside a per-instance template. A Name
        // found here can never be trusted as a global anchor -- every row in
        // a grid could have the same "PART_Editor" name.
        private static readonly System.Collections.Generic.HashSet<string> RepeatingContainerTypes =
            new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal)
        {
            "RowControl", "GridRow", "RowContentPresenter",
            "LightweightCellEditor", "GridCellContentPresenter", "CellEditor",
            "DataGridRow", "DataGridCell", "LayoutItem", "LayoutGroup",
            "ListBoxItem", "ListBoxEditItem", "TreeListNode", "TreeViewControlNode"
        };

        private static readonly System.Collections.Generic.Dictionary<string, System.Collections.Generic.HashSet<string>> ContainerInclusionRules
            = new(StringComparer.Ordinal)
        {
            ["LayoutPanel"] = new HashSet<string>(StringComparer.Ordinal) { "LayoutGroup", "LayoutItem" },
            ["DocumentPanel"] = new HashSet<string>(StringComparer.Ordinal) { "DocumentGroup" }
        };

        private enum SegmentKind : byte
        {
            Generic,
            DevExpressRow,
            DevExpressCell,
            DevExpressLayoutItem,
            DevExpressTreeNode,
            WpfGeneratorBacked // DataGridRow / DataGridCell / SelectorItem-derived / ListBoxEditItem
        }

        private readonly struct TypeProfile
        {
            public readonly bool SkipLayout;
            public readonly bool IsRepeatingContainerType;
            public readonly bool IsItemsControlType;
            public readonly SegmentKind Kind;
            public readonly string? TokenOverride;

            public TypeProfile(bool skipLayout, bool isRepeatingContainerType, bool isItemsControlType, SegmentKind kind, string? tokenOverride)
            {
                SkipLayout = skipLayout;
                IsRepeatingContainerType = isRepeatingContainerType;
                IsItemsControlType = isItemsControlType;
                Kind = kind;
                TokenOverride = tokenOverride;
            }
        }

        private static readonly ConcurrentDictionary<Type, TypeProfile> _typeProfiles = new();

        private static TypeProfile GetProfile(Type type) => _typeProfiles.GetOrAdd(type, ClassifyType);

        private static TypeProfile ClassifyType(Type type)
        {
            string typeName = type.Name;
            bool skipLayout = SkipLayouts.Contains(typeName);
            bool isRepeatingContainerType = RepeatingContainerTypes.Contains(typeName);
            bool isItemsControlType = typeof(ItemsControl).IsAssignableFrom(type);

            SegmentKind kind;
            string? tokenOverride = null;

            if (DevExpressRowTypes.Contains(typeName)) kind = SegmentKind.DevExpressRow;
            else if (DevExpressCellTypes.Contains(typeName)) kind = SegmentKind.DevExpressCell;
            else if (DevExpressLayoutItemTypes.Contains(typeName)) kind = SegmentKind.DevExpressLayoutItem;
            else if (typeName == "DataGridRow" || typeName == "DataGridCell") kind = SegmentKind.WpfGeneratorBacked;
            else if (DevExpressTreeNodeTypes.Contains(typeName)) kind = SegmentKind.DevExpressTreeNode;
            else if (typeof(ListBoxItem).IsAssignableFrom(type) || typeName == "ListBoxEditItem")
            {
                kind = SegmentKind.WpfGeneratorBacked;
                tokenOverride = typeName == "ListBoxEditItem" ? "ListBoxItem" : null;
            }
            else kind = SegmentKind.Generic;

            return new TypeProfile(skipLayout, isRepeatingContainerType, isItemsControlType, kind, tokenOverride);
        }

        // True if 'element' sits beneath a per-instance repeating template
        // (grid row/cell, list item, etc). Bounded walk, only paid when a
        // Name candidate needs verifying.
        private static bool IsInsideRepeatingContainer(DependencyObject element)
        {
            DependencyObject? node = VisualTreeHelper.GetParent(element);
            int guard = 0;
            while (node != null && !(node is Window) && guard++ < 256)
            {
                TypeProfile profile = GetProfile(node.GetType());
                if (profile.IsRepeatingContainerType) return true;
                if (profile.IsItemsControlType && ((ItemsControl)node).ItemContainerGenerator != null) return true;
                node = VisualTreeHelper.GetParent(node);
            }
            return false;
        }

        private static bool IsKnownInteractiveType(Type type)
        {
            if (typeof(ButtonBase).IsAssignableFrom(type)) return true;
            if (typeof(ToggleButton).IsAssignableFrom(type)) return true;
            if (typeof(System.Windows.Controls.ListBoxItem).IsAssignableFrom(type)) return true;
            if (typeof(System.Windows.Controls.ComboBoxItem).IsAssignableFrom(type)) return true;
            if (typeof(MenuItem).IsAssignableFrom(type)) return true;
            if (typeof(Hyperlink).IsAssignableFrom(type)) return true;
            if (typeof(TabItem).IsAssignableFrom(type)) return true;
            if (typeof(Thumb).IsAssignableFrom(type)) return true;
            if (typeof(System.Windows.Controls.DataGridRow).IsAssignableFrom(type)) return true;
            if (typeof(System.Windows.Controls.DataGridCell).IsAssignableFrom(type)) return true;
            if (typeof(TextBox).IsAssignableFrom(type)) return true;
            if (typeof(PasswordBox).IsAssignableFrom(type)) return true;
            if (typeof(ComboBox).IsAssignableFrom(type)) return true;
            if (typeof(ScrollBar).IsAssignableFrom(type)) return true;
            return false;
        }

        private static bool IsEligibleStopPoint(DependencyObject node, out bool isInteractiveType)
        {
            isInteractiveType = false;

            if (node is FrameworkElement fe && !string.IsNullOrEmpty(AutomationProperties.GetAutomationId(fe)))
            {
                return true;
            }

            foreach (var iface in node.GetType().GetInterfaces())
            {
                if (iface.Name == "ISpyInteractable")
                {
                    isInteractiveType = true;
                    return true;
                }
            }

            if (IsKnownInteractiveType(node.GetType()))
            {
                isInteractiveType = true;
                return true;
            }

            if (node is FrameworkElement fe2
                && !string.IsNullOrEmpty(fe2.Name)
                && !IsLikelyTemplatePartName(fe2.Name)
                && !IsInsideRepeatingContainer(node))
            {
                return true;
            }

            return false;
        }

        // Prefers the ItemsControl logical index (stable under virtualization);
        // falls back to counting realized visual siblings only when there's no
        // generator to ask.
        private static int GetStableIndex(DependencyObject element, DependencyObject? parent, Type targetType)
        {
            var itemsControl = FindOwningItemsControl(element);
            if (itemsControl?.ItemContainerGenerator != null)
            {
                int generatorIndex = itemsControl.ItemContainerGenerator.IndexFromContainer(element);
                if (generatorIndex >= 0) return generatorIndex;
            }
            return FastGetIndexByType(element, parent, targetType);
        }

        private static ItemsControl? FindOwningItemsControl(DependencyObject element)
        {
            DependencyObject? node = VisualTreeHelper.GetParent(element);
            int guard = 0;
            while (node != null && guard++ < 256)
            {
                if (GetProfile(node.GetType()).IsItemsControlType)
                {
                    var ic = (ItemsControl)node;
                    if (ic.ItemContainerGenerator?.IndexFromContainer(element) >= 0) return ic;
                }
                node = VisualTreeHelper.GetParent(node);
            }
            return null;
        }

        // Counts siblings by exact Type (pointer compare) -- used for
        // DevExpress LayoutItem/TreeListNode (must not merge LayoutItem and
        // LayoutGroup counts) and as the fallback for generator-backed types.
        private static int FastGetIndexByType(DependencyObject element, DependencyObject? parent, Type targetType)
        {
            if (parent == null) return 0;
            int matchCount = 0;
            int childCount = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childCount; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child.GetType() == targetType)
                {
                    if (child == element) return matchCount;
                    matchCount++;
                }
            }
            return 0;
        }

        // Counts siblings by cached SegmentKind -- used where DevExpress groups
        // several distinct type names into one logical row/cell concept
        // (RowControl/GridRow/RowContentPresenter all count together).
        private static int FastGetIndexByKind(DependencyObject element, DependencyObject? parent, SegmentKind targetKind)
        {
            if (parent == null) return 0;
            int matchCount = 0;
            int childCount = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childCount; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (GetProfile(child.GetType()).Kind == targetKind)
                {
                    if (child == element) return matchCount;
                    matchCount++;
                }
            }
            return 0;
        }

        // -----------------------------------------------------------------
        // Win32 interop for topmost-window hit resolution (see FindByScreenPoint)
        // -----------------------------------------------------------------
        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(POINT point);

        private static bool TryInvokeSpy(DependencyObject element, string methodName)
        {
            foreach (var iface in element.GetType().GetInterfaces())
            {
                if (iface.Name == "ISpyInteractable")
                {
                    var method = iface.GetMethod(methodName);
                    if (method != null)
                    {
                        method.Invoke(element, null);
                        return true;
                    }
                }
            }
            return false;
        }

        private static string? TryGetSpyText(FrameworkElement element)
        {
            foreach (var iface in element.GetType().GetInterfaces())
            {
                if (iface.Name == "ISpyInteractable")
                {
                    var method = iface.GetMethod("SpyGetText");
                    return method?.Invoke(element, null) as string;
                }
            }
            return null;
        }

        public static FrameworkElement? FindByName(string name)
        {
            foreach (Window window in Application.Current.Windows)
            {
                var found = FindByNameRecursive(window, name);
                if (found != null)
                {
                    return found;
                }
            }
            return null;
        }

        private static FrameworkElement? FindByNameRecursive(DependencyObject root, string name)
        {
            if (root is FrameworkElement fe && fe.Name == name)
            {
                return fe;
            }

            int childCount = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < childCount; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                var result = FindByNameRecursive(child, name);
                if (result != null)
                {
                    return result;
                }
            }
            return null;
        }

        private static void PrintElementBounds(Visual rootVisual, DependencyObject? dp)
        {
            
            if (dp == null) return;
            var element = dp as UIElement;
            if (element == null) return;

            // 1️⃣ Layout bounds (without transforms)
            double width = (element as FrameworkElement)?.ActualWidth ?? 0;
            double height = (element as FrameworkElement)?.ActualHeight ?? 0;
            LogVerbose($"Layout bounds: Width={width}, Height={height}");

            // 2️⃣ Transformed bounds relative to the Window
            GeneralTransform transform = element.TransformToAncestor(rootVisual);
            Rect transformedBounds = transform.TransformBounds(
                new Rect(new Point(0, 0), element.RenderSize)
            );
            LogVerbose($"Transformed bounds (relative to Window): {transformedBounds}");

            // 3️⃣ Screen coordinates
            Point topLeft = element.PointToScreen(new Point(0, 0));
            Point bottomRight = element.PointToScreen(new Point(width, height));
            LogVerbose($"Screen bounds: TopLeft={topLeft}, BottomRight={bottomRight}");
        }

        private static DependencyObject? HitTestRespectingInputVisibility(Visual rootVisual, Point point)
        {
            DependencyObject? firstInteractiveHit = null;
            DependencyObject? lastFilterHit = null;

            VisualTreeHelper.HitTest(
                rootVisual,
                potentialHitTestTarget =>
                {
                    LogVerbose($"HitTestRespectingInputVisibility {potentialHitTestTarget?.GetType().Name} ");
                    if (potentialHitTestTarget is UIElement uiElement && !uiElement.IsHitTestVisible)
                    {
                        return HitTestFilterBehavior.ContinueSkipSelfAndChildren;
                    }
                    else if(potentialHitTestTarget is UIElement uiElement2 && !uiElement2.IsVisible)
                    {
                        return HitTestFilterBehavior.ContinueSkipSelfAndChildren;
                    }
                    LogVerbose($"HitTestRespectingInputVisibility Not skipped {potentialHitTestTarget?.GetType().Name} ");
                    PrintElementBounds(rootVisual, potentialHitTestTarget);
                    lastFilterHit = potentialHitTestTarget;
                    return HitTestFilterBehavior.Continue;
                },
                hitTestResult =>
                {
                    PrintElementBounds(rootVisual, hitTestResult?.VisualHit);
                    LogVerbose($"HitTestRespectingInputVisibility - hitTestResult - {hitTestResult?.VisualHit.GetType().Name} ");
                    firstInteractiveHit = hitTestResult.VisualHit;
                    return HitTestResultBehavior.Stop; // first surviving hit wins, same Z-order real input uses
                },
                new PointHitTestParameters(point));

            return lastFilterHit
                   ?? firstInteractiveHit;
        }

        /// <summary>
        /// Hit-tests the given SCREEN coordinates and returns the most
        /// specific named FrameworkElement at that point. Used by
        /// WpfTestIde's recorder to identify which element the user just
        /// clicked, including custom-rendered controls that have no
        /// AutomationId at all.
        ///
        /// IMPORTANT: this resolves the topmost OS window at the point via
        /// Win32 WindowFromPoint, NOT by enumerating Application.Current.Windows.
        /// Application.Current.Windows only contains top-level Window objects
        /// explicitly shown via .Show()/.ShowDialog() -- it does NOT include
        /// Popup overlays, dropdown editors, tooltips, context menus, or
        /// DevExpress floating/docking panels, which WPF typically renders in
        /// their own separate HwndSource (a distinct native window). That gap
        /// is why plain VisualTreeHelper.HitTest scoped to Application windows
        /// misses clicks inside those surfaces while Snoop finds them --
        /// Snoop resolves the actual topmost window under the cursor first,
        /// then maps it back to its WPF PresentationSource, exactly like the
        /// approach below.
        /// </summary>
        public static FrameworkElement? FindByScreenPoint(double screenX, double screenY)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var screenPoint = new Point(screenX, screenY);
                var winPoint = new POINT { X = (int)Math.Round(screenX), Y = (int)Math.Round(screenY) };
                IntPtr hwnd = WindowFromPoint(winPoint);

                if (hwnd == IntPtr.Zero)
                {
                    Log($"({screenX},{screenY}) -> WindowFromPoint returned NULL; falling back to Application.Current.Windows scan");
                    return FindByScreenPointFallback(screenX, screenY, sw);
                }

                var source = HwndSource.FromHwnd(hwnd);
                if (source?.RootVisual == null)
                {
                    // Either a foreign-process window, or a native (non-WPF)
                    // child control -- correctly nothing for us to hit-test.
                    Log($"({screenX},{screenY}) -> hwnd 0x{hwnd.ToInt64():X} has no WPF RootVisual (foreign window or non-WPF surface)");
                    return FindByScreenPointFallback(screenX, screenY, sw);
                }

                Visual rootVisual = source.RootVisual;
                Point localPoint;
                try
                {
                    localPoint = rootVisual.PointFromScreen(screenPoint);
                }
                catch (InvalidOperationException)
                {
                    Log($"({screenX},{screenY}) -> PointFromScreen failed for hwnd 0x{hwnd.ToInt64():X} (not yet arranged/measured)");
                    return null;
                }

                var hitSw = System.Diagnostics.Stopwatch.StartNew();
                Log($"localPoint - {localPoint.X} {localPoint.Y}");
                DependencyObject? hit = HitTestRespectingInputVisibility(rootVisual, localPoint);
                hitSw.Stop();
                Log($"hwnd=0x{hwnd.ToInt64():X} {hit?.GetType().Name} local=({localPoint.X},{localPoint.Y}) hit-test in {hitSw.ElapsedMilliseconds}ms");

                var resolved = ResolveHitToNamedElement(hit, screenX, screenY, sw);
                Log($"ResolveHitToNamedElement {resolved?.GetType().Name} {resolved?.Name} hit-test ");

                if (resolved != null) return resolved;

                sw.Stop();
                Log($"({screenX},{screenY}) -> null after {sw.ElapsedMilliseconds}ms (hwnd 0x{hwnd.ToInt64():X}, no fallback -- topmost window was resolved correctly but contained no named element)");
                return null;
            }
            catch (Exception ex)
            {
                Log($"FindByScreenPoint exception: {ex}");
                return FindByScreenPointFallback(screenX, screenY, sw);
            }
        }

        // Shared by both the primary (WindowFromPoint-based) and fallback
        // paths: given a hit-test visual, walk up to the nearest meaningful
        // element using a single unified eligibility check (AutomationId,
        // ISpyInteractable, known interactive type, or non-template Name),
        // with the TextBoxView special-case handled up-front.
        private static FrameworkElement? ResolveHitToNamedElement(DependencyObject? visual, double screenX, double screenY, System.Diagnostics.Stopwatch sw)
        {
            if (visual == null) return null;

            DependencyObject? tvAncestor = visual;
            for (int depth = 0; depth < 10 && tvAncestor != null; depth++)
            {
                if (tvAncestor.GetType().Name == "TextBoxView")
                {
                    var textBox = WalkUpFromTextBoxView(tvAncestor);
                    if (textBox != null)
                    {
                        sw.Stop();
                        Log($"TextBoxView ancestor -> {textBox.GetType().Name} name={textBox.Name} in {sw.ElapsedMilliseconds}ms");
                        return textBox;
                    }
                    break;
                }
                tvAncestor = VisualTreeHelper.GetParent(tvAncestor);
            }

            DependencyObject? current = visual;
            const int maxSteps = 1000;
            int step = 0;
            var visited = new System.Collections.Generic.HashSet<DependencyObject>();
            while (current != null)
            {
                step++;
                if (step > maxSteps)
                {
                    Log($"ResolveHitToNamedElement safety limit hit after {maxSteps} steps starting from {visual.GetType().Name}");
                    return null;
                }
                if (!visited.Add(current))
                {
                    Log($"ResolveHitToNamedElement cycle detected after {step} steps starting from {visual.GetType().Name}");
                    return null;
                }

                if (IsEligibleStopPoint(current, out bool isInteractiveType))
                {
                    if (current is not FrameworkElement)
                    {
                        DependencyObject? feAncestor = current;
                        while (feAncestor != null && feAncestor is not FrameworkElement)
                        {
                            feAncestor = VisualTreeHelper.GetParent(feAncestor);
                        }
                        current = feAncestor;
                    }

                    if (current is FrameworkElement result)
                    {
                        sw.Stop();
                        Log($"({screenX},{screenY}) -> {result.GetType().Name} name={result.Name} (interactive={isInteractiveType}) in {sw.ElapsedMilliseconds}ms");
                        return result;
                    }
                }
                current = VisualTreeHelper.GetParent(current);
            }

            return null;
        }

        // Legacy path kept as a safety net for the rare case WindowFromPoint
        // can't resolve a WPF surface (e.g. certain remote/virtualized
        // desktop scenarios). Scoped only to Application.Current.Windows, so
        // it still can't see Popups/tooltips/floating panels -- that's the
        // known limitation this whole fix addresses -- but it's better than
        // returning nothing outright.
        private static FrameworkElement? FindByScreenPointFallback(double screenX, double screenY, System.Diagnostics.Stopwatch sw)
        {
            int windowIndex = 0;
            foreach (Window window in Application.Current.Windows)
            {
                windowIndex++;
                if (!window.IsVisible) continue;

                Point clientPoint;
                try
                {
                    clientPoint = window.PointFromScreen(new Point(screenX, screenY));
                }
                catch (InvalidOperationException)
                {
                    continue;
                }

                if (clientPoint.X < 0 || clientPoint.Y < 0 ||
                    clientPoint.X > window.ActualWidth || clientPoint.Y > window.ActualHeight)
                {
                    continue;
                }
                Log($"clientPoint - {clientPoint.X} {clientPoint.Y}");
                DependencyObject? hit = HitTestRespectingInputVisibility(window, clientPoint);
                var resolved = ResolveHitToNamedElement(hit, screenX, screenY, sw);
                if (resolved != null) return resolved;
            }
            sw.Stop();
            Log($"({screenX},{screenY}) -> null after fallback scan of {windowIndex} windows, {sw.ElapsedMilliseconds}ms total");
            return null;
        }

        /// <summary>
        /// Special-case for TextBox/PasswordBox: hit-testing often lands on the inner
        /// TextBoxView, whose visual tree is deep and template-internal.
        /// Walk up to the containing text-input control so the recorder sees the
        /// user-facing control, not the template leaf.
        /// </summary>
        private static FrameworkElement? WalkUpFromTextBoxView(DependencyObject visual)
        {
            const int maxSteps = 100;
            int step = 0;
            DependencyObject? current = visual;
            while (current != null)
            {
                step++;
                if (step > maxSteps)
                {
                    Log($"WalkUpFromTextBoxView safety limit hit after {maxSteps} steps from {visual.GetType().Name}");
                    return null;
                }
                string typeName = current.GetType().Name;
                if (typeName == "TextBox" || typeName == "PasswordBox")
                {
                    Log($"WalkUpFromTextBoxView returned {typeName} name={((FrameworkElement)current).Name} after {step} steps");
                    return (FrameworkElement)current;
                }
                current = VisualTreeHelper.GetParent(current);
            }
            Log($"WalkUpFromTextBoxView reached null after {step} steps from {visual.GetType().Name}");
            return null;
        }

        public static void Invoke(FrameworkElement element)
        {
            if (ElementHandlerRegistry.TryInvoke(element)) return;
            if (TryInvokeSpy(element, "SpyInvoke")) return;
            throw new InvalidOperationException(
                $"WpfSpyAgent: don't know how to invoke element of type '{element.GetType().Name}'. " +
                "Standard buttons and controls implementing ISpyInteractable are supported.");
        }

        public static void SetValue(FrameworkElement element, string value)
        {
            if (ElementHandlerRegistry.TrySetValue(element, value)) return;
            bool handled = false;
            foreach (var iface in element.GetType().GetInterfaces())
            {
                if (iface.Name == "ISpyInteractable")
                {
                    var method = iface.GetMethod("SpySetValue");
                    method?.Invoke(element, new object[] { value });
                    handled = true;
                    break;
                }
            }
            if (!handled)
            {
                throw new InvalidOperationException(
                    $"WpfSpyAgent: don't know how to set a value on element of type '{element.GetType().Name}'.");
            }
        }

        public static string GetText(FrameworkElement element)
        {
            if (ElementHandlerRegistry.TryGetText(element, out string result)) return result;
            if (TryGetSpyText(element) is string text) return text;
            throw new InvalidOperationException(
                $"WpfSpyAgent: don't know how to read text from element of type '{element.GetType().Name}'.");
        }

        public static bool IsVisible(FrameworkElement element) => element.IsVisible;

        public static bool IsEnabled(FrameworkElement element) => element.IsEnabled;

        public static string GetAttribute(FrameworkElement element, string attributeName)
        {
            return attributeName.ToLower() switch
            {
                "text" => GetText(element),
                "isenabled" or "is_enabled" => IsEnabled(element).ToString().ToLower(),
                "isvisible" or "is_visible" => IsVisible(element).ToString().ToLower(),
                "automationid" or "automation_id" => AutomationProperties.GetAutomationId(element) ?? "",
                "name" => element.Name ?? "",
                "controltype" or "control_type" => element.GetType().Name,
                _ => ""
            };
        }

        public static void DoubleClick(FrameworkElement element)
        {
            if (ElementHandlerRegistry.TryDoubleClick(element)) return;
            if (TryInvokeSpy(element, "SpyInvoke")) return;
            throw new InvalidOperationException(
                $"WpfSpyAgent: don't know how to double-click element of type '{element.GetType().Name}'.");
        }

        public static void RightClick(FrameworkElement element)
        {
            if (ElementHandlerRegistry.TryRightClick(element)) return;
            if (TryInvokeSpy(element, "SpyInvoke")) return;
            throw new InvalidOperationException(
                $"WpfSpyAgent: don't know how to right-click element of type '{element.GetType().Name}'.");
        }

        public static void PressKeys(FrameworkElement element, string keys)
        {
            if (ElementHandlerRegistry.TryPressKeys(element, keys)) return;
            throw new InvalidOperationException(
                $"WpfSpyAgent: don't know how to press keys on element of type '{element.GetType().Name}'.");
        }

        public static void DragDrop(FrameworkElement element, FrameworkElement target)
        {
            if (ElementHandlerRegistry.TryDragDrop(element, target)) return;
            throw new InvalidOperationException(
                $"WpfSpyAgent: don't know how to drag-drop element of type '{element.GetType().Name}'.");
        }

        public static void Hover(FrameworkElement element)
        {
            if (ElementHandlerRegistry.TryInvoke(element)) return;
            element.RaiseEvent(new System.Windows.Input.MouseEventArgs(
                System.Windows.Input.Mouse.PrimaryDevice,
                0)
            {
                RoutedEvent = System.Windows.UIElement.MouseEnterEvent
            });
        }

        public static void Scroll(FrameworkElement element, string direction)
        {
            if (ElementHandlerRegistry.TryScroll(element, direction)) return;
            throw new InvalidOperationException(
                $"WpfSpyAgent: don't know how to scroll element of type '{element.GetType().Name}'. Expected ScrollViewer.");
        }

        public static byte[] CaptureScreenshot(FrameworkElement element)
        {
            try
            {
                var rect = new Rect(0, 0, element.ActualWidth, element.ActualHeight);
                if (rect.Width <= 0 || rect.Height <= 0)
                {
                    return Array.Empty<byte>();
                }

                var bitmap = new RenderTargetBitmap(
                    (int)rect.Width, (int)rect.Height,
                    96, 96, PixelFormats.Pbgra32);
                bitmap.Render(element);

                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));

                using var stream = new MemoryStream();
                encoder.Save(stream);
                return stream.ToArray();
            }
            catch
            {
                return Array.Empty<byte>();
            }
        }

        public static void Toggle(FrameworkElement element)
        {
            if (ElementHandlerRegistry.TryToggle(element)) return;
            if (TryInvokeSpy(element, "SpyInvoke")) return;
            throw new InvalidOperationException(
                $"WpfSpyAgent: don't know how to toggle element of type '{element.GetType().Name}'.");
        }

        // -----------------------------------------------------------------
        // XPath support
        // -----------------------------------------------------------------

        // Conventionally-named template internals (WPF's "PART_" naming
        // convention, plus a few common recurring internal names) should
        // never be trusted as a global Name anchor, regardless of whether
        // they sit inside a repeating container or not.
        private static bool IsLikelyTemplatePartName(string name)
        {
            return name.StartsWith("PART_", StringComparison.Ordinal)
                || name == "AdornerLayer"
                || name == "border"
                || name == "Background"
                || name == "contentPresenter";
        }

        /// <summary>
        /// Builds a robust XPath for <paramref name="element"/>.
        ///
        /// Anchor priority (first match wins, walk stops there -- nothing
        /// above the anchor is included in the path):
        /// 1. <c>[@AutomationId='...']</c> -- deliberately assigned, never
        ///    scoped to a repeating template, so it's always safe as a
        ///    global anchor.
        /// 2. <c>[@Name='...']</c> -- ONLY if the element is not inside a
        ///    per-instance repeating template (grid row/cell/list item/etc).
        ///    A Name found inside such a template is scoped per-instance
        ///    (e.g. every grid row's "PART_Editor"), so trusting it as a
        ///    global anchor would make every row produce the same path.
        /// 3. <c>Window</c> root -- fallback if nothing else anchored.
        ///
        /// Between the anchor and the element, DevExpress row/cell/layout-item/
        /// tree-node families are indexed by direct sibling counting (they run
        /// their own virtualization, not the standard ItemContainerGenerator),
        /// while genuinely generator-backed WPF types (DataGridRow/DataGridCell/
        /// SelectorItem-derived) are indexed via ItemContainerGenerator, which
        /// stays correct across virtualization/scrolling.
        /// </summary>
        public static AliasSplit SplitXPathForAlias(string xpath)
        {
            if (string.IsNullOrEmpty(xpath)) return new AliasSplit { WindowName = string.Empty, AncestorPath = string.Empty };

            var parts = xpath.Split('/');
            int windowIdx = -1;
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].StartsWith("Window[", StringComparison.Ordinal))
                {
                    windowIdx = i;
                    break;
                }
            }
            if (windowIdx < 0) return new AliasSplit { WindowName = string.Empty, AncestorPath = xpath };

            int nameStart = -1;
            for (int j = windowIdx + 1; j < parts.Length; j++)
            {
                if (IsMeaningfulName(parts[j]))
                {
                    nameStart = j;
                    break;
                }
            }
            if (nameStart < 0) return new AliasSplit { WindowName = string.Empty, AncestorPath = string.Empty };

            string windowName = ExtractName(parts[nameStart]);
            var ancestor = new System.Text.StringBuilder();
            for (int k = nameStart + 1; k < parts.Length; k++)
            {
                if (!IsMeaningfulName(parts[k])) continue;
                if (ancestor.Length > 0) ancestor.Append('.');
                ancestor.Append(ExtractName(parts[k]));
            }
            return new AliasSplit { WindowName = windowName, AncestorPath = ancestor.ToString() };
        }

        public struct AliasSplit
        {
            public string WindowName;
            public string AncestorPath;
        }

        private static bool IsMeaningfulName(string segment)
        {
            if (string.IsNullOrEmpty(segment)) return false;
            if (!segment.StartsWith("[", StringComparison.Ordinal)) return false;
            if (segment.StartsWith("[@", StringComparison.Ordinal)) return false;
            var name = ExtractName(segment);
            return !string.IsNullOrEmpty(name) && name != "MainWindow";
        }

        private static string ExtractName(string segment)
        {
            int at = segment.IndexOf("@Name='", StringComparison.Ordinal);
            if (at < 0) return string.Empty;
            int start = at + "@Name='".Length;
            int end = segment.IndexOf("'", start, StringComparison.Ordinal);
            if (end < 0) return string.Empty;
            return segment.Substring(start, end - start);
        }

        public static string BuildXPath(FrameworkElement element)
        {
            var segments = new System.Collections.Generic.List<string>();
            DependencyObject? current = element;

            while (current != null)
            {
                Log($"BuildXPath {current?.GetType().Name} ");
                if (current is Window window)
                {
                    string? windowAutoId = AutomationProperties.GetAutomationId(window);
                    if (!string.IsNullOrEmpty(windowAutoId))
                    {
                        segments.Insert(0, $"Window[@Name='{windowAutoId}']");
                    }
                    else if (!string.IsNullOrEmpty(window.Name))
                    {
                        segments.Insert(0, $"Window[@Name='{window.Name}']");
                    }
                    else if (!string.IsNullOrEmpty(window.Title))
                    {
                        segments.Insert(0, $"Window[@Name='{window.Title}']");
                    }
                    else
                    {
                        segments.Insert(0, "Window");
                    }
                    break;
                }

                Type type = current.GetType();
                string typeName = type.Name;
                var fe = current as FrameworkElement;
                string? elementName = fe?.Name;
                TypeProfile profile = GetProfile(type);

               

                string? automationId = fe != null ? AutomationProperties.GetAutomationId(fe) : null;
                if (!string.IsNullOrEmpty(automationId))
                {
                    segments.Insert(0, $"{typeName}[@AutomationId='{automationId}']");
                    current = VisualTreeHelper.GetParent(current);
                    continue;
                }

                if (!string.IsNullOrEmpty(elementName) && !IsLikelyTemplatePartName(elementName) && !IsInsideRepeatingContainer(current))
                {
                    segments.Insert(0, $"{typeName}[@Name='{elementName}']");
                    current = VisualTreeHelper.GetParent(current);
                    continue;
                }

                if (profile.SkipLayout)
                {
                    bool includeContainer = false;
                    if (ContainerInclusionRules.TryGetValue(typeName, out var parentTypes))
                    {
                        DependencyObject? containerParent = VisualTreeHelper.GetParent(current);
                        if (containerParent != null)
                        {
                            string parentTypeName = containerParent.GetType().Name;
                            if (parentTypes.Contains(parentTypeName))
                            {
                                int siblingCount = 0;
                                int childCount = VisualTreeHelper.GetChildrenCount(containerParent);
                                for (int i = 0; i < childCount; i++)
                                {
                                    if (VisualTreeHelper.GetChild(containerParent, i).GetType().Name == typeName)
                                        siblingCount++;
                                }
                                if (siblingCount > 1)
                                {
                                    includeContainer = true;
                                }
                            }
                        }
                    }

                    if (!includeContainer)
                    {
                        current = VisualTreeHelper.GetParent(current);
                        continue;
                    }
                }

                DependencyObject? parent = VisualTreeHelper.GetParent(current);
                //string segment = profile.Kind switch
                //{
                //    SegmentKind.DevExpressRow => $"DevExpressRow[{FastGetIndexByKind(current, parent, SegmentKind.DevExpressRow) + 1}]",
                //    SegmentKind.DevExpressCell => $"DevExpressCell[{FastGetIndexByKind(current, parent, SegmentKind.DevExpressCell) + 1}]",
                //    SegmentKind.DevExpressLayoutItem => $"{typeName}[{FastGetIndexByType(current, parent, type) + 1}]",
                //    SegmentKind.DevExpressTreeNode => $"{typeName}[{FastGetIndexByType(current, parent, type) + 1}]",
                //    SegmentKind.WpfGeneratorBacked => $"{profile.TokenOverride ?? typeName}[{GetStableIndex(current, parent, type) + 1}]",
                //    _ => FastGetIndexByType(current, parent, type) > 0 ? $"{typeName}[{FastGetIndexByType(current, parent, type) + 1}]" : typeName
                //};

                string segment = FastGetIndexByType(current, parent, type) > 0 ? $"{typeName}[{FastGetIndexByType(current, parent, type) + 1}]" : typeName;

                segments.Insert(0, segment);
                current = parent;
            }

            return "/" + string.Join("/", segments);
        }

        /// <summary>
        /// Finds the first element matching a simple WPF XPath expression.
        /// Supported syntax:
        /// <list type="bullet">
        ///   <item><c>/</c> — absolute path from the root window</item>
        ///   <item><c>ElementName</c> — match by type name</item>
        ///   <item><c>[@AutomationId='value']</c> — match by AutomationId (priority 1)</item>
        ///   <item><c>[@Name='value']</c> — match by FrameworkElement.Name (priority 2)</item>
        ///   <item><c>[N]</c> — match the N-th child of that type (1-based)</item>
        /// </list>
        /// Example: <c>/Window[@AutomationId='MainWindow']/Grid/TextBox[@AutomationId='txtUsername']</c>
        /// </summary>
        public static FrameworkElement? FindByAutomationId(string automationId)
        {
            foreach (Window window in Application.Current.Windows)
            {
                var found = FindByAutomationIdRecursive(window, automationId);
                if (found != null)
                {
                    return found;
                }
            }
            return null;
        }

        private static FrameworkElement? FindByAutomationIdRecursive(DependencyObject root, string automationId)
        {
            if (root is FrameworkElement fe)
            {
                string? currentAutomationId = AutomationProperties.GetAutomationId(fe);
                if (currentAutomationId == automationId)
                {
                    return fe;
                }
            }

            int childCount = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < childCount; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                var result = FindByAutomationIdRecursive(child, automationId);
                if (result != null)
                {
                    return result;
                }
            }
            return null;
        }

        public static FrameworkElement? FindByXPath(string xpath)
        {
            if (string.IsNullOrEmpty(xpath) || !xpath.StartsWith("/"))
            {
                return null;
            }

            foreach (PresentationSource source in PresentationSource.CurrentSources)
            {
                if (source?.RootVisual is not DependencyObject root)
                {
                    continue;
                }

                var result = MatchXPathSegment(root, xpath.Split('/'), 1);
                if (result != null)
                {
                    return result;
                }
            }

            foreach (Window window in Application.Current.Windows)
            {
                if (!window.IsVisible) continue;

                var result = MatchXPathSegment(window, xpath.Split('/'), 1);
                if (result != null)
                {
                    return result;
                }
            }

            return null;
        }

        private static FrameworkElement? MatchXPathSegment(DependencyObject current, string[] segments, int index)
        {
            if (index >= segments.Length)
            {
                return current as FrameworkElement;
            }

            string segment = segments[index];
            if (string.IsNullOrEmpty(segment))
            {
                return MatchXPathSegment(current, segments, index + 1);
            }

            // Check if the current node itself matches this segment (for the
            // root/Window case) before descending into children.
            if (MatchesSegment(current, segment))
            {
                if (index + 1 >= segments.Length)
                {
                    return current as FrameworkElement;
                }
                var result = MatchXPathSegmentChildren(current, segments, index + 1);
                if (result != null)
                {
                    return result;
                }
            }

            // Otherwise try matching children.
            return MatchXPathSegmentChildren(current, segments, index);
        }

        private static FrameworkElement? MatchXPathSegmentChildren(DependencyObject parent, string[] segments, int index)
        {
            if (index >= segments.Length)
            {
                return parent as FrameworkElement;
            }

            string segment = segments[index];
            if (string.IsNullOrEmpty(segment))
            {
                return MatchXPathSegmentChildren(parent, segments, index + 1);
            }

            // Search all descendants (not just direct children) because the
            // WPF visual tree inserts wrapper elements (e.g. WindowRoot,
            // AdornerLayer, ContentPresenter) between logical parents and
            // children. This makes XPath robust for real WPF hierarchies.
            return MatchXPathSegmentDescendants(parent, segments, index);
        }

        private static FrameworkElement? MatchXPathSegmentDescendants(DependencyObject current, string[] segments, int index)
        {
            if (index >= segments.Length)
            {
                return current as FrameworkElement;
            }

            string segment = segments[index];
            if (string.IsNullOrEmpty(segment))
            {
                return MatchXPathSegmentDescendants(current, segments, index + 1);
            }

            if (MatchesSegment(current, segment))
            {
                if (index + 1 >= segments.Length)
                {
                    return current as FrameworkElement;
                }
                var result = MatchXPathSegmentDescendants(current, segments, index + 1);
                if (result != null)
                {
                    return result;
                }
            }

            int childCount = VisualTreeHelper.GetChildrenCount(current);
            for (int i = 0; i < childCount; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(current, i);
                var result = MatchXPathSegmentDescendants(child, segments, index);
                if (result != null)
                {
                    return result;
                }
            }
            return null;
        }

        private readonly struct SegmentParseResult
        {
            public readonly string TypeToken;
            public readonly System.Collections.Generic.List<string> Predicates;

            public SegmentParseResult(string typeToken, System.Collections.Generic.List<string> predicates)
            {
                TypeToken = typeToken;
                Predicates = predicates;
            }
        }

        // Splits a segment like "DevExpressRow[3][@Name='foo']" into its type
        // token and a list of raw "[...]" predicate bodies (without the
        // brackets). BuildXPath can emit BOTH an index and a Name predicate
        // on the same segment (e.g. a DevExpressRow with a Name set), so a
        // single segment may carry more than one bracket group.
        private static SegmentParseResult ParseSegment(string segment)
        {
            var predicates = new System.Collections.Generic.List<string>();
            int firstBracket = segment.IndexOf('[');
            string typeToken = firstBracket >= 0 ? segment.Substring(0, firstBracket) : segment;

            int pos = firstBracket;
            while (pos >= 0 && pos < segment.Length)
            {
                int close = segment.IndexOf(']', pos);
                if (close < 0) break;
                predicates.Add(segment.Substring(pos + 1, close - pos - 1));
                pos = segment.IndexOf('[', close);
            }

            return new SegmentParseResult(typeToken, predicates);
        }

        private static bool MatchesSegment(DependencyObject element, string segment)
        {
            var parseResult = ParseSegment(segment);
            string typeToken = parseResult.TypeToken;
            var predicates = parseResult.Predicates;

            string? automationIdPredicate = null;
            string? namePredicate = null;
            int? indexPredicate = null;
            bool isWildcardAutomationId = false;
            bool isWildcardName = false;
            string? regexPattern = null;

            foreach (var predicate in predicates)
            {
                if (predicate.StartsWith("@AutomationId='", StringComparison.Ordinal))
                {
                    int start = "@AutomationId='".Length;
                    int end = predicate.LastIndexOf('\'');
                    if (end > start) 
                    {
                        automationIdPredicate = predicate.Substring(start, end - start);
                        // Check for wild-card pattern
                        if (automationIdPredicate.Contains("*") || automationIdPredicate.Contains("?"))
                        {
                            isWildcardAutomationId = true;
                        }
                    }
                }
                else if (predicate.StartsWith("@Name='", StringComparison.Ordinal))
                {
                    int start = "@Name='".Length;
                    int end = predicate.LastIndexOf('\'');
                    if (end > start)
                    {
                        namePredicate = predicate.Substring(start, end - start);
                        // Check for wild-card pattern
                        if (namePredicate.Contains("*") || namePredicate.Contains("?"))
                        {
                            isWildcardName = true;
                        }
                    }
                }
                else if (int.TryParse(predicate, out int parsedIndex))
                {
                    indexPredicate = parsedIndex;
                }
                else if (predicate.StartsWith("regex:", StringComparison.OrdinalIgnoreCase))
                {
                    // Regex pattern matching: [regex:pattern]
                    regexPattern = predicate.Substring(6);
                }
            }

            var elementType = element.GetType();
            TypeProfile profile = GetProfile(elementType);

            // Wild-card type matching: * or ? matches any element
            bool typeMatches;
            if (typeToken == "*" || typeToken == "?")
            {
                // Wild-card matches any type
                typeMatches = true;
            }
            else if (typeToken == "DevExpressRow")
            {
                typeMatches = profile.Kind == SegmentKind.DevExpressRow;
            }
            else if (typeToken == "DevExpressCell")
            {
                typeMatches = profile.Kind == SegmentKind.DevExpressCell;
            }
            else if (elementType.Name == typeToken)
            {
                typeMatches = true;
            }
            else
            {
                var baseType = elementType.BaseType;
                while (baseType != null && baseType.Name != typeToken)
                {
                    baseType = baseType.BaseType;
                }
                typeMatches = baseType != null;
            }

            if (!typeMatches) return false;

            if (isWildcardAutomationId && automationIdPredicate != null)
            {
                // Wild-card matching for AutomationId
                if (!(element is FrameworkElement fe))
                {
                    return false;
                }
                string? actualId = AutomationProperties.GetAutomationId(fe);
                if (!MatchesWildcard(actualId, automationIdPredicate))
                {
                    return false;
                }
            }
            else if (isWildcardName && namePredicate != null)
            {
                // Wild-card matching for Name
                if (!(element is FrameworkElement fe))
                {
                    return false;
                }
                if (!MatchesWildcard(fe.Name, namePredicate))
                {
                    return false;
                }
            }
            else if (automationIdPredicate != null)
            {
                if (!(element is FrameworkElement fe) ||
                    AutomationProperties.GetAutomationId(fe) != automationIdPredicate)
                {
                    return false;
                }
            }
            else if (namePredicate != null)
            {
                if (element is Window window)
                {
                    if (window.Title != namePredicate)
                    {
                        return false;
                    }
                }
                else if (!(element is FrameworkElement fe) || fe.Name != namePredicate)
                {
                    return false;
                }
            }

            if (regexPattern != null)
            {
                // Regex pattern matching - match against all element properties
                if (!MatchesRegex(element, regexPattern))
                {
                    return false;
                }
            }

            if (indexPredicate.HasValue)
            {
                DependencyObject? parent = VisualTreeHelper.GetParent(element);
                int actualIndex = typeToken switch
                {
                    "DevExpressRow" => FastGetIndexByKind(element, parent, SegmentKind.DevExpressRow) + 1,
                    "DevExpressCell" => FastGetIndexByKind(element, parent, SegmentKind.DevExpressCell) + 1,
                    _ when profile.Kind == SegmentKind.WpfGeneratorBacked => GetStableIndex(element, parent, elementType) + 1,
                    _ => FastGetIndexByType(element, parent, elementType) + 1
                };

                if (actualIndex != indexPredicate.Value) return false;
            }

            return true;
        }

        /// <summary>
        /// Matches a string against a wild-card pattern.
        /// Supports: * (any characters), ? (single character)
        /// </summary>
        private static bool MatchesWildcard(string? value, string pattern)
        {
            if (value == null) return false;
            if (pattern == "*" || pattern == "?") return true;

            // Convert wild-card pattern to regex
            var regexPattern = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
                .Replace("\\*", ".*")
                .Replace("\\?", ".") + "$";

            return System.Text.RegularExpressions.Regex.IsMatch(value, regexPattern);
        }

        /// <summary>
        /// Matches an element against a regex pattern.
        /// The regex is matched against all element properties (Name, AutomationId, ClassName, Text).
        /// </summary>
        private static bool MatchesRegex(DependencyObject element, string pattern)
        {
            try
            {
                var regex = new System.Text.RegularExpressions.Regex(pattern, 
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                // Check Name
                if (element is FrameworkElement fe)
                {
                    if (!string.IsNullOrEmpty(fe.Name) && regex.IsMatch(fe.Name))
                        return true;

                    var automationId = AutomationProperties.GetAutomationId(fe);
                    if (!string.IsNullOrEmpty(automationId) && regex.IsMatch(automationId))
                        return true;

                    // Check Text property for text controls
                    if (fe is System.Windows.Controls.TextBlock tb && !string.IsNullOrEmpty(tb.Text))
                        if (regex.IsMatch(tb.Text)) return true;
                    if (fe is System.Windows.Controls.TextBox txt && !string.IsNullOrEmpty(txt.Text))
                        if (regex.IsMatch(txt.Text)) return true;
                }

                // Check class name
                if (regex.IsMatch(element.GetType().Name))
                    return true;

                return false;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        /// <summary>
        /// Generates a flexible XPath with wild-card alternatives.
        /// Useful for creating resilient locators that can survive UI changes.
        /// </summary>
        public static string BuildFlexibleXPath(FrameworkElement element)
        {
            var sb = new System.Text.StringBuilder();
            
            // Start with exact XPath
            string exactXPath = BuildXPath(element);
            sb.AppendLine("Exact: " + exactXPath);
            
            // Get element properties
            var automationId = AutomationProperties.GetAutomationId(element);
            var name = element.Name;
            
            // Generate prefix wild-card version
            if (!string.IsNullOrEmpty(automationId))
            {
                sb.AppendLine($"Prefix: //{element.GetType().Name}[@AutomationId='*{automationId}']");
            }
            
            // Generate suffix wild-card version
            if (!string.IsNullOrEmpty(automationId))
            {
                sb.AppendLine($"Suffix: //{element.GetType().Name}[@AutomationId='{automationId}*']");
            }
            
            // Generate contains version
            if (!string.IsNullOrEmpty(automationId))
            {
                sb.AppendLine($"Contains: //{element.GetType().Name}[contains(@AutomationId,'{automationId}')]");
            }
            
            // Generate any-matching version
            sb.AppendLine($"Any: //{element.GetType().Name}[@AutomationId='*']");
            
            return sb.ToString();
        }

        /// <summary>
        /// Extracts a DataGrid's content as structured JSON: column
        /// headers and row cell values. Uses the DataGrid's own
        /// Items/Columns APIs and walks the visual tree for
        /// row/cell containers — no UI Automation dependency.
        /// </summary>
        public static string GetDataGridContent(FrameworkElement element)
        {
            if (element is not System.Windows.Controls.DataGrid dataGrid)
            {
                return JsonHelper.Serialize(new { error = "Element is not a DataGrid" });
            }

            var columns = new List<string>();
            foreach (var col in dataGrid.Columns)
            {
                string header = "";
                if (col.Header is string h)
                    header = h;
                else if (col.Header != null)
                    header = col.Header.ToString() ?? "";
                columns.Add(header);
            }

            var rows = new List<List<string>>();
            for (int i = 0; i < dataGrid.Items.Count; i++)
            {
                var row = dataGrid.ItemContainerGenerator.ContainerFromIndex(i) as System.Windows.Controls.DataGridRow;
                if (row is null)
                {
                    rows.Add(new List<string>());
                    continue;
                }

                var cells = new List<string>();
                for (int j = 0; j < dataGrid.Columns.Count; j++)
                {
                    System.Windows.Controls.DataGridCell? cell = null;
                    var cellContent = dataGrid.Columns[j].GetCellContent(row);
                    if (cellContent != null)
                    {
                        DependencyObject? parent = VisualTreeHelper.GetParent(cellContent);
                        while (parent != null && parent is not System.Windows.Controls.DataGridCell)
                        {
                            parent = VisualTreeHelper.GetParent(parent);
                        }
                        cell = parent as System.Windows.Controls.DataGridCell;
                    }
                    if (cell is null)
                    {
                        cells.Add("");
                        continue;
                    }

                    string cellText = GetCellText(cell);
                    cells.Add(cellText);
                }
                rows.Add(cells);
            }

            var result = new
            {
                columns,
                rows
            };
return JsonHelper.Serialize(result);
         }

         /// <summary>
         /// Captures a DataGrid element as a PNG screenshot and returns
         /// the image as a base64-encoded string. Used by OCR-based
         /// content extraction where the visual appearance of the
         /// DataGrid must be read rather than its programmatic content.
         /// </summary>
         public static string GetDataGridScreenshot(FrameworkElement element)
         {
             if (element is not System.Windows.Controls.DataGrid dataGrid)
             {
                 return JsonHelper.Serialize(new { error = "Element is not a DataGrid" });
             }

             try
             {
                 var rect = new Rect(0, 0, dataGrid.ActualWidth, dataGrid.ActualHeight);
                 if (rect.Width <= 0 || rect.Height <= 0)
                 {
                     return JsonHelper.Serialize(new { error = "DataGrid has zero size" });
                 }

                 var bitmap = new RenderTargetBitmap(
                     (int)rect.Width, (int)rect.Height,
                     96, 96, PixelFormats.Pbgra32);
                 bitmap.Render(dataGrid);

                 var encoder = new PngBitmapEncoder();
                 encoder.Frames.Add(BitmapFrame.Create(bitmap));

                 using var stream = new MemoryStream();
                 encoder.Save(stream);
                 var bytes = stream.ToArray();
                 return Convert.ToBase64String(bytes);
             }
             catch (Exception ex)
             {
                 return JsonHelper.Serialize(new { error = ex.Message });
             }
         }

         private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T t)
                    return t;
                var descendant = FindVisualChild<T>(child);
                if (descendant != null)
                    return descendant;
            }
            return null;
        }

        private static string GetCellText(System.Windows.Controls.DataGridCell cell)
        {
            if (cell.Content is System.Windows.Controls.TextBlock textBlock)
                return textBlock.Text ?? "";
            if (cell.Content is System.Windows.Controls.TextBox textBox)
                return textBox.Text ?? "";
            if (cell.Content is System.Windows.Controls.CheckBox checkBox)
                return checkBox.IsChecked == true ? "True" : "False";
            if (cell.Content is System.Windows.Controls.ComboBox comboBox)
                return comboBox.SelectedItem?.ToString() ?? "";
            if (cell.Content is System.Windows.Controls.Label label)
                return label.Content?.ToString() ?? "";
            if (cell.Content is System.Windows.Controls.Button button)
                return button.Content?.ToString() ?? "";
            if (cell.Content != null)
                return cell.Content.ToString() ?? "";

            var textBlockChild = FindVisualChild<System.Windows.Controls.TextBlock>(cell);
            if (textBlockChild != null && !string.IsNullOrEmpty(textBlockChild.Text))
                return textBlockChild.Text;

            return "";
        }

        /// <summary>
        /// Converts OCR text into CSV format.
        /// </summary>
        public static string OcrTextToCsv(string ocrText)
        {
            var lines = ocrText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            var csv = new StringBuilder();
            foreach (var line in lines)
            {
                var cells = line.Trim().Split(new[] { " ", "\t" }, StringSplitOptions.RemoveEmptyEntries);
                csv.AppendLine(string.Join(",", cells));
            }
            return csv.ToString();
        }

        /// <summary>
        /// Builds a tree structure of all elements in a visual tree.
        /// Used by the Spy Tool to display the element hierarchy.
        /// </summary>
        public static ElementTreeNode? BuildElementTree(DependencyObject root)
        {
            if (root == null) return null;

            var node = new ElementTreeNode
            {
                Name = (root as FrameworkElement)?.Name ?? "",
                AutomationId = GetAutomationId(root),
                ControlType = root.GetType().Name,
                ClassName = root.GetType().FullName ?? "",
                Text = GetElementText(root),
                IsEnabled = IsElementEnabled(root),
                IsVisible = IsElementVisible(root),
                XPath = root is FrameworkElement fe ? BuildXPath(fe) : null
            };

            // Add children
            int childCount = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < childCount; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child != null)
                {
                    var childNode = BuildElementTree(child);
                    if (childNode != null)
                    {
                        childNode.Parent = node;
                        node.Children.Add(childNode);
                    }
                }
            }

            return node;
        }

        private static string? GetAutomationId(DependencyObject? element)
        {
            if (element is FrameworkElement fe)
            {
                var id = AutomationProperties.GetAutomationId(fe);
                return string.IsNullOrEmpty(id) ? null : id;
            }
            return null;
        }

        private static string? GetElementText(DependencyObject? element)
        {
            if (element is System.Windows.Controls.TextBlock tb)
                return tb.Text;
            if (element is System.Windows.Controls.TextBox txt)
                return txt.Text;
            if (element is System.Windows.Controls.ContentControl cc)
                return cc.Content?.ToString();
            return null;
        }

        private static bool IsElementEnabled(DependencyObject? element)
        {
            if (element is FrameworkElement fe)
                return fe.IsEnabled;
            return true;
        }

        private static bool IsElementVisible(DependencyObject? element)
        {
            if (element is FrameworkElement fe)
                return fe.IsVisible && fe.IsLoaded;
            return false;
        }
    }

    internal static class ElementHandlerRegistry
    {
    private static readonly ConcurrentDictionary<Type, Action<FrameworkElement>> _invoke = new();
    private static readonly ConcurrentDictionary<Type, Action<FrameworkElement, string>> _setValue = new();
    private static readonly ConcurrentDictionary<Type, Func<FrameworkElement, string>> _getText = new();
    private static readonly ConcurrentDictionary<Type, Action<FrameworkElement>> _doubleClick = new();
    private static readonly ConcurrentDictionary<Type, Action<FrameworkElement>> _rightClick = new();
    private static readonly ConcurrentDictionary<Type, Action<FrameworkElement, string>> _pressKeys = new();
    private static readonly ConcurrentDictionary<Type, Action<FrameworkElement>> _hover = new();
    private static readonly ConcurrentDictionary<Type, Action<FrameworkElement, string>> _scroll = new();
    private static readonly ConcurrentDictionary<Type, Action<FrameworkElement>> _toggle = new();

    static ElementHandlerRegistry()
    {
        RegisterInvoke<TextBox>(_ => _.Focus());
        RegisterInvoke<ButtonBase>(bb =>
        {
            if (bb.Command != null)
            {
                Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() =>
                {
                    try { bb.Command.Execute(null); }
                    catch { }
                }));
            }
            else
            {
                bb.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            }
        });
        RegisterInvoke<TabItem>(ti => ti.IsSelected = true);

        RegisterSetValue<TextBox>((tb, v) => tb.Text = v);
        RegisterSetValue<PasswordBox>((pb, v) => pb.Password = v);
        RegisterSetValue<ComboBox>(SetComboBoxValue);

        RegisterGetText<TextBox>(tb => tb.Text);
        RegisterGetText<PasswordBox>(pb => pb.Password);
        RegisterGetText<ComboBox>(cb => cb.Text);
        RegisterGetText<TextBlock>(tb => tb.Text);
        RegisterGetText<ContentControl>(cc => cc.Content?.ToString() ?? "");
        RegisterGetText<ItemsControl>(ic => $"{ic.Items.Count} rows");

        RegisterDoubleClick<ButtonBase>(bb =>
        {
            bb.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            bb.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        });

        RegisterRightClick<ButtonBase>(bb =>
            bb.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)));

        RegisterPressKeys<TextBox>((tb, keys) => { tb.Focus(); tb.Text += keys; });

        RegisterScroll<ScrollViewer>((sv, dir) =>
        {
            switch (dir.ToLower())
            {
                case "down": sv.LineDown(); break;
                case "up": sv.LineUp(); break;
                case "pagedown": sv.PageDown(); break;
                case "pageup": sv.PageUp(); break;
                case "left": sv.LineLeft(); break;
                case "right": sv.LineRight(); break;
                default: sv.ScrollToVerticalOffset(sv.VerticalOffset + 50); break;
            }
        });

        RegisterToggle<ToggleButton>(tb => tb.IsChecked = !(tb.IsChecked ?? false));
    }

    private static void SetComboBoxValue(ComboBox comboBox, string value)
    {
        if (comboBox.IsEditable)
        {
            comboBox.Text = value;
            return;
        }

        foreach (var item in comboBox.Items)
        {
            if (item is System.Windows.Controls.ComboBoxItem cbi && cbi.Content?.ToString() == value)
            {
                comboBox.SelectedItem = item;
                return;
            }
        }
        comboBox.Text = value;
    }

    public static void RegisterInvoke<T>(Action<T> handler) where T : FrameworkElement
        => _invoke[typeof(T)] = fe => handler((T)fe);
    public static void RegisterSetValue<T>(Action<T, string> handler) where T : FrameworkElement
        => _setValue[typeof(T)] = (fe, v) => handler((T)fe, v);
    public static void RegisterGetText<T>(Func<T, string> handler) where T : FrameworkElement
        => _getText[typeof(T)] = fe => handler((T)fe);
    public static void RegisterDoubleClick<T>(Action<T> handler) where T : FrameworkElement
        => _doubleClick[typeof(T)] = fe => handler((T)fe);
    public static void RegisterRightClick<T>(Action<T> handler) where T : FrameworkElement
        => _rightClick[typeof(T)] = fe => handler((T)fe);
    public static void RegisterPressKeys<T>(Action<T, string> handler) where T : FrameworkElement
        => _pressKeys[typeof(T)] = (fe, v) => handler((T)fe, v);
    public static void RegisterHover<T>(Action<T> handler) where T : FrameworkElement
        => _hover[typeof(T)] = fe => handler((T)fe);
    public static void RegisterScroll<T>(Action<T, string> handler) where T : FrameworkElement
        => _scroll[typeof(T)] = (fe, v) => handler((T)fe, v);
    public static void RegisterToggle<T>(Action<T> handler) where T : FrameworkElement
        => _toggle[typeof(T)] = fe => handler((T)fe);

    private static bool TryGetHandler(Type type, ConcurrentDictionary<Type, Action<FrameworkElement>> dict, out Action<FrameworkElement> handler)
    {
        while (type != null && type != typeof(object))
        {
            if (dict.TryGetValue(type, out handler)) return true;
            type = type.BaseType;
        }
        handler = null;
        return false;
    }

    private static bool TryGetHandler(Type type, ConcurrentDictionary<Type, Action<FrameworkElement, string>> dict, out Action<FrameworkElement, string> handler)
    {
        while (type != null && type != typeof(object))
        {
            if (dict.TryGetValue(type, out handler)) return true;
            type = type.BaseType;
        }
        handler = null;
        return false;
    }

    private static bool TryGetHandler(Type type, ConcurrentDictionary<Type, Func<FrameworkElement, string>> dict, out Func<FrameworkElement, string> handler)
    {
        while (type != null && type != typeof(object))
        {
            if (dict.TryGetValue(type, out handler)) return true;
            type = type.BaseType;
        }
        handler = null;
        return false;
    }

    public static bool TryInvoke(FrameworkElement element)
    {
        if (element is ISpyInteractable spy) { spy.SpyInvoke(); return true; }
        if (TryGetHandler(element.GetType(), _invoke, out var handler))
        {
            handler(element);
            return true;
        }
        return false;
    }

    public static bool TrySetValue(FrameworkElement element, string value)
    {
        if (element is ISpyInteractable spy) { spy.SpySetValue(value); return true; }
        if (TryGetHandler(element.GetType(), _setValue, out var handler))
        {
            handler(element, value);
            return true;
        }
        return false;
    }

    public static bool TryGetText(FrameworkElement element, out string text)
    {
        text = null;
        if (element is ISpyInteractable spy) { text = spy.SpyGetText(); return true; }
        if (TryGetHandler(element.GetType(), _getText, out var handler))
        {
            text = handler(element);
            return true;
        }
        return false;
    }

    public static bool TryDoubleClick(FrameworkElement element)
    {
        if (element is ISpyInteractable spy) { spy.SpyInvoke(); return true; }
        if (TryGetHandler(element.GetType(), _doubleClick, out var handler))
        {
            handler(element);
            return true;
        }
        return false;
    }

    public static bool TryRightClick(FrameworkElement element)
    {
        if (element is ISpyInteractable spy) { spy.SpyInvoke(); return true; }
        if (TryGetHandler(element.GetType(), _rightClick, out var handler))
        {
            handler(element);
            return true;
        }
        return false;
    }

    public static bool TryPressKeys(FrameworkElement element, string keys)
    {
        if (element is ISpyInteractable spy) { spy.SpySetValue(keys); return true; }
        if (TryGetHandler(element.GetType(), _pressKeys, out var handler))
        {
            handler(element, keys);
            return true;
        }
        return false;
    }

    public static bool TryHover(FrameworkElement element)
    {
        if (element is ISpyInteractable spy) { spy.SpyInvoke(); return true; }
        if (TryGetHandler(element.GetType(), _hover, out var handler))
        {
            handler(element);
            return true;
        }
        return false;
    }

    public static bool TryScroll(FrameworkElement element, string direction)
    {
        if (TryGetHandler(element.GetType(), _scroll, out var handler))
        {
            handler(element, direction);
            return true;
        }
        return false;
    }

    public static bool TryToggle(FrameworkElement element)
    {
        if (element is ISpyInteractable spy) { spy.SpyInvoke(); return true; }
        if (TryGetHandler(element.GetType(), _toggle, out var handler))
        {
            handler(element);
            return true;
        }
        return false;
    }

    public static bool TryDragDrop(FrameworkElement element, FrameworkElement target)
    {
        if (element is ISpyInteractable spy) { spy.SpyInvoke(); return true; }
        return false;
    }
}

    /// <summary>
    /// Represents a node in the element tree for the Spy Tool.
    /// </summary>
    public class ElementTreeNode
    {
        public string? Name { get; set; }
        public string? AutomationId { get; set; }
        public string? ControlType { get; set; }
        public string? ClassName { get; set; }
        public string? Text { get; set; }
        public string? XPath { get; set; }
        public bool? IsEnabled { get; set; }
        public bool? IsVisible { get; set; }
        public Rect? Bounds { get; set; }
#if NET461
        [Newtonsoft.Json.JsonIgnore]
#else
        [System.Text.Json.Serialization.JsonIgnore]
#endif
        public ElementTreeNode? Parent { get; set; }
        public List<ElementTreeNode> Children { get; set; } = new();
    }
}


