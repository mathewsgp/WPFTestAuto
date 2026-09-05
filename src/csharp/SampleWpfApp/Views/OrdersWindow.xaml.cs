using System.Collections.ObjectModel;
using System.Windows;

namespace SampleWpfApp.Views
{
    public partial class OrdersWindow : Window
    {
        public class OrderRow
        {
            public string Sku { get; set; } = "";
            public string Qty { get; set; } = "";
        }

        private readonly ObservableCollection<OrderRow> _orders = new();

        public OrdersWindow()
        {
            InitializeComponent();
            gridOrders.ItemsSource = _orders;
        }

        private void BtnCreateOrder_Click(object sender, RoutedEventArgs e)
        {
            string sku = cmbSku.Text;
            string qty = txtQty.Text;

            if (string.IsNullOrWhiteSpace(sku))
            {
                lblConfirmation.Content = "Please select a SKU";
                lblConfirmation.Visibility = Visibility.Visible;
                return;
            }

            _orders.Add(new OrderRow { Sku = sku, Qty = qty });
            lblConfirmation.Content = $"Order confirmed: {sku} x{qty}";
            lblConfirmation.Visibility = Visibility.Visible;
        }

        private void BtnLogout_Click(object sender, RoutedEventArgs e)
        {
            // Simulate logout: return to the login page by recreating MainWindow
            var loginWindow = new MainWindow();
            loginWindow.Show();
            Application.Current.MainWindow = loginWindow;
            Close();
        }
    }
}
