using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace WpfTestIde.Models
{
    /// <summary>One locator strategy row (searchBy/value) for a driver group.</summary>
    public class ElementStrategyItem : INotifyPropertyChanged
    {
        private string _searchBy = "XPath";
        private string _value = "";
        private string? _scope;
        private string? _imagePath;
        private double? _similarity;

        public string SearchBy { get => _searchBy; set { _searchBy = value ?? ""; OnPropertyChanged(); } }
        public string Value { get => _value; set { _value = value ?? ""; OnPropertyChanged(); } }
        public string? Scope { get => _scope; set { _scope = value; OnPropertyChanged(); } }
        public string? ImagePath { get => _imagePath; set { _imagePath = value; OnPropertyChanged(); } }
        public double? Similarity { get => _similarity; set { _similarity = value; OnPropertyChanged(); } }

        public ElementStrategyItem Clone() => new()
        {
            SearchBy = SearchBy,
            Value = Value,
            Scope = Scope,
            ImagePath = ImagePath,
            Similarity = Similarity,
        };

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>A named driver strategy group (UIA3/WPFSpy/OculiX) containing locator rows.</summary>
    public class ElementStrategyGroup : INotifyPropertyChanged
    {
        private string _driver = "";
        private bool _isEnabled = true;
        public string Driver { get => _driver; set { _driver = value ?? ""; OnPropertyChanged(); } }
        public bool IsEnabled { get => _isEnabled; set { _isEnabled = value; OnPropertyChanged(); } }
        public System.Collections.ObjectModel.ObservableCollection<ElementStrategyItem> Items { get; set; } = new();

        public ElementStrategyGroup Clone()
        {
            var g = new ElementStrategyGroup { Driver = Driver, IsEnabled = IsEnabled };
            foreach (var i in Items) g.Items.Add(i.Clone());
            return g;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
