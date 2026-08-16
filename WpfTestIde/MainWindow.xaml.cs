using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
    }
}
