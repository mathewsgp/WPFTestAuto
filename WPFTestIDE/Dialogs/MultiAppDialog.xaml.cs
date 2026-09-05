using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using WpfTestIde.Models;
using WpfTestIde.ViewModels;

namespace WpfTestIde.Dialogs
{
    public partial class MultiAppDialog : Window
    {
        private readonly MainViewModel _viewModel;

        public MultiAppDialog(MainViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            AppsGrid.ItemsSource = _viewModel.AttachedApps;
        }

        private void SetDefault_Click(object sender, RoutedEventArgs e)
        {
            if (AppsGrid.SelectedItem is WpfTestIde.Models.AppContext selectedApp)
            {
                _viewModel.SelectedApp = selectedApp;
                _viewModel.SetDefaultApplication(selectedApp.AppId);
                MessageBox.Show($"Set '{selectedApp.AppId}' as default application.", "Default App", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show("Select an application first.", "Set Default", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void Detach_Click(object sender, RoutedEventArgs e)
        {
            if (AppsGrid.SelectedItem is WpfTestIde.Models.AppContext selectedApp)
            {
                var result = MessageBox.Show(
                    $"Detach from '{selectedApp.AppId}' (PID {selectedApp.ProcessId})?",
                    "Detach Application",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    _viewModel.DetachApplication(selectedApp.AppId);
                    AppsGrid.ItemsSource = null;
                    AppsGrid.ItemsSource = _viewModel.AttachedApps;
                }
            }
            else
            {
                MessageBox.Show("Select an application first.", "Detach", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }
    }
}
