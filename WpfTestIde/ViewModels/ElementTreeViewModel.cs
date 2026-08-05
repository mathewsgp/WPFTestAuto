using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using WpfTestIde.Models;

namespace WpfTestIde.ViewModels
{
    /// <summary>
    /// Represents a node in the element tree hierarchy.
    /// Can be either a folder (group) or a leaf element.
    /// </summary>
    public class ElementTreeNode : INotifyPropertyChanged
    {
        private string _name = "";
        private string _icon = "📁";
        private bool _isExpanded = true;
        private bool _isSelected;
        private bool _isVisible = true;
        private string _controlType = "";

        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayText)); }
        }

        public string Icon
        {
            get => _icon;
            set { _icon = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayText)); }
        }

        public string ControlType
        {
            get => _controlType;
            set { _controlType = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayText)); }
        }

        public bool IsExpanded
        {
            get => _isExpanded;
            set { _isExpanded = value; OnPropertyChanged(); }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }

        public bool IsVisible
        {
            get => _isVisible;
            set { _isVisible = value; OnPropertyChanged(); }
        }

        public bool IsFolder => Children.Count > 0;
        public ElementEntry? Element { get; set; }
        public ElementTreeNode? Parent { get; set; }
        public ObservableCollection<ElementTreeNode> Children { get; set; } = new();

        public string DisplayText => IsFolder ? $"{Icon} {Name}" : $"{Icon} {Name}";

        public string FullPath
        {
            get
            {
                if (Parent == null || Parent.Parent == null)
                    return Name;
                return $"{Parent.FullPath}.{Name}";
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        /// <summary>
        /// Get all element nodes (not folders) in this subtree.
        /// </summary>
        public IEnumerable<ElementTreeNode> GetAllElements()
        {
            if (!IsFolder && Element != null)
                yield return this;
            foreach (var child in Children)
                foreach (var node in child.GetAllElements())
                    yield return node;
        }
    }

    /// <summary>
    /// ViewModel for the Element Tree View panel.
    /// Organizes elements hierarchically by alias prefix.
    /// </summary>
    public class ElementTreeViewModel : INotifyPropertyChanged
    {
        private ObservableCollection<ElementTreeNode> _rootNodes = new();
        private ElementTreeNode? _selectedNode;
        private string _searchFilter = "";
        private ElementEntry? _selectedElement;

        public ElementTreeViewModel()
        {
            // Initialize commands
            AddFolderCommand = new RelayCommand(_ => AddFolder());
            AddElementCommand = new RelayCommand(_ => AddElement(), _ => SelectedNode != null);
            DeleteNodeCommand = new RelayCommand(_ => DeleteSelected(), _ => SelectedNode != null && !SelectedNode.IsFolder);
            EditNodeCommand = new RelayCommand(_ => EditSelected(), _ => SelectedNode != null && !SelectedNode.IsFolder);
            PreviewNodeCommand = new RelayCommand(_ => PreviewSelected(), _ => SelectedNode != null && !SelectedNode.IsFolder);
            ExpandAllCommand = new RelayCommand(_ => ExpandAll());
            CollapseAllCommand = new RelayCommand(_ => CollapseAll());
            RefreshCommand = new RelayCommand(_ => RefreshTree());
        }

        public ObservableCollection<ElementTreeNode> RootNodes
        {
            get => _rootNodes;
            set { _rootNodes = value; OnPropertyChanged(); }
        }

        public ElementTreeNode? SelectedNode
        {
            get => _selectedNode;
            set
            {
                if (_selectedNode != null) _selectedNode.IsSelected = false;
                _selectedNode = value;
                if (_selectedNode != null)
                {
                    _selectedNode.IsSelected = true;
                    _selectedElement = _selectedNode.Element;
                }
                else
                {
                    _selectedElement = null;
                }
                OnPropertyChanged();
                OnPropertyChanged(nameof(SelectedElement));
            }
        }

        public ElementEntry? SelectedElement
        {
            get => _selectedElement;
            set { _selectedElement = value; OnPropertyChanged(); }
        }

        public string SearchFilter
        {
            get => _searchFilter;
            set
            {
                _searchFilter = value;
                OnPropertyChanged();
                ApplyFilter();
            }
        }

        // Commands
        public ICommand AddFolderCommand { get; }
        public ICommand AddElementCommand { get; }
        public ICommand DeleteNodeCommand { get; }
        public ICommand EditNodeCommand { get; }
        public ICommand PreviewNodeCommand { get; }
        public ICommand ExpandAllCommand { get; }
        public ICommand CollapseAllCommand { get; }
        public ICommand RefreshCommand { get; }

        /// <summary>
        /// Loads elements from an ObservableCollection and builds the tree hierarchy.
        /// </summary>
        public void LoadFromElements(ObservableCollection<ElementEntry> elements)
        {
            RootNodes.Clear();

            // Group elements by their prefix (e.g., "LoginPage" from "LoginPage.btnSubmit")
            var grouped = elements
                .Select(e => new { Element = e, Prefix = GetPrefix(e.Alias), Suffix = GetSuffix(e.Alias) })
                .GroupBy(x => x.Prefix)
                .OrderBy(g => g.Key);

            foreach (var group in grouped)
            {
                var groupNode = new ElementTreeNode
                {
                    Name = group.Key,
                    Icon = GetFolderIcon(group.Key),
                    IsExpanded = true
                };

                foreach (var item in group.OrderBy(x => x.Suffix))
                {
                    var elementNode = new ElementTreeNode
                    {
                        Name = item.Suffix,
                        Icon = GetControlIcon(item.Element.ControlType),
                        ControlType = item.Element.ControlType,
                        Element = item.Element,
                        Parent = groupNode
                    };
                    groupNode.Children.Add(elementNode);
                }

                RootNodes.Add(groupNode);
            }

            // Add "Ungrouped" for elements without dots
            var ungrouped = elements.Where(e => !e.Alias.Contains('.'));
            if (ungrouped.Any())
            {
                var ungroupedNode = new ElementTreeNode
                {
                    Name = "Other",
                    Icon = "📦",
                    IsExpanded = true
                };

                foreach (var element in ungrouped)
                {
                    var elementNode = new ElementTreeNode
                    {
                        Name = element.Alias,
                        Icon = GetControlIcon(element.ControlType),
                        ControlType = element.ControlType,
                        Element = element,
                        Parent = ungroupedNode
                    };
                    ungroupedNode.Children.Add(elementNode);
                }

                RootNodes.Add(ungroupedNode);
            }
        }

        private string GetPrefix(string alias)
        {
            var dotIndex = alias.LastIndexOf('.');
            return dotIndex > 0 ? alias.Substring(0, dotIndex) : "Other";
        }

        private string GetSuffix(string alias)
        {
            var dotIndex = alias.LastIndexOf('.');
            return dotIndex > 0 ? alias.Substring(dotIndex + 1) : alias;
        }

        private string GetControlIcon(string controlType)
        {
            return controlType.ToLower() switch
            {
                "button" => "🔘",
                "textbox" => "📝",
                "textblock" => "📄",
                "checkbox" => "☑️",
                "radiobutton" => "⭕",
                "combobox" or "dropdown" => "🔽",
                "listbox" => "📋",
                "datagrid" or "grid" => "📊",
                "menu" => "🍔",
                "menuitem" => "📌",
                "tabcontrol" => "📑",
                "tabitem" => "📰",
                "label" => "🏷️",
                "image" => "🖼️",
                "progressbar" => "📈",
                "slider" => "🎚️",
                "treeview" => "🌳",
                "treeviewitem" => "🌲",
                "window" => "🪟",
                "panel" or "grid" or "stackpanel" => "⬜",
                "link" or "hyperlink" => "🔗",
                _ => "🎯"
            };
        }

        private string GetFolderIcon(string name)
        {
            return name.ToLower() switch
            {
                "loginpage" or "login" => "🔐",
                "mainwindow" or "main" => "🏠",
                "dialog" or "dialogs" => "💬",
                "settings" or "preferences" => "⚙️",
                "checkout" => "🛒",
                "admin" => "👨‍💼",
                "report" or "reports" => "📊",
                "user" or "users" => "👤",
                _ => "📁"
            };
        }

        private void ApplyFilter()
        {
            if (string.IsNullOrWhiteSpace(_searchFilter))
            {
                foreach (var root in RootNodes)
                    SetNodeVisibility(root, true);
                return;
            }

            var filter = _searchFilter.ToLower();
            foreach (var root in RootNodes)
            {
                bool anyVisible = false;
                foreach (var node in root.Children)
                {
                    bool visible = node.Name.ToLower().Contains(filter) ||
                                   (node.Element?.AutomationId?.ToLower().Contains(filter) ?? false) ||
                                   (node.Element?.Name?.ToLower().Contains(filter) ?? false);
                    node.IsVisible = visible;
                    if (visible) anyVisible = true;
                }
                root.IsVisible = anyVisible;
            }
        }

        private void SetNodeVisibility(ElementTreeNode node, bool visible)
        {
            node.IsVisible = visible;
            foreach (var child in node.Children)
                SetNodeVisibility(child, visible);
        }

        private void ExpandAll()
        {
            foreach (var root in RootNodes)
                SetExpanded(root, true);
        }

        private void CollapseAll()
        {
            foreach (var root in RootNodes)
                SetExpanded(root, false);
        }

        private void SetExpanded(ElementTreeNode node, bool expanded)
        {
            node.IsExpanded = expanded;
            foreach (var child in node.Children)
                SetExpanded(child, expanded);
        }

        private void AddFolder()
        {
            var folderName = $"NewGroup_{RootNodes.Count + 1}";
            var newFolder = new ElementTreeNode
            {
                Name = folderName,
                Icon = "📁",
                IsExpanded = true
            };
            RootNodes.Add(newFolder);
            SelectedNode = newFolder;
        }

        private void AddElement()
        {
            if (SelectedNode == null) return;

            var parent = SelectedNode.IsFolder ? SelectedNode : SelectedNode.Parent;
            if (parent == null) parent = RootNodes.LastOrDefault();
            if (parent == null) return;

            var elementName = $"NewElement_{parent.Children.Count + 1}";
            var newElement = new ElementEntry
            {
                Alias = $"{parent.Name}.{elementName}",
                DisplayName = elementName,
                ControlType = "Button"
            };

            var newNode = new ElementTreeNode
            {
                Name = elementName,
                Icon = GetControlIcon("Button"),
                ControlType = "Button",
                Element = newElement,
                Parent = parent
            };

            parent.Children.Add(newNode);
            SelectedNode = newNode;
        }

        private void DeleteSelected()
        {
            if (SelectedNode == null || SelectedNode.IsFolder) return;

            var parent = SelectedNode.Parent;
            if (parent != null)
            {
                parent.Children.Remove(SelectedNode);
                SelectedNode = parent.Children.FirstOrDefault();
            }
        }

        private void EditSelected()
        {
            // This would typically open an edit dialog
            // For now, we just select the element for editing in the main view
            if (SelectedNode?.Element != null)
            {
                SelectedElement = SelectedNode.Element;
            }
        }

        private void PreviewSelected()
        {
            // This would highlight the element in the running application
            if (SelectedNode?.Element != null)
            {
                SelectedElement = SelectedNode.Element;
            }
        }

        private void RefreshTree()
        {
            // Trigger a refresh - the actual reload should be called from outside
            OnPropertyChanged(nameof(RootNodes));
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
