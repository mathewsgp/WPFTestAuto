using System;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
                    var serializer = new JsonLayoutSerializer(dockManager);
                    using var stream = new MemoryStream(Encoding.UTF8.GetBytes(state.DockLayoutJson));
                    serializer.Deserialize(stream);
                }
                catch { }
            }
            if (!string.IsNullOrWhiteSpace(state.ActivePaneId))
            {
                ShowPane(state.ActivePaneId);
            }
            else if (state.SelectedTabIndex >= 0 && state.SelectedTabIndex < 3)
            {
                ShowPane(state.SelectedTabIndex switch { 1 => "Scripts", 2 => "Results", _ => "Elements" });
            }

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
            if (WindowState == System.Windows.WindowState.Normal)
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

            // A1 splitter widths (pixels). The splitter now lives on the promoted
            // Elements pane (Docking/Views/ElementsPane.xaml), so its GridLength state
            // is restored there via ElementsPane.ApplySplitterState — which must be
            // resolved from the LayoutAnchorable's content after the DockingManager
            // materializes the pane. For Step 3 compilability the legacy MainWindow
            // colTree/colProperties refs are removed (the fields moved with the body).
            // TODO Step 4: invoke ElementsPane.ApplySplitterState(state.TreeColumnWidth,
            //      state.PropertiesColumnWidth) once pane content is reachable here.
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (DataContext is not MainViewModel vm) return;
            var state = new LayoutState
            {
                WindowState = (int)WindowState,
                Top = Top,
                Left = Left,
                Width = ActualWidth > 0 ? ActualWidth : Width,
                Height = ActualHeight > 0 ? ActualHeight : Height,
                Theme = Themes.ThemeManager.CurrentTheme,
                SelectedTabIndex = vm.SelectedTabIndex,
                // A1 splitter: state migrated to ElementsPane.xaml.cs (ApplySplitterState /
                // SnapshotSplitterState). MainWindow no longer owns colTree/colProperties.
                // Step 3: pane snapshot not yet reachable from MainWindow -> placeholder
                // zeros so star-column defaults apply on next load; Step 4 wires real
                // values from the LayoutAnchorable's pane content.
                TreeColumnWidth = 0,
                PropertiesColumnWidth = 0,
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
            // v5 traversal: LayoutRoot->RootPanel (LayoutPanel, implements
            // ILayoutContainer w/ Children) -> recurse Children for the first
            // LayoutAnchorable whose Title matches the requested pane id.
            // (v4's Descenents() extension does NOT exist in v5.)
            var anchorable = FindAnchorableByTitle(dockManager.Layout.RootPanel, paneId);
            anchorable?.Show();
        }

        private static LayoutAnchorable? FindAnchorableByTitle(LayoutPanel root, string title)
        {
            if (root == null) return null;
            foreach (var child in root.Children)
            {
                if (child is LayoutAnchorable anchorable && string.Equals(anchorable.Title, title, StringComparison.OrdinalIgnoreCase))
                    return anchorable;
                if (child is LayoutAnchorablePane pane && pane.Children.Count > 0)
                {
                    foreach (var grand in pane.Children)
                    {
                        if (grand is LayoutAnchorable match && string.Equals(match.Title, title, StringComparison.OrdinalIgnoreCase))
                            return match;
                    }
                }
            }
            return null;
        }

        private string SerializeLayout(DockingManager dm)
        {
            try
            {
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
    }
}
