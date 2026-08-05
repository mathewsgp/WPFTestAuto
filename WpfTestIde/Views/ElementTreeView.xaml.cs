using System.Windows;
using System.Windows.Controls;
using WpfTestIde.Models;
using WpfTestIde.ViewModels;

namespace WpfTestIde.Views
{
    public partial class ElementTreeView : UserControl
    {
        public ElementTreeView()
        {
            InitializeComponent();
        }

        private void ElementTreeViewControl_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (DataContext is ElementTreeViewModel vm && e.NewValue is ElementTreeNode node)
            {
                vm.SelectedNode = node;
            }
        }

        private void CopyAlias_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is ElementTreeViewModel vm && vm.SelectedNode?.Element != null)
            {
                Clipboard.SetText(vm.SelectedNode.Element.Alias);
            }
        }

        private void CopyXPath_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is ElementTreeViewModel vm && vm.SelectedNode?.Element != null)
            {
                var xpath = vm.SelectedNode.Element.XPath ?? "";
                if (!string.IsNullOrEmpty(xpath))
                {
                    Clipboard.SetText(xpath);
                }
            }
        }
    }
}
