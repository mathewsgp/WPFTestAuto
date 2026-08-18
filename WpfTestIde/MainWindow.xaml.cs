using System;
using System.IO;
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
                    
                    // JsonLayoutSerializer v5 BUG: does NOT deserialize LeftSide/RightSide/TopSide/BottomSide
                    // auto-hide panes. Manually restore them from the saved JSON.
                    RestoreAutoHidePanesFromJson(state.DockLayoutJson);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Dock layout deserialize failed: {ex.Message}");
                    // Fall back to the XAML-defined default layout (already set).
                }
            }
            
            // JsonLayoutSerializer cannot persist UIElement Content (UserControls),
            // so deserialized LayoutAnchorables arrive with null Content. Re-inject
            // the pane UserControls here so the three tabs are visible after restart.
            RestoreDockPaneContent();
            
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

            // Force a layout pass so the DockingManager processes auto-hide state
            // (LeftSide/RightSide panes) that JsonLayoutSerializer restored but
            // the visual tree hasn't realized yet.
            Dispatcher.BeginInvoke(new Action(() => 
            {
                dockManager.UpdateLayout();
                
                System.Diagnostics.Debug.WriteLine("=== LAYOUT RESTORE: AFTER UPDATELAYOUT ===");
                LogLayoutStructure("After UpdateLayout", dockManager.Layout);
            }),
                DispatcherPriority.Loaded);

            // Window geometry. Only apply if the persisted size is reasonable;
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

            // A1 splitter widths (pixels). The splitter now lives on the promoted
            // Elements pane (Docking/Views/ElementsPane.xaml), so its GridLength state
            // is restored there via ElementsPane.ApplySplitterState — which must be
            // resolved from the LayoutAnchorable's content after the DockingManager
            // materializes the pane.
            try
            {
                var elementsAnchorable = FindAnchorableByTitle(dockManager.Layout.RootPanel, "Elements");
                if (elementsAnchorable?.Content is FrameworkElement fe)
                {
                    var elementsPane = fe.FindName("ElementsPaneRoot") as Docking.Views.ElementsPane
                        ?? FindVisualChild<Docking.Views.ElementsPane>(fe);
                    elementsPane?.ApplySplitterState(state.TreeColumnWidth, state.PropertiesColumnWidth);
                }
            }
            catch { /* layout not yet materialized or pane not found — non-fatal */ }
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (DataContext is not MainViewModel vm) return;
            
            // Snapshot ElementsPane splitter state from the live pane content.
            double treeWidth = 0, propsWidth = 0;
            try
            {
                var elementsAnchorable = FindAnchorableByTitle(dockManager.Layout.RootPanel, "Elements");
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
            catch { /* non-fatal: persist zeros if pane not reachable */ }
            
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
            };
            LayoutPersistence.Save(state);
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
            catch
            {
                return null;
            }
        }

        // ------------------------------------------------------------
        // A6+E4 layout content restoration.
        // JsonLayoutSerializer cannot persist UIElement Content (UserControls),
        // so deserialized LayoutAnchorables arrive with null Content. Without
        // re-injection the three dock tabs are invisible after restart.
        // ------------------------------------------------------------
        private void RestoreDockPaneContent()
        {
            if (dockManager.Layout?.RootPanel is not LayoutPanel rootPanel) return;
            
            // Walk the entire layout tree and inject content into any
            // LayoutAnchorable whose Content is null after deserialization.
            // Do NOT clear/recreate the layout — the serializer may wrap
            // LayoutAnchorablePanes inside LayoutDocumentPanes or otherwise
            // reorganize the tree; we only fix missing Content.
            bool foundAnyAnchorable = false;
            RestoreDockPaneContentRecursive(rootPanel, ref foundAnyAnchorable);
            
            // If no anchorables were found at all (e.g., completely corrupted
            // layout), recreate the three expected panes from scratch.
            if (!foundAnyAnchorable)
            {
                RecreateDefaultDockPanes(rootPanel);
            }
        }

        private static void RestoreDockPaneContentRecursive(ILayoutElement element, ref bool foundAny)
        {
            if (element is LayoutAnchorable anchorable)
            {
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
        // JsonLayoutSerializer v5 BUG WORKAROUND:
        // The serializer does NOT deserialize LeftSide/RightSide/TopSide/BottomSide
        // (auto-hide panes). Parse the saved JSON and manually rebuild them.
        // ------------------------------------------------------------
        private void RestoreAutoHidePanesFromJson(string json)
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                var root = doc.RootElement;
                
                // Restore LeftSide (auto-hide on left edge)
                if (root.TryGetProperty("LeftSide", out var leftSide) && leftSide.TryGetProperty("Children", out var leftChildren))
                {
                    RestoreAnchorSide(leftChildren, dockManager.Layout.LeftSide, "Left");
                }
                
                // Restore RightSide
                if (root.TryGetProperty("RightSide", out var rightSide) && rightSide.TryGetProperty("Children", out var rightChildren))
                {
                    RestoreAnchorSide(rightChildren, dockManager.Layout.RightSide, "Right");
                }
                
                // Restore TopSide
                if (root.TryGetProperty("TopSide", out var topSide) && topSide.TryGetProperty("Children", out var topChildren))
                {
                    RestoreAnchorSide(topChildren, dockManager.Layout.TopSide, "Top");
                }
                
                // Restore BottomSide
                if (root.TryGetProperty("BottomSide", out var bottomSide) && bottomSide.TryGetProperty("Children", out var bottomChildren))
                {
                    RestoreAnchorSide(bottomChildren, dockManager.Layout.BottomSide, "Bottom");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"RestoreAutoHidePanesFromJson failed: {ex.Message}");
            }
        }

        private void RestoreAnchorSide(System.Text.Json.JsonElement childrenArray, LayoutAnchorSide anchorSide, string sideName)
        {
            try
            {
                if (anchorSide == null) return;
                
                anchorSide.Children.Clear();
                
                foreach (var paneElement in childrenArray.EnumerateArray())
                {
                    if (!paneElement.TryGetProperty("Children", out var anchorableArray)) continue;
                    
                    var group = new LayoutAnchorGroup();
                    
                    foreach (var anchorableElement in anchorableArray.EnumerateArray())
                    {
                        var anchorable = new LayoutAnchorable();
                        
                        // Copy all anchorable properties
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
                        
                        // Inject content for our known panes
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
                        
                        // Add anchorable directly to group (LayoutAnchorGroup.Children expects LayoutAnchorable)
                        group.Children.Add(anchorable);
                    }
                    
                    if (group.Children.Count > 0)
                    {
                        anchorSide.Children.Add(group);
                    }
                }
                
                System.Diagnostics.Debug.WriteLine($"Restored {anchorSide.Children.Count} auto-hide group(s) to {sideName}Side");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"RestoreAnchorSide({sideName}) failed: {ex.Message}");
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
