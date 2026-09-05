using System;
using System.Windows;
using System.Windows.Controls;

namespace WpfTestIde.Docking.Views
{
    public partial class ElementsPane : UserControl
    {
        public ElementsPane()
        {
            InitializeComponent();
        }

        public void ApplySplitterState(double treeWidth, double propertiesWidth)
        {
            if (colTree != null)
                colTree.Width = new GridLength(treeWidth, GridUnitType.Pixel);
            if (colProperties != null)
                colProperties.Width = new GridLength(propertiesWidth, GridUnitType.Pixel);
        }

        public (double tree, double props) SnapshotSplitterState()
        {
            // ActualWidth is 0 when the pane was never rendered (e.g., app closed
            // before AvalonDock materialized the content). Fall back to the
            // persisted Width.Value, but clamp star factors to a sensible pixel
            // minimum so ApplySplitterState never collapses the columns.
            const double MinTreeWidth = 200;
            const double MinPropsWidth = 250;
            double tree = colTree?.ActualWidth > 0 ? colTree.ActualWidth : Math.Max(colTree?.Width.Value ?? 0, MinTreeWidth);
            double props = colProperties?.ActualWidth > 0 ? colProperties.ActualWidth : Math.Max(colProperties?.Width.Value ?? 0, MinPropsWidth);
            return (tree, props);
        }
    }
}