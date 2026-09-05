using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
        // E1: layout + theme persistence.
        // On Loaded apply the snapshot loaded by App.OnStartup; on Closing
        // capture the live window/panel/splitter state back into a new
        // LayoutState and save it via LayoutPersistence.
        // ------------------------------------------------------------
        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (Application.Current?.Properties["LayoutState"] is not LayoutState state) return;

            // Theme first so the rest of the UI paints in the user's theme.
            Themes.ThemeManager.ApplyTheme(string.IsNullOrWhiteSpace(state.Theme) ? "Light" : state.Theme);
            if (DataContext is MainViewModel vm)
            {
                vm.OcrPanelExpanded = state.OcrPanelExpanded;
                vm.RepositoryPanelExpanded = state.RepositoryPanelExpanded;
                vm.RunOutputPanelExpanded = state.RunOutputPanelExpanded;
            }

            // -------- Window geometry FIRST --------
            // Apply size/state/position before deserializing the dock layout so the
            // DockingManager rebuilds into an already-correctly-sized host. Previously
            // geometry was applied AFTER the dock restore, which meant AvalonDock laid
            // out against the transient XAML default size and the pane widths drifted
            // from what was saved. Only apply if the persisted size is reasonable;
            // ignore obviously-broken values (zero/negative) so a corrupted file
            // can't push the window off-screen or shrink it to nothing.
            if (state.Width > 100 && state.Height > 100)
            {
                Width = state.Width;
                Height = state.Height;
            }
            if (System.Windows.WindowState.Normal <= (System.Windows.WindowState)state.WindowState
                && (System.Windows.WindowState)state.WindowState <= System.Windows.WindowState.Maximized)
            {
                WindowState = (System.Windows.WindowState)state.WindowState;
            }
            // Position only when Normal (a maximized window has Top/Left off-screen
            // on some multi-monitor configs). Restore to TopLeft so windows reopen
            // where they were; guard against off-screen placement.
            // Treat 0,0 as "unset" (fresh layout.json) and fall back to centering.
            if (WindowState == System.Windows.WindowState.Normal)
            {
                if (state.Left > 0 || state.Top > 0)
                {
                    if (state.Left >= SystemParameters.VirtualScreenLeft - 10
                        && state.Top >= SystemParameters.VirtualScreenTop - 10
                        && state.Left < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 100
                        && state.Top < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 100)
                    {
                        Left = state.Left;
                        Top = state.Top;
                    }
                    else
                    {
                        // Persisted position is off the current monitor layout -> fall
                        // back to centering so the window stays reachable.
                        WindowStartupLocation = WindowStartupLocation.CenterScreen;
                    }
                }
                else
                {
                    WindowStartupLocation = WindowStartupLocation.CenterScreen;
                }
            }
            // A6+E4 step 3: dock layout restore + pane activation.
            if (!string.IsNullOrWhiteSpace(state.DockLayoutJson))
            {
                try
                {
                    System.Diagnostics.Debug.WriteLine("=== LAYOUT RESTORE: BEFORE DESERIALIZE ===");
                    LogLayoutStructure("XAML Default", dockManager.Layout);
                    
                    var serializer = new JsonLayoutSerializer(dockManager);
                    using var stream = new MemoryStream(Encoding.UTF8.GetBytes(state.DockLayoutJson));
                    serializer.Deserialize(stream);
                    
                    System.Diagnostics.Debug.WriteLine("=== LAYOUT RESTORE: AFTER DESERIALIZE ===");
                    LogLayoutStructure("After Deserialize", dockManager.Layout);
                    
                    // JsonLayoutSerializer cannot persist UIElement Content (UserControls),
                    // so deserialized LayoutAnchorables arrive with null Content. Re-inject
                    // the pane UserControls here so the three tabs are visible after restart.
                    RestoreDockPaneContent();
                    
                    // JsonLayoutSerializer v5 BUG: does NOT deserialize LeftSide/RightSide/TopSide/BottomSide
                    // auto-hide panes. Manually restore them from the saved JSON.
                    RestoreAutoHidePanesFromJson(state.DockLayoutJson);
                    
                    DeduplicateLayout();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Dock layout deserialize failed: {ex.Message}");
                    // Fall back to the XAML-defined default layout (already set).
                }
            }
            
            System.Diagnostics.Debug.WriteLine("=== LAYOUT RESTORE: AFTER CONTENT RESTORE ===");
            LogLayoutStructure("After RestoreDockPaneContent", dockManager.Layout);
            
            if (!string.IsNullOrWhiteSpace(state.ActivePaneId))
            {
                ShowPane(state.ActivePaneId);
            }
            else if (state.SelectedTabIndex >= 0 && state.SelectedTabIndex < 3)
            {
                ShowPane(state.SelectedTabIndex switch { 1 => "Scripts", 2 => "Results", _ => "Elements" });
            }

            // Defer the post-restore layout pass + pane-width re-application until the
            // visual tree has realized the docked pane content. Two phases so the order
            // is deterministic:
            //   1) UpdateLayout - processes auto-hide sides JsonLayoutSerializer restored
            //      but the visual tree hadn't realized yet, and realizes pane content.
            //   2) ApplyPaneWidths / ApplySplitterState - re-assert the persisted absolute
            //      pane widths AFTER the dock layout has been (re)built, so they win
            //      regardless of whether the dock JSON round-tripped them correctly or
            //      fell back to defaults.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                dockManager.UpdateLayout();
                ApplyPaneWidths(state.PaneWidths);
                ApplySplitterState(state);

                System.Diagnostics.Debug.WriteLine("=== LAYOUT RESTORE: AFTER UPDATELAYOUT ===");
                LogLayoutStructure("After UpdateLayout", dockManager.Layout);
            }), DispatcherPriority.Loaded);
        }

        private void ApplySplitterState(LayoutState state)
        {
            try
            {
                var elementsAnchorable = FindAnchorableByTitle(dockManager.Layout, "Elements");
                if (elementsAnchorable?.Content is FrameworkElement fe)
                {
                    var elementsPane = fe.FindName("ElementsPaneRoot") as Docking.Views.ElementsPane
                        ?? FindVisualChild<Docking.Views.ElementsPane>(fe);
                    elementsPane?.ApplySplitterState(state.TreeColumnWidth, state.PropertiesColumnWidth);
                }
            }
            catch (Exception ex)
            {
                // Stop hiding the cause: a silent no-op here means the Element Tree
                // <-> Properties splitter width is silently lost.
                System.Diagnostics.Debug.WriteLine($"ElementsPane splitter restore skipped: {ex.Message}");
            }
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (DataContext is not MainViewModel vm) return;
            
            // Snapshot ElementsPane splitter state from the live pane content.
            double treeWidth = 0, propsWidth = 0;
            try
            {
                var elementsAnchorable = FindAnchorableByTitle(dockManager.Layout, "Elements");
                if (elementsAnchorable?.Content is FrameworkElement fe)
                {
                    var elementsPane = fe.FindName("ElementsPaneRoot") as Docking.Views.ElementsPane
                        ?? FindVisualChild<Docking.Views.ElementsPane>(fe);
                    if (elementsPane != null)
                    {
                        (treeWidth, propsWidth) = elementsPane.SnapshotSplitterState();
                    }
                }
            }
            catch (Exception ex)
            {
                // Persist zeros if the pane isn't reachable, but stop hiding the cause:
                // a silent no-op here means the splitter width is silently lost.
                System.Diagnostics.Debug.WriteLine($"ElementsPane splitter snapshot skipped: {ex.Message}");
            }
            
            var state = new LayoutState
            {
                WindowState = (int)WindowState,
                Top = Top,
                Left = Left,
                // When maximized/minimized, ActualWidth/Height are the maximized
                // size; save RestoreBounds instead so the "restored" size is correct.
                Width = WindowState == System.Windows.WindowState.Normal && ActualWidth > 0 ? ActualWidth : RestoreBounds.Width,
                Height = WindowState == System.Windows.WindowState.Normal && ActualHeight > 0 ? ActualHeight : RestoreBounds.Height,
                Theme = Themes.ThemeManager.CurrentTheme,
                SelectedTabIndex = vm.SelectedTabIndex,
                TreeColumnWidth = treeWidth,
                PropertiesColumnWidth = propsWidth,
                OcrPanelExpanded = vm.OcrPanelExpanded,
                RepositoryPanelExpanded = vm.RepositoryPanelExpanded,
                RunOutputPanelExpanded = vm.RunOutputPanelExpanded,
                ActivePaneId = vm.ActivePaneId ?? (vm.SelectedTabIndex switch { 1 => "Scripts", 2 => "Results", _ => "Elements" }),
                DockLayoutJson = SerializeLayout(dockManager),
                // E1: explicit absolute pane widths, independent of the dock JSON.
                PaneWidths = SnapshotPaneWidths(),
            };
            LayoutPersistence.Save(state);
        }

        public void ResetLayout()
        {
            try
            {
                // Clear the saved layout state so next launch uses XAML defaults.
                var defaultState = new LayoutState();
                LayoutPersistence.Save(defaultState);

                // Recreate the default dock panes in the root panel.
                if (dockManager.Layout?.RootPanel is LayoutPanel rootPanel)
                {
                    RecreateDefaultDockPanes(rootPanel);
                    dockManager.UpdateLayout();
                }

                // Reset window geometry to defaults.
                Width = defaultState.Width;
                Height = defaultState.Height;
                WindowState = System.Windows.WindowState.Normal;
                Left = 0;
                Top = 0;
                WindowStartupLocation = WindowStartupLocation.CenterScreen;

                // Reset splitter state to defaults.
                if (DataContext is MainViewModel vm)
                {
                    vm.RepositoryPanelExpanded = false;
                    vm.OcrPanelExpanded = false;
                    vm.RunOutputPanelExpanded = false;
                }

                System.Diagnostics.Debug.WriteLine("Layout reset to defaults.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ResetLayout failed: {ex.Message}");
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
        // (only when the press is NOT on a Button, so ↑/↓/verify/X keep working),
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
            // If the press originated on a Button (↑/↓/verify/X), don't start a drag.
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
            if (e.PropertyName == nameof(MainViewModel.ActivePaneId))
            {
                if (DataContext is MainViewModel vm && vm.ActivePaneId is string paneId)
                {
                    ShowPane(paneId);
                }
            }
        }

        private void ShowPane(string paneId)
        {
            if (dockManager.Layout == null) return;
            var anchorable = FindAnchorableByTitle(dockManager.Layout.RootPanel, paneId);
            anchorable?.Show();
        }

        private static LayoutAnchorable? FindAnchorableByTitle(ILayoutElement element, string title)
        {
            if (element is LayoutAnchorable anchorable && string.Equals(anchorable.Title, title, StringComparison.OrdinalIgnoreCase))
                return anchorable;
            if (element is ILayoutContainer container)
            {
                foreach (var child in container.Children)
                {
                    var found = FindAnchorableByTitle(child, title);
                    if (found != null) return found;
                }
            }
            return null;
        }

        private string SerializeLayout(DockingManager dm)
        {
            try
            {
                // Force pending layout passes so runtime changes (auto-hide, float, dock)
                // are committed to the model before serialization.
                dm.UpdateLayout();
                var serializer = new JsonLayoutSerializer(dm);
                using var stream = new MemoryStream();
                serializer.Serialize(stream);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
            catch (Exception ex)
            {
                // Stop swallowing this silently: a null DockLayoutJson here means the
                // next launch falls back to XAML defaults and ALL docked pane widths
                // (and auto-hide/float state) are lost with no diagnostic.
                System.Diagnostics.Debug.WriteLine($"SerializeLayout failed: {ex.Message}");
                return null;
            }
        }

        // ------------------------------------------------------------
        // E1: docked ELEMENTS/SCRIPTS/RESULTS pane-width persistence.
        //
        // JsonLayoutSerializer v5 round-trips LayoutAnchorablePane.DockWidth in
        // principle, but in practice the three docked columns still did not keep
        // their width across restarts: the layout is deserialized/replaced before
        // the window reaches its saved size and before the pane content is
        // materialized, and an exception during serialize (window tearing down)
        // silently wrote DockLayoutJson=null. To make the pane widths reliable we
        // ALSO persist them here as plain absolute pixel values and re-apply them
        // after the DockingManager has been (re)built and UpdateLayout has run.
        //
        // We snapshot DockWidth (not ActualWidth) so the value is independent of
        // whether the visual tree has rendered. Only ABSOLUTE widths are recorded:
        // star widths ("1*") are the XAML default and carry no single pixel value,
        // so they are intentionally omitted and the apply-path leaves them alone.
        // ------------------------------------------------------------
        /// <summary>Snapshots the absolute DockWidth of every docked
        /// LayoutAnchorablePane in the root panel, keyed by the title of its first
        /// child anchorable (Elements/Scripts/Results). Star widths are skipped.</summary>
        private Dictionary<string, double> SnapshotPaneWidths()
        {
            var widths = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (dockManager.Layout?.RootPanel is not LayoutPanel rootPanel) return widths;
                foreach (var child in rootPanel.Children.OfType<LayoutAnchorablePane>())
                {
                    var anchorable = child.Children.FirstOrDefault();
                    if (string.IsNullOrWhiteSpace(anchorable?.Title)) continue;
                    var dockWidth = child.DockWidth;
                    // Only absolute pixel widths are meaningful to persist; star widths
                    // are the default and have no single pixel representation.
                    if (dockWidth.IsAbsolute && dockWidth.Value > 0)
                    {
                        widths[anchorable.Title] = dockWidth.Value;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SnapshotPaneWidths failed: {ex.Message}");
            }
            return widths;
        }

        /// <summary>Re-applies persisted absolute pane widths to the docked
        /// LayoutAnchorablePanes, matched by the title of their first child
        /// anchorable. Called after UpdateLayout so the (possibly deserialized or
        /// default) dock tree has been realized. Star entries are left untouched.</summary>
        private void ApplyPaneWidths(IReadOnlyDictionary<string, double>? paneWidths)
        {
            if (paneWidths == null || paneWidths.Count == 0) return;
            if (dockManager.Layout?.RootPanel is not LayoutPanel rootPanel) return;

            int applied = 0;
            foreach (var child in rootPanel.Children.OfType<LayoutAnchorablePane>())
            {
                var anchorable = child.Children.FirstOrDefault();
                if (string.IsNullOrWhiteSpace(anchorable?.Title)) continue;
                if (paneWidths.TryGetValue(anchorable.Title, out var width) && width > 0)
                {
                    child.DockWidth = new GridLength(width, GridUnitType.Pixel);
                    applied++;
                }
            }
            if (applied > 0)
            {
                // Re-run a layout pass so the new absolute widths take effect visually
                // immediately rather than on the next user interaction.
                dockManager.UpdateLayout();
            }
            System.Diagnostics.Debug.WriteLine($"ApplyPaneWidths: matched {applied}/{paneWidths.Count} pane width(s)");
        }

        // ------------------------------------------------------------
        // A6+E4 layout content restoration.
        // JsonLayoutSerializer cannot persist UIElement Content (UserControls),
        // so deserialized LayoutAnchorables arrive with null Content. Without
        // re-injection the three dock tabs are invisible after restart.
        // Note: JsonLayoutSerializer can wrap our LayoutAnchorablePanes inside
        // a LayoutDocumentPane. The recursive walker below handles whatever
        // structure the serializer produces.
        // ------------------------------------------------------------
        private void RestoreDockPaneContent()
        {
            if (dockManager.Layout?.RootPanel is not LayoutPanel rootPanel) return;
            
            // Only recreate if RootPanel is completely empty (no layout elements at all).
            // After deserialization, the serializer may produce a LayoutDocumentPane
            // containing our panes — that's fine, we just inject content into it.
            if (rootPanel.Children.Count == 0)
            {
                RecreateDefaultDockPanes(rootPanel);
                return;
            }
            
            bool foundAny = false;
            RestoreDockPaneContentRecursive(rootPanel, ref foundAny);
            
            // If no anchorables/documents were found at all (completely corrupted
            // layout), recreate the three expected panes from scratch.
            if (!foundAny)
            {
                RecreateDefaultDockPanes(rootPanel);
            }
        }

        private static void RestoreDockPaneContentRecursive(ILayoutElement element, ref bool foundAny)
        {
            switch (element)
            {
                case LayoutAnchorable anchorable:
                    foundAny = true;
                    if (anchorable.Content == null)
                    {
                        anchorable.Content = anchorable.Title switch
                        {
                            "ELEMENTS" => CreatePaneContent(new Docking.Views.ElementsPane(), "tabElements"),
                            "SCRIPTS" => CreatePaneContent(new Docking.Views.ScriptsPane(), "tabScripts"),
                            "RESULTS" => CreatePaneContent(new Docking.Views.ResultsPane(), "tabResults"),
                            _ => null
                        };
                    }
                    break;
                case LayoutDocument document:
                    foundAny = true;
                    if (document.Content == null)
                    {
                        document.Content = document.Title switch
                        {
                            "ELEMENTS" => CreatePaneContent(new Docking.Views.ElementsPane(), "tabElements"),
                            "SCRIPTS" => CreatePaneContent(new Docking.Views.ScriptsPane(), "tabScripts"),
                            "RESULTS" => CreatePaneContent(new Docking.Views.ResultsPane(), "tabResults"),
                            _ => null
                        };
                    }
                    break;
            }
            
            if (element is ILayoutContainer container)
            {
                foreach (var child in container.Children)
                {
                    RestoreDockPaneContentRecursive(child, ref foundAny);
                }
            }
        }

        private static void RecreateDefaultDockPanes(LayoutPanel rootPanel)
        {
            rootPanel.Children.Clear();
            
            var elementsPane = new LayoutAnchorable { Title = "ELEMENTS", IsActive = true };
            elementsPane.Content = CreatePaneContent(new Docking.Views.ElementsPane(), "tabElements");
            var elementsPaneHost = new LayoutAnchorablePane();
            elementsPaneHost.Children.Add(elementsPane);
            rootPanel.Children.Add(elementsPaneHost);
            
            var scriptsPane = new LayoutAnchorable { Title = "SCRIPTS" };
            scriptsPane.Content = CreatePaneContent(new Docking.Views.ScriptsPane(), "tabScripts");
            var scriptsPaneHost = new LayoutAnchorablePane();
            scriptsPaneHost.Children.Add(scriptsPane);
            rootPanel.Children.Add(scriptsPaneHost);
            
            var resultsPane = new LayoutAnchorable { Title = "RESULTS" };
            resultsPane.Content = CreatePaneContent(new Docking.Views.ResultsPane(), "tabResults");
            var resultsPaneHost = new LayoutAnchorablePane();
            resultsPaneHost.Children.Add(resultsPane);
            rootPanel.Children.Add(resultsPaneHost);
        }

        // ------------------------------------------------------------
        // Post-restore deduplication: JsonLayoutSerializer can leave ghost
        // copies of auto-hidden panes in unexpected containers. After all
        // restoration steps, walk the layout tree and remove any anchorable
        // whose Title appears more than once, keeping only the first instance.
        // ------------------------------------------------------------
        private void DeduplicateLayout()
        {
            if (dockManager.Layout?.RootPanel is not LayoutPanel rootPanel) return;
            
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var removed = 0;
            
            removed += RemoveDuplicatesInPanel(rootPanel, seen);
            removed += RemoveDuplicatesInSide(dockManager.Layout.LeftSide, seen);
            removed += RemoveDuplicatesInSide(dockManager.Layout.RightSide, seen);
            removed += RemoveDuplicatesInSide(dockManager.Layout.TopSide, seen);
            removed += RemoveDuplicatesInSide(dockManager.Layout.BottomSide, seen);
            
            removed += RemoveEmptyGroupsFromSide(dockManager.Layout.LeftSide);
            removed += RemoveEmptyGroupsFromSide(dockManager.Layout.RightSide);
            removed += RemoveEmptyGroupsFromSide(dockManager.Layout.TopSide);
            removed += RemoveEmptyGroupsFromSide(dockManager.Layout.BottomSide);
            
            if (removed > 0)
            {
                System.Diagnostics.Debug.WriteLine($"DeduplicateLayout removed {removed} duplicate anchorable(s) or empty group(s)");
            }
        }

        private static int RemoveEmptyGroupsFromSide(LayoutAnchorSide side)
        {
            if (side == null) return 0;
            
            int removed = 0;
            foreach (var group in side.Children.ToList())
            {
                if (group is LayoutAnchorGroup anchorGroup && anchorGroup.Children.Count == 0)
                {
                    side.Children.Remove(group);
                    removed++;
                }
            }
            return removed;
        }

        private static int RemoveDuplicatesInSide(LayoutAnchorSide side, HashSet<string> seen)
        {
            if (side == null) return 0;
            
            int removed = 0;
            
            foreach (var group in side.Children.ToList())
            {
                if (group is LayoutAnchorGroup anchorGroup)
                {
                    foreach (var anchorable in anchorGroup.Children.ToList())
                    {
                        if (!string.IsNullOrEmpty(anchorable.Title))
                        {
                            if (!seen.Add(anchorable.Title))
                            {
                                anchorGroup.Children.Remove(anchorable);
                                removed++;
                            }
                        }
                    }
                }
            }
            
            return removed;
        }

        private static int RemoveDuplicatesInPanel(LayoutPanel panel, HashSet<string> seen)
        {
            int removed = 0;
            
            foreach (var child in panel.Children.ToList())
            {
                switch (child)
                {
                    case LayoutAnchorablePane pane:
                        removed += RemoveDuplicatesInPane(pane, seen);
                        break;
                    case LayoutPanel childPanel:
                        removed += RemoveDuplicatesInPanel(childPanel, seen);
                        break;
                }
            }
            
            return removed;
        }

        private static int RemoveDuplicatesInPane(LayoutAnchorablePane pane, HashSet<string> seen)
        {
            int removed = 0;
            
            foreach (var anchorable in pane.Children.ToList())
            {
                if (!string.IsNullOrEmpty(anchorable.Title))
                {
                    if (!seen.Add(anchorable.Title))
                    {
                        pane.Children.Remove(anchorable);
                        removed++;
                    }
                }
            }
            
            return removed;
        }

        // ------------------------------------------------------------
        // JsonLayoutSerializer v5 BUG WORKAROUND:
        // The serializer does NOT deserialize LeftSide/RightSide/TopSide/BottomSide
        // (auto-hide panes). Worse: it can leave ghost copies of auto-hidden
        // panes inside RootPanel, so if we just add the real copies to the
        // sides we get duplicates. Strategy:
        //   1. Collect every pane title that the saved JSON placed on a side.
        //   2. Strip those anchorables out of the live layout tree so the
        //      serializer's ghost copies are gone.
        //   3. Rebuild the side groups from JSON and inject real UserControl
        //      content so the panes are visible and functional.
        // ------------------------------------------------------------
        private void RestoreAutoHidePanesFromJson(string json)
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                var root = doc.RootElement;
                
                var autoHideTitles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                CollectAutoHideTitles(root, "LeftSide", autoHideTitles);
                CollectAutoHideTitles(root, "RightSide", autoHideTitles);
                CollectAutoHideTitles(root, "TopSide", autoHideTitles);
                CollectAutoHideTitles(root, "BottomSide", autoHideTitles);

                if (dockManager.Layout?.RootPanel is LayoutPanel rootPanel && autoHideTitles.Count > 0)
                {
                    RemoveAnchorablesFromPanel(rootPanel, autoHideTitles);
                }

                RestoreSide(root, "LeftSide", dockManager.Layout.LeftSide);
                RestoreSide(root, "RightSide", dockManager.Layout.RightSide);
                RestoreSide(root, "TopSide", dockManager.Layout.TopSide);
                RestoreSide(root, "BottomSide", dockManager.Layout.BottomSide);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"RestoreAutoHidePanesFromJson failed: {ex.Message}");
            }
        }

        private void CollectAutoHideTitles(System.Text.Json.JsonElement root, string sideName, HashSet<string> titles)
        {
            if (!root.TryGetProperty(sideName, out var side) || !side.TryGetProperty("Children", out var children)) return;
            foreach (var group in children.EnumerateArray())
            {
                if (group.TryGetProperty("Children", out var anchorables))
                {
                    foreach (var a in anchorables.EnumerateArray())
                    {
                        if (a.TryGetProperty("Title", out var t))
                            titles.Add(t.GetString() ?? "");
                    }
                }
            }
        }

        private static void RemoveAnchorablesFromPanel(LayoutPanel rootPanel, HashSet<string> titlesToRemove)
        {
            foreach (var child in rootPanel.Children.ToList())
            {
                switch (child)
                {
                    case LayoutAnchorablePane pane:
                        RemoveFromPane(pane, titlesToRemove);
                        break;
                    case LayoutPanel panel:
                        RemoveAnchorablesFromPanel(panel, titlesToRemove);
                        break;
                }
            }
        }

        private static void RemoveFromPane(LayoutAnchorablePane pane, HashSet<string> titlesToRemove)
        {
            foreach (var anchorable in pane.Children.ToList())
            {
                if (titlesToRemove.Contains(anchorable.Title))
                {
                    pane.Children.Remove(anchorable);
                }
            }
        }

        private void RestoreSide(System.Text.Json.JsonElement root, string sideName, LayoutAnchorSide anchorSide)
        {
            try
            {
                if (anchorSide == null) return;
                if (!root.TryGetProperty(sideName, out var side)) return;
                if (!side.TryGetProperty("Children", out var children)) return;
                
                anchorSide.Children.Clear();
                
                foreach (var paneElement in children.EnumerateArray())
                {
                    if (!paneElement.TryGetProperty("Children", out var anchorableArray)) continue;
                    
                    var group = new LayoutAnchorGroup();
                    
                    foreach (var anchorableElement in anchorableArray.EnumerateArray())
                    {
                        var anchorable = new LayoutAnchorable();
                        
                        anchorable.Title = anchorableElement.TryGetProperty("Title", out var t) ? t.GetString() ?? "" : "";
                        anchorable.CanHide = anchorableElement.TryGetProperty("CanHide", out var ch) && ch.GetBoolean();
                        anchorable.CanAutoHide = anchorableElement.TryGetProperty("CanAutoHide", out var cah) && cah.GetBoolean();
                        anchorable.AutoHideWidth = anchorableElement.TryGetProperty("AutoHideWidth", out var ahw) ? ahw.GetDouble() : 0;
                        anchorable.AutoHideHeight = anchorableElement.TryGetProperty("AutoHideHeight", out var ahh) ? ahh.GetDouble() : 0;
                        anchorable.AutoHideMinWidth = anchorableElement.TryGetProperty("AutoHideMinWidth", out var ahmw) ? ahmw.GetDouble() : 100;
                        anchorable.AutoHideMinHeight = anchorableElement.TryGetProperty("AutoHideMinHeight", out var ahmh) ? ahmh.GetDouble() : 100;
                        anchorable.CanDockAsTabbedDocument = anchorableElement.TryGetProperty("CanDockAsTabbedDocument", out var cdatd) && cdatd.GetBoolean();
                        anchorable.CanMove = anchorableElement.TryGetProperty("CanMove", out var cm) && cm.GetBoolean();
                        anchorable.IsDetached = anchorableElement.TryGetProperty("IsDetached", out var idet) && idet.GetBoolean();
                        anchorable.CanClose = anchorableElement.TryGetProperty("CanClose", out var cc) && cc.GetBoolean();
                        anchorable.CanFloat = anchorableElement.TryGetProperty("CanFloat", out var cf) && cf.GetBoolean();
                        anchorable.CanShowOnHover = anchorableElement.TryGetProperty("CanShowOnHover", out var csh) && csh.GetBoolean();
                        
                        if (!string.IsNullOrEmpty(anchorable.Title))
                        {
                            anchorable.Content = anchorable.Title switch
                            {
                                "ELEMENTS" => CreatePaneContent(new Docking.Views.ElementsPane(), "tabElements"),
                                "SCRIPTS" => CreatePaneContent(new Docking.Views.ScriptsPane(), "tabScripts"),
                                "RESULTS" => CreatePaneContent(new Docking.Views.ResultsPane(), "tabResults"),
                                _ => anchorable.Content
                            };
                        }
                        
                        group.Children.Add(anchorable);
                    }
                    
                    if (group.Children.Count > 0)
                    {
                        anchorSide.Children.Add(group);
                    }
                }
                
                System.Diagnostics.Debug.WriteLine($"Restored {anchorSide.Children.Count} auto-hide group(s) to {sideName}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"RestoreSide({sideName}) failed: {ex.Message}");
            }
        }

        private static object CreatePaneContent(UserControl pane, string automationId)
        {
            var grid = new Grid();
            AutomationProperties.SetAutomationId(grid, automationId);
            grid.Children.Add(pane);
            return grid;
        }

        // ------------------------------------------------------------
        // Debug: log layout tree structure for comparison at different stages.
        // ------------------------------------------------------------
        private static void LogLayoutStructure(string stage, LayoutRoot layout)
        {
            try
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"=== LAYOUT STRUCTURE: {stage} ===");
                if (layout == null)
                {
                    sb.AppendLine("  LayoutRoot is NULL");
                }
                else
                {
                    LogElementRecursive(sb, layout, 0);
                }
                System.Diagnostics.Debug.WriteLine(sb.ToString());
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LogLayoutStructure error: {ex.Message}");
            }
        }

        private static void LogElementRecursive(System.Text.StringBuilder sb, ILayoutElement element, int indent)
        {
            if (element == null) return;
            
            var prefix = new string(' ', indent * 2);
            var typeName = element.GetType().Name;
            var title = "";
            
            if (element is LayoutAnchorable a) title = $" Title='{a.Title}'";
            else if (element is LayoutDocument d) title = $" Title='{d.Title}'";
            
            var content = element is LayoutAnchorable la && la.Content != null ? " [HAS CONTENT]" : 
                         element is LayoutDocument ld && ld.Content != null ? " [HAS CONTENT]" : "";
            
            sb.AppendLine($"{prefix}{typeName}{title}{content}");
            
            if (element is ILayoutContainer container)
            {
                foreach (var child in container.Children)
                {
                    LogElementRecursive(sb, child, indent + 1);
                }
            }
        }
    }
}
