using System.Windows;
using SampleWpfApp.Views;

namespace SampleWpfApp
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void BtnSubmit_Click(object sender, RoutedEventArgs e)
        {
            if (txtUsername.Text == "user1" && txtPassword.Password == "Pass@123")
            {
                var orders = new OrdersWindow();
                orders.Show();
                Close();
            }
            else
            {
                lblError.Content = "Invalid username or password";
                lblError.Visibility = Visibility.Visible;
            }
        }
    }
}
