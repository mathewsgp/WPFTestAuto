using System;
using System.Windows;
using System.Windows.Controls;
using WpfTestIde.ViewModels;

namespace WpfTestIde.Views
{
    public partial class ElementEditorView : UserControl
    {
        public ElementEditorView()
        {
            try
            {
                InitializeComponent();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"ElementEditorView init error: {ex.GetType().Name}: {ex.Message}", "Element Editor Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            // Fallback: if XAML DataContext binding failed, try to get it from the window
            if (DataContext == null)
            {
                try
                {
                    var window = Window.GetWindow(this);
                    if (window?.DataContext is MainViewModel vm)
                    {
                        DataContext = vm;
                    }
                }
                catch { }
            }
        }
    }
}
