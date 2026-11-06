using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
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

        public string SearchBy { get => _searchBy; set { _searchBy = value ?? ""; OnPropertyChanged(); OnPropertyChanged(nameof(Summary)); } }
        public string Value { get => _value; set { _value = value ?? ""; OnPropertyChanged(); OnPropertyChanged(nameof(Summary)); } }
        public string? Scope { get => _scope; set { _scope = value; OnPropertyChanged(); OnPropertyChanged(nameof(Summary)); } }
        public string? ImagePath { get => _imagePath; set { _imagePath = value; OnPropertyChanged(); OnPropertyChanged(nameof(Summary)); } }
        public double? Similarity { get => _similarity; set { _similarity = value; OnPropertyChanged(); OnPropertyChanged(nameof(Summary)); } }

        public string Summary
        {
            get
            {
                var parts = new System.Collections.Generic.List<string>();
                if (!string.IsNullOrWhiteSpace(SearchBy) || !string.IsNullOrWhiteSpace(Value))
                    parts.Add($"{SearchBy}: {Value}");
                if (!string.IsNullOrWhiteSpace(Scope)) parts.Add($"scope={Scope}");
                if (!string.IsNullOrWhiteSpace(ImagePath)) parts.Add($"image={ImagePath}");
                if (Similarity.HasValue) parts.Add($"similarity={Similarity:0.##}");
                return parts.Count == 0 ? "(empty)" : string.Join(" · ", parts);
            }
        }

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
        private bool _isEditing;
        private System.Collections.ObjectModel.ObservableCollection<ElementStrategyItem> _items = new();

        public ElementStrategyGroup()
        {
            _items.CollectionChanged += Items_CollectionChanged;
        }

        public string Driver { get => _driver; set { _driver = value ?? ""; OnPropertyChanged(); OnPropertyChanged(nameof(HeaderText)); } }
        public bool IsEnabled { get => _isEnabled; set { _isEnabled = value; OnPropertyChanged(); OnPropertyChanged(nameof(HeaderText)); } }
        public bool IsEditing { get => _isEditing; set { _isEditing = value; OnPropertyChanged(); OnPropertyChanged(nameof(EditButtonText)); if (!value) OnPropertyChanged(nameof(Summary)); } }
        public string EditButtonText => IsEditing ? "Done" : "Edit";
        public string HeaderText => IsEnabled ? Driver : $"{Driver} (disabled)";

        public System.Collections.ObjectModel.ObservableCollection<ElementStrategyItem> Items
        {
            get => _items;
            set
            {
                if (_items != null) _items.CollectionChanged -= Items_CollectionChanged;
                _items = value ?? new System.Collections.ObjectModel.ObservableCollection<ElementStrategyItem>();
                _items.CollectionChanged += Items_CollectionChanged;
                OnPropertyChanged();
                OnPropertyChanged(nameof(Summary));
            }
        }

        public string Summary
        {
            get
            {
                if (Items == null || Items.Count == 0) return "No locators";
                return string.Join("\n", Items.Select((i, idx) => $"{idx + 1}. {i.Summary}"));
            }
        }

        private void Items_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
                foreach (ElementStrategyItem i in e.NewItems) i.PropertyChanged += Item_PropertyChanged;
            if (e.OldItems != null)
                foreach (ElementStrategyItem i in e.OldItems) i.PropertyChanged -= Item_PropertyChanged;
            OnPropertyChanged(nameof(Summary));
        }

        private void Item_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ElementStrategyItem.Summary) || string.IsNullOrEmpty(e.PropertyName))
                OnPropertyChanged(nameof(Summary));
        }

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
