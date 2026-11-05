using System.Windows;
using System.Windows.Controls;
using WpfTestIde.Models;
using WpfTestIde.ViewModels;

namespace WpfTestIde.Docking.Views
{
    public partial class WorkingFolderPane : UserControl
    {
        public WorkingFolderPane()
        {
            InitializeComponent();
        }

        private void FolderTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (e.NewValue is FolderTreeItem item && DataContext is MainViewModel vm)
            {
                vm.WorkingFolderSelectedItem = item;
            }
        }
    }
}
