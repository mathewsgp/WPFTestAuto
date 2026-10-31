using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using AvalonDock;
using AvalonDock.Layout;
using AvalonDock.Serializer.Json;
using WpfTestIde.Helpers;
using WpfTestIde.Models;
using WpfTestIde.ViewModels;

namespace WpfTestIde
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            DataContext = new MainViewModel();
            if (DataContext is MainViewModel vm)
                vm.PropertyChanged += Vm_PropertyChanged;
            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
            StateChanged += MainWindow_StateChanged;
        }

        private void MainWindow_StateChanged(object sender, EventArgs e)
        {
            if (WindowState == WindowState.Maximized)
            {
                BorderThickness = new System.Windows.Thickness(7);
            }
            else
            {
                BorderThickness = new System.Windows.Thickness(0);
            }
        }

        // ------------------------------------------------------------
        // Window and AvalonDock state persistence.
        // ------------------------------------------------------------
        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            var state = Application.Current?.Properties["LayoutState"] as LayoutState ?? new LayoutState();

            Themes.ThemeManager.ApplyTheme(string.IsNullOrWhiteSpace(state.Theme) ? "Light" : state.Theme);
            if (DataContext is MainViewModel vm)
            {
                vm.RepositoryPanelExpanded = state.RepositoryPanelExpanded;
                vm.RunOutputPanelExpanded = state.RunOutputPanelExpanded;
            }

            ApplyWindowState(state);

            if (!string.IsNullOrWhiteSpace(state.DockLayoutJson))
            {
                try
                {
                    var serializer = new JsonLayoutSerializer(dockManager);
                    serializer.LayoutSerializationCallback += (_, args) =>
                    {
                        if (CreatePaneContentForTitle(args.Model.Title) is { } content)
                            args.Content = content;
                        else
                            args.Cancel = true;
                    };
                    using var stream = new MemoryStream(Encoding.UTF8.GetBytes(state.DockLayoutJson));
                    serializer.Deserialize(stream);

                    DeduplicateLayout(state.DockLayoutJson);
                    if (!RestoreDockPaneContent())
                        throw new InvalidDataException("The saved AvalonDock layout contains no recognized IDE panes.");

                    RestoreAutoHidePanesFromJson(state.DockLayoutJson);
                    RestoreActivePane(state);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Dock layout restore failed: {ex}");
                    ResetDockLayout();
                    state = new LayoutState();
                }
            }
            else
            {
                RestoreActivePane(state);
            }

            if (Application.Current != null)
                Application.Current.Properties["LayoutState"] = state;
            dockManager.UpdateLayout();
            ScheduleDockStateReapply(state);
        }

        private void ApplyWindowState(LayoutState state)
        {
            if (state.Width > 100 && state.Height > 100 && double.IsFinite(state.Width) && double.IsFinite(state.Height))
            {
                Width = state.Width;
                Height = state.Height;
            }

            if (state.WindowState == (int)System.Windows.WindowState.Maximized)
                WindowState = System.Windows.WindowState.Maximized;
            else
                WindowState = System.Windows.WindowState.Normal;

            if (!state.HasWindowBounds || WindowState != System.Windows.WindowState.Normal)
                return;

            double virtualRight = SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth;
            double virtualBottom = SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight;
            bool intersectsScreen = state.Left + Width > SystemParameters.VirtualScreenLeft + 80
                && state.Left < virtualRight - 80
                && state.Top + 32 > SystemParameters.VirtualScreenTop
                && state.Top < virtualBottom - 32;
            if (intersectsScreen && double.IsFinite(state.Left) && double.IsFinite(state.Top))
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = state.Left;
                Top = state.Top;
            }
            else
            {
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
        }

        private void ScheduleDockStateReapply(LayoutState state)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                ReapplyDockState(state);
                // AvalonDock may run another child-size normalization after the first
                // Loaded callback. Reapply after that pass as well.
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => ReapplyDockState(state)));
            }));
        }

        private void ReapplyDockState(LayoutState state)
        {
            try
            {
                RestoreActivePane(state);
                dockManager.UpdateLayout();
                ApplyPaneWidths(state.PaneWidths);
                ApplyPaneHeights(state.PaneHeights);
                ApplySplitterState(state);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Deferred dock state restore failed: {ex}");
            }
        }

        private void ApplySplitterState(LayoutState state)
        {
            if (state.TreeColumnWidth <= 0 || state.PropertiesColumnWidth <= 0
                || !double.IsFinite(state.TreeColumnWidth) || !double.IsFinite(state.PropertiesColumnWidth))
                return;

            try
            {
                var anchorable = FindAnchorableByTitle(dockManager.Layout, "Elements");
                if (anchorable?.Content is DependencyObject content)
                {
                    var elementsPane = FindVisualChild<Docking.Views.ElementsPane>(content);
                    if (elementsPane != null)
                    {
                        elementsPane.ApplySplitterState(state.TreeColumnWidth, state.PropertiesColumnWidth);
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Elements pane splitter restore skipped: {ex}");
            }

            System.Diagnostics.Debug.WriteLine("Elements pane splitter was not materialized during restore.");
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            var previousState = Application.Current?.Properties["LayoutState"] as LayoutState;
            double treeWidth = 0;
            double propertiesWidth = 0;
            try
            {
                var elements = FindAnchorableByTitle(dockManager.Layout, "Elements");
                if (elements?.Content is DependencyObject content)
                {
                    var pane = FindVisualChild<Docking.Views.ElementsPane>(content);
                    if (pane != null)
                        (treeWidth, propertiesWidth) = pane.SnapshotSplitterState();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Elements pane splitter snapshot skipped: {ex}");
            }

            double restoreWidth = RestoreBounds.Width > 100 ? RestoreBounds.Width : Width;
            double restoreHeight = RestoreBounds.Height > 100 ? RestoreBounds.Height : Height;
            var activePane = GetActivePaneId() ?? (DataContext as MainViewModel)?.ActivePaneId;
            var state = new LayoutState
            {
                WindowState = (int)(WindowState == System.Windows.WindowState.Minimized
                    ? System.Windows.WindowState.Normal
                    : WindowState),
                HasWindowBounds = true,
                Top = WindowState == System.Windows.WindowState.Normal ? Top : RestoreBounds.Top,
                Left = WindowState == System.Windows.WindowState.Normal ? Left : RestoreBounds.Left,
                Width = WindowState == System.Windows.WindowState.Normal && ActualWidth > 100 ? ActualWidth : restoreWidth,
                Height = WindowState == System.Windows.WindowState.Normal && ActualHeight > 100 ? ActualHeight : restoreHeight,
                Theme = Themes.ThemeManager.CurrentTheme,
                ActivePaneId = activePane,
                SelectedTabIndex = (DataContext as MainViewModel)?.SelectedTabIndex ?? 0,
                TreeColumnWidth = treeWidth,
                PropertiesColumnWidth = propertiesWidth,
                RepositoryPanelExpanded = (DataContext as MainViewModel)?.RepositoryPanelExpanded ?? false,
                RunOutputPanelExpanded = (DataContext as MainViewModel)?.RunOutputPanelExpanded ?? false,
                DockLayoutJson = previousState?.DockLayoutJson,
            };

            // Capture absolute pane dimensions before SerializeLayout calls
            // DockingManager.UpdateLayout(). AvalonDock may normalize resized
            // widths to star units during that layout pass.
            SnapshotPaneWidths(state);
            state.DockLayoutJson = SerializeLayout() ?? previousState?.DockLayoutJson;
            LayoutPersistence.Save(state);
            if (Application.Current != null)
                Application.Current.Properties["LayoutState"] = state;
        }

        public void ResetLayout()
        {
            try
            {
                var state = new LayoutState();
                ResetDockLayout();
                Width = state.Width;
                Height = state.Height;
                WindowState = System.Windows.WindowState.Normal;
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
                Left = SystemParameters.WorkArea.Left + (SystemParameters.WorkArea.Width - Width) / 2;
                Top = SystemParameters.WorkArea.Top + (SystemParameters.WorkArea.Height - Height) / 2;
                Themes.ThemeManager.ApplyTheme(state.Theme);

                if (DataContext is MainViewModel vm)
                {
                    vm.RepositoryPanelExpanded = false;
                    vm.RunOutputPanelExpanded = false;
                    vm.ActivePaneId = "Elements";
                }

                if (Application.Current != null)
                    Application.Current.Properties["LayoutState"] = state;
                LayoutPersistence.Save(state);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ResetLayout failed: {ex}");
            }
        }

        private void ResetDockLayout()
        {
            var root = new LayoutRoot();
            var rootPanel = new LayoutPanel { Orientation = Orientation.Horizontal };

            var workingFolder = new LayoutAnchorable { Title = "WORKING FOLDER", IsActive = false };
            workingFolder.Content = CreatePaneContent(new Docking.Views.WorkingFolderPane(), "tabWorkingFolder");
            var workingFolderPane = new LayoutAnchorablePane { DockWidth = new GridLength(0.2, GridUnitType.Star) };
            workingFolderPane.Children.Add(workingFolder);

            var rightGroup = new LayoutPanel { Orientation = Orientation.Vertical, DockWidth = new GridLength(1, GridUnitType.Star) };
            var tabs = new LayoutAnchorablePane();
            var elements = new LayoutAnchorable { Title = "ELEMENTS", IsActive = true };
            elements.Content = CreatePaneContent(new Docking.Views.ElementsPane(), "tabElements");
            var scripts = new LayoutAnchorable { Title = "SCRIPTS" };
            scripts.Content = CreatePaneContent(new Docking.Views.ScriptsPane(), "tabScripts");
            var results = new LayoutAnchorable { Title = "RESULTS" };
            results.Content = CreatePaneContent(new Docking.Views.ResultsPane(), "tabResults");
            tabs.Children.Add(elements);
            tabs.Children.Add(scripts);
            tabs.Children.Add(results);
            rightGroup.Children.Add(tabs);

            rootPanel.Children.Add(workingFolderPane);
            rootPanel.Children.Add(rightGroup);
            root.RootPanel = rootPanel;
            dockManager.Layout = root;
            dockManager.UpdateLayout();
        }

        private string? SerializeLayout()
        {
            try
            {
                dockManager.UpdateLayout();
                var serializer = new JsonLayoutSerializer(dockManager);
                using var stream = new MemoryStream();
                serializer.Serialize(stream);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Dock layout serialize failed: {ex}");
                return null;
            }
        }

        private void SnapshotPaneWidths(LayoutState state)
        {
            state.PaneWidths.Clear();
            state.PaneHeights.Clear();
            if (dockManager.Layout == null) return;

            foreach (var pane in EnumerateLayoutElements(dockManager.Layout).OfType<LayoutAnchorablePane>())
            {
                var width = pane.DockWidth;
                var height = pane.DockHeight;
                foreach (var anchorable in pane.Children)
                {
                    var id = CanonicalPaneId(anchorable.Title);
                    if (id.Length == 0) continue;
                    if (width.IsAbsolute && width.Value > 0 && double.IsFinite(width.Value))
                        state.PaneWidths[id] = width.Value;
                    if (height.IsAbsolute && height.Value > 0 && double.IsFinite(height.Value))
                        state.PaneHeights[id] = height.Value;
                }
            }
        }

        private void ApplyPaneWidths(IReadOnlyDictionary<string, double>? widths)
        {
            if (widths == null || widths.Count == 0 || dockManager.Layout == null) return;
            foreach (var pane in EnumerateLayoutElements(dockManager.Layout).OfType<LayoutAnchorablePane>())
            {
                var saved = pane.Children.Select(item => CanonicalPaneId(item.Title))
                    .Where(id => id.Length > 0 && widths.ContainsKey(id))
                    .Select(id => widths[id]).FirstOrDefault(value => value > 0 && double.IsFinite(value));
                if (saved > 0)
                    pane.DockWidth = new GridLength(saved, GridUnitType.Pixel);
            }
        }

        private void ApplyPaneHeights(IReadOnlyDictionary<string, double>? heights)
        {
            if (heights == null || heights.Count == 0 || dockManager.Layout == null) return;
            foreach (var pane in EnumerateLayoutElements(dockManager.Layout).OfType<LayoutAnchorablePane>())
            {
                var saved = pane.Children.Select(item => CanonicalPaneId(item.Title))
                    .Where(id => id.Length > 0 && heights.ContainsKey(id))
                    .Select(id => heights[id]).FirstOrDefault(value => value > 0 && double.IsFinite(value));
                if (saved > 0)
                    pane.DockHeight = new GridLength(saved, GridUnitType.Pixel);
            }
        }

        private void RestoreActivePane(LayoutState state)
        {
            var paneId = CanonicalPaneId(state.ActivePaneId);
            if (paneId.Length == 0)
                paneId = state.SelectedTabIndex switch
                {
                    1 => "Scripts",
                    2 => "Results",
                    3 => "Working Folder",
                    _ => "Elements",
                };

            var anchorable = FindAnchorableByTitle(dockManager.Layout, paneId);
            if (anchorable == null || anchorable.IsHidden) return;
            anchorable.IsSelected = true;
            anchorable.IsActive = true;
            if (DataContext is MainViewModel vm)
                vm.ActivePaneId = paneId;
        }

        private string? GetActivePaneId()
        {
            if (dockManager.Layout == null) return null;
            var anchorables = EnumerateLayoutElements(dockManager.Layout).OfType<LayoutAnchorable>().ToList();
            var active = anchorables.FirstOrDefault(item => item.IsActive)
                ?? anchorables.FirstOrDefault(item => item.IsSelected);
            return active == null ? null : CanonicalPaneId(active.Title);
        }

        private void DeduplicateLayout(string layoutJson)
        {
            if (dockManager.Layout == null) return;
            var autoHideTitles = ReadAutoHideGroups(layoutJson)
                .SelectMany(group => group.PaneTitles)
                .Select(CanonicalPaneId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var group in EnumerateLayoutElements(dockManager.Layout).OfType<LayoutAnchorable>()
                         .Where(item => !string.IsNullOrWhiteSpace(item.Title))
                         .GroupBy(item => CanonicalPaneId(item.Title), StringComparer.OrdinalIgnoreCase)
                         .Where(group => group.Count() > 1))
            {
                bool shouldAutoHide = autoHideTitles.Contains(group.Key);
                var keep = group.OrderByDescending(item => shouldAutoHide ? item.IsAutoHidden : !item.IsAutoHidden)
                    .ThenByDescending(item => item.IsActive || item.IsSelected)
                    .First();
                foreach (var duplicate in group.Where(item => !ReferenceEquals(item, keep)).ToArray())
                {
                    try
                    {
                        if (duplicate.Parent is ILayoutContainer parent)
                            parent.RemoveChild(duplicate);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Could not remove duplicate dock pane '{duplicate.Title}': {ex.Message}");
                    }
                }
            }
        }

        private void RestoreAutoHidePanesFromJson(string layoutJson)
        {
            if (dockManager.Layout is not LayoutRoot root) return;
            foreach (var savedGroup in ReadAutoHideGroups(layoutJson))
            {
                var targetSide = GetAnchorSide(root, savedGroup.Side);
                foreach (var title in savedGroup.PaneTitles)
                {
                    var anchorable = FindAnchorableByTitle(root, title);
                    if (anchorable == null) continue;

                    try
                    {
                        if (anchorable.IsHidden)
                            anchorable.Show();
                        if (!anchorable.IsAutoHidden)
                            anchorable.ToggleSingleAutoHide();

                        if (anchorable.Parent is LayoutAnchorGroup group && group.Parent is LayoutAnchorSide currentSide
                            && !ReferenceEquals(currentSide, targetSide))
                        {
                            currentSide.Children.Remove(group);
                            targetSide.Children.Add(group);
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Auto-hide restore for '{anchorable.Title}' skipped: {ex}");
                    }
                }
            }
        }

        private static List<AutoHideGroup> ReadAutoHideGroups(string layoutJson)
        {
            var result = new List<AutoHideGroup>();
            try
            {
                using var document = JsonDocument.Parse(layoutJson);
                var root = document.RootElement;
                foreach (var (propertyName, side) in new[]
                         {
                             ("TopSide", AnchorSide.Top), ("RightSide", AnchorSide.Right),
                             ("BottomSide", AnchorSide.Bottom), ("LeftSide", AnchorSide.Left),
                         })
                {
                    if (!TryGetPropertyIgnoreCase(root, propertyName, out var sideElement)
                        || !TryGetPropertyIgnoreCase(sideElement, "Children", out var groups)
                        || groups.ValueKind != JsonValueKind.Array)
                        continue;

                    foreach (var group in groups.EnumerateArray())
                    {
                        if (!TryGetPropertyIgnoreCase(group, "Children", out var children)
                            || children.ValueKind != JsonValueKind.Array)
                            continue;
                        var titles = children.EnumerateArray()
                            .Select(item => TryGetPropertyIgnoreCase(item, "Title", out var title) && title.ValueKind == JsonValueKind.String
                                ? title.GetString()
                                : null)
                            .Where(title => !string.IsNullOrWhiteSpace(title))
                            .Select(title => title!)
                            .ToArray();
                        if (titles.Length > 0)
                            result.Add(new AutoHideGroup(side, titles));
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Could not read saved AvalonDock auto-hide sides: {ex.Message}");
            }
            return result;
        }

        private sealed record AutoHideGroup(AnchorSide Side, string[] PaneTitles);

        private static bool TryGetPropertyIgnoreCase(JsonElement element, string name, out JsonElement value)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in element.EnumerateObject())
                {
                    if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        value = property.Value;
                        return true;
                    }
                }
            }
            value = default;
            return false;
        }

        private static LayoutAnchorSide GetAnchorSide(LayoutRoot root, AnchorSide side) => side switch
        {
            AnchorSide.Left => root.LeftSide,
            AnchorSide.Right => root.RightSide,
            AnchorSide.Top => root.TopSide,
            AnchorSide.Bottom => root.BottomSide,
            _ => root.LeftSide,
        };

        private bool RestoreDockPaneContent()
        {
            if (dockManager.Layout == null) return false;
            int recognizedPanes = 0;
            foreach (var anchorable in EnumerateLayoutElements(dockManager.Layout).OfType<LayoutAnchorable>())
            {
                var paneContent = CreatePaneContentForTitle(anchorable.Title);
                if (paneContent == null) continue;
                recognizedPanes++;
                if (anchorable.Content == null)
                    anchorable.Content = paneContent;
            }
            return recognizedPanes > 0;
        }

        private static object? CreatePaneContentForTitle(string? title)
        {
            var pane = CanonicalPaneId(title) switch
            {
                "Elements" => (UserControl)new Docking.Views.ElementsPane(),
                "Scripts" => new Docking.Views.ScriptsPane(),
                "Results" => new Docking.Views.ResultsPane(),
                "Working Folder" => new Docking.Views.WorkingFolderPane(),
                _ => null,
            };
            if (pane == null) return null;
            var automationId = CanonicalPaneId(title) switch
            {
                "Elements" => "tabElements",
                "Scripts" => "tabScripts",
                "Results" => "tabResults",
                _ => "tabWorkingFolder",
            };
            return CreatePaneContent(pane, automationId);
        }

        private static Grid CreatePaneContent(UserControl pane, string automationId)
        {
            var grid = new Grid();
            AutomationProperties.SetAutomationId(grid, automationId);
            grid.Children.Add(pane);
            return grid;
        }

        private static string CanonicalPaneId(string? title)
        {
            if (string.IsNullOrWhiteSpace(title)) return string.Empty;
            return title.Trim().ToUpperInvariant() switch
            {
                "ELEMENTS" => "Elements",
                "SCRIPTS" => "Scripts",
                "RESULTS" => "Results",
                "WORKING FOLDER" => "Working Folder",
                _ => title.Trim(),
            };
        }

        private static LayoutAnchorable? FindAnchorableByTitle(ILayoutElement? element, string title)
        {
            if (element == null) return null;
            var canonical = CanonicalPaneId(title);
            if (element is LayoutAnchorable anchorable
                && string.Equals(CanonicalPaneId(anchorable.Title), canonical, StringComparison.OrdinalIgnoreCase))
                return anchorable;
            if (element is ILayoutContainer container)
            {
                foreach (var child in container.Children)
                {
                    var found = FindAnchorableByTitle(child, canonical);
                    if (found != null) return found;
                }
            }
            return null;
        }

        private static IEnumerable<ILayoutElement> EnumerateLayoutElements(ILayoutElement element)
        {
            yield return element;
            if (element is not ILayoutContainer container) yield break;
            foreach (var child in container.Children.ToArray())
            {
                foreach (var descendant in EnumerateLayoutElements(child))
                    yield return descendant;
            }
        }

        private void ActivityElements_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm)
            {
                vm.ShowElementsCommand.Execute(null);
            }
        }

        private void ActivityScripts_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm)
            {
                vm.ShowScriptsCommand.Execute(null);
            }
        }

        private void ActivityRun_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm)
            {
                vm.RunCommand.Execute(null);
            }
        }

        private void ActivitySpy_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm)
            {
                vm.OpenSpyToolCommand.Execute(null);
            }
        }

        private void ActivityBuilder_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm)
            {
                vm.OpenVisualTestBuilderCommand.Execute(null);
            }
        }

        private void ActivitySettings_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm)
            {
                vm.OpenSettingsCommand.Execute(null);
            }
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is System.Windows.Media.Visual visual && FindAncestor<Button>(visual) != null) return;
            if (e.ClickCount == 2 && WindowState != WindowState.Maximized)
            {
                Maximize_Click(null, null);
                return;
            }
            DragMove();
        }

        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void Maximize_Click(object sender, RoutedEventArgs e)
        {
            if (WindowState == WindowState.Maximized)
            {
                WindowState = WindowState.Normal;
            }
            else
            {
                WindowState = WindowState.Maximized;
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        // ------------------------------------------------------------
        // D3: drag-to-reorder recorded steps in StepsListBox.
        // Strategy: capture the hit-tested RecordedStep on PreviewMouseLeftButtonDown
        // (only when the press is NOT on a Button, so ?/?/verify/X keep working),
        // then start DoDragDrop on PreviewMouseMove once the mouse moves past the
        // system drag threshold. On Drop, find the RecordedStep under the cursor
        // and move the dragged step to that position via MainViewModel.MoveStepTo.
        // ------------------------------------------------------------
        private RecordedStep? _draggedStep;
        private Point _dragOrigin;
        private bool _dragPossible;

        private void StepsListBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragPossible = false;
            _draggedStep = null;
            if (sender is not ListBox listBox) return;
            // If the press originated on a Button (?/?/verify/X), don't start a drag.
            if (e.OriginalSource is DependencyObject d && FindAncestor<Button>(d) != null) return;
            var hit = TryGetStepFromPoint(listBox, e.GetPosition(listBox));
            if (hit == null) return;
            _draggedStep = hit;
            _dragOrigin = e.GetPosition(null);
            _dragPossible = true;
        }

        private void StepsListBox_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!_dragPossible || _draggedStep == null) return;
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                _dragPossible = false;
                _draggedStep = null;
                return;
            }
            var pos = e.GetPosition(null);
            var dx = Math.Abs(pos.X - _dragOrigin.X);
            var dy = Math.Abs(pos.Y - _dragOrigin.Y);
            if (dx < SystemParameters.MinimumHorizontalDragDistance &&
                dy < SystemParameters.MinimumVerticalDragDistance) return;
            // Past threshold: start the drag.
            if (sender is ListBox listBox)
            {
                _dragPossible = false;
                DragDrop.DoDragDrop(listBox, _draggedStep, DragDropEffects.Move);
            }
        }

        private void StepsListBox_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(typeof(RecordedStep))
                ? DragDropEffects.Move
                : DragDropEffects.None;
            e.Handled = true;
        }

        private void StepsListBox_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(typeof(RecordedStep)) && DataContext is MainViewModel vm)
            {
                var dragged = e.Data.GetData(typeof(RecordedStep)) as RecordedStep;
                if (dragged == null) return;
                if (sender is ListBox listBox)
                {
                    var target = TryGetStepFromPointEx(listBox, e);
                    if (target != null && !ReferenceEquals(target, dragged))
                    {
                        int currentIndex = vm.Steps.IndexOf(dragged);
                        int targetIndex = vm.Steps.IndexOf(target);
                        // Standard ListBox reorder: drop in the lower half of a row ->
                        // insert *after* it. `ObservableCollection.Move` removes the
                        // source before inserting, so when dragging DOWN (source above
                        // target) removing the source shifts the target's index down by
                        // one. Compensate with `targetIndex--` for the down case so the
                        // step actually lands where the drop visual promised.
                        bool draggingDown = currentIndex >= 0 && currentIndex < targetIndex;
                        try
                        {
                            var itemContainer = listBox.ItemContainerGenerator.ContainerFromItem(target) as ListBoxItem;
                            if (itemContainer != null)
                            {
                                double relY = e.GetPosition(itemContainer).Y;
                                if (relY > itemContainer.ActualHeight / 2) targetIndex++;
                            }
                        }
                        catch { /* bounds race: fall back to insert-before */ }
                        if (draggingDown) targetIndex--;
                        vm.MoveStepTo(dragged, targetIndex);
                    }
                }
            }
            e.Handled = true;
        }

        private static RecordedStep? TryGetStepFromPoint(ListBox listBox, Point point)
        {
            var element = listBox.InputHitTest(point) as DependencyObject;
            if (element == null) return null;
            return FindDataContext<RecordedStep>(element);
        }

        // Thin wrapper that adapts Drop's DragEventArgs to TryGetStepFromPoint,
        // so the hit-test/tree-walk logic lives in exactly one place.
        private static RecordedStep? TryGetStepFromPointEx(ListBox listBox, DragEventArgs e)
            => TryGetStepFromPoint(listBox, e.GetPosition(listBox));

        private static T? FindDataContext<T>(DependencyObject? element) where T : class
        {
            while (element != null && element is not Window)
            {
                if (element is FrameworkContentElement fce && fce.DataContext is T t) return t;
                if (element is FrameworkElement fe && fe.DataContext is T t2) return t2;
                element = GetParentSafe(element);
            }
            return null;
        }

        private static T? FindAncestor<T>(DependencyObject? element) where T : DependencyObject
        {
            while (element != null)
            {
                if (element is T t) return t;
                element = GetParentSafe(element);
            }
            return null;
        }

        /// <summary>Walks the visual tree downward to find the first child of
        /// type <typeparamref name="T"/>. Used to locate the ElementsPane UserControl
        /// from the LayoutAnchorable's Content host.</summary>
        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) return null;
            int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is T t) return t;
                var found = FindVisualChild<T>(child);
                if (found != null) return found;
            }
            return null;
        }

        /// <summary>WPF elements inside text (Run, Inline, Hyperlink, ContentElement)
        /// are not Visual/Visual3D, so VisualTreeHelper.GetParent throws on them. Walk
        /// the logical tree (LogicalTreeHelper.GetParent) for those, and the visual tree
        /// for Visual elements; fall back to logical for anything else. Used by
        /// FindAncestor/FindDataContext so drag hit-testing never throws.</summary>
        private static DependencyObject? GetParentSafe(DependencyObject element)
        {
            // Visual -> visual tree.
            if (element is System.Windows.Media.Visual)
            {
                return System.Windows.Media.VisualTreeHelper.GetParent(element);
            }
            // ContentElement / FrameworkContentElement / other DependencyObject -> logical tree.
            return System.Windows.LogicalTreeHelper.GetParent(element);
        }

        private void Vm_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainViewModel.ActivePaneId)
                && DataContext is MainViewModel vm
                && !string.IsNullOrWhiteSpace(vm.ActivePaneId))
            {
                ShowPane(vm.ActivePaneId);
            }
        }

        private void ShowPane(string paneId)
        {
            var anchorable = FindAnchorableByTitle(dockManager.Layout, paneId);
            if (anchorable == null) return;
            if (anchorable.IsHidden)
                anchorable.Show();
            anchorable.IsSelected = true;
            anchorable.IsActive = true;
        }

    }
}
