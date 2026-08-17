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
            double tree = colTree?.ActualWidth > 0 ? colTree.ActualWidth : (colTree?.Width.Value ?? 0);
            double props = colProperties?.ActualWidth > 0 ? colProperties.ActualWidth : (colProperties?.Width.Value ?? 0);
            return (tree, props);
        }
    }
}