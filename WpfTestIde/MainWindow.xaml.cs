using System.Windows;
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
    }
}
