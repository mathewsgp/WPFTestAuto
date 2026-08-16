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
        // D1: when true, deep matches show their ancestor chain (VS Code "Filter includes parents").
        private bool _filterIncludesParents = true;
        // D1: total/visible element counters backing properties.
        private int _elementCount;
        private int _visibleElementCount;

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
            // D1: clears the search filter (the SearchFilter setter re-runs ApplyFilter).
            ClearSearchCommand = new RelayCommand(_ => SearchFilter = "");
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

        /// <summary>
        /// D1: when true, deep matches show their ancestor chain (VS Code
        /// "Filter includes parents"). When false, only leaf matches themselves
        /// are shown and ancestor folders are hidden unless they themselves
        /// match the filter.
        /// </summary>
        public bool FilterIncludesParents
        {
            get => _filterIncludesParents;
            set
            {
                _filterIncludesParents = value;
                OnPropertyChanged();
                ApplyFilter();
            }
        }

        /// <summary>D1: total number of leaf (non-folder) element nodes in the tree.</summary>
        public int ElementCount
        {
            get => _elementCount;
            private set { _elementCount = value; OnPropertyChanged(); OnPropertyChanged(nameof(ElementCountText)); }
        }

        /// <summary>D1: leaf element nodes that pass the current filter.</summary>
        public int VisibleElementCount
        {
            get => _visibleElementCount;
            private set { _visibleElementCount = value; OnPropertyChanged(); OnPropertyChanged(nameof(ElementCountText)); }
        }

        // A2: Element Tree collapsed state (pin/unpin). False = pinned (visible,
        // default). True = tree column shrinks to width 0 + GridSplitter hidden so
        // Properties reclaim the freed width. Push model (no floating overlay).
        // Two-way so the toolbar ToggleButton drives it and the layout reacts live.
        private bool _elementTreeCollapsed;
        public bool ElementTreeCollapsed
        {
            get => _elementTreeCollapsed;
            set { _elementTreeCollapsed = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// D1: single binding for the count chip. Renders "N elements" when no
        /// filter is active and "M / N elements" while filtering.
        /// </summary>
        public string ElementCountText =>
            string.IsNullOrWhiteSpace(_searchFilter)
                ? $"{ElementCount} elements"
                : $"{VisibleElementCount} / {ElementCount} elements";

        // Commands
        public ICommand AddFolderCommand { get; }
        public ICommand AddElementCommand { get; }
        public ICommand DeleteNodeCommand { get; }
        public ICommand EditNodeCommand { get; }
        public ICommand PreviewNodeCommand { get; }
        public ICommand ExpandAllCommand { get; }
        public ICommand CollapseAllCommand { get; }
        public ICommand RefreshCommand { get; }
        /// <summary>D1: clears the search box (re-runs ApplyFilter via the SearchFilter setter).</summary>
        public ICommand ClearSearchCommand { get; }

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

            // D1: rebuild element count after the tree is (re)loaded.
            RecountElements();
        }

        /// <summary>D1: count all leaf element nodes (not folders) in the tree.</summary>
        private void RecountElements()
        {
            int total = 0;
            foreach (var root in RootNodes)
                total += CountLeaves(root);
            ElementCount = total;
        }

        private static int CountLeaves(ElementTreeNode node)
        {
            if (!node.IsFolder && node.Element != null) return 1;
            int n = 0;
            foreach (var child in node.Children)
                n += CountLeaves(child);
            return n;
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
                // D1: when the filter is cleared, every leaf is visible.
                RecountVisible();
                return;
            }

            var filter = _searchFilter.ToLower();
            // D1: walk every root recursively, scoring leaf matches. ScoreNode
            // sets IsVisible only - it deliberately leaves IsExpanded alone so
            // the user's expand/collapse state is preserved (matching the
            // pre-D1 behavior on the checkbox-off path).
            foreach (var root in RootNodes)
                ScoreNode(root, filter);

            // D1: when "Filter includes parents" is on, reveal and expand every
            // ancestor folder whose subtree contains a matched leaf so deep
            // matches are actually visible in the UI - this is the VS Code
            // "Filter includes parents" pattern. When the checkbox is off this
            // pass is skipped entirely (folders stay in whatever expanded state
            // the user left them, exactly like the legacy behavior).
            if (_filterIncludesParents)
            {
                foreach (var root in RootNodes)
                    PropagateAncestors(root);
            }

            RecountVisible();
        }

        /// <summary>
        /// Recursively decide a node's IsVisible. A leaf matches if its name,
        /// alias (AutomationId) or reference Name contains the filter. A folder
        /// is visible iff any descendant is visible - folder-name-only matches
        /// are intentionally ignored: what users search for is elements, not
        /// synthetic group names. IsExpanded is never touched here.
        /// </summary>
        private bool ScoreNode(ElementTreeNode node, string filter)
        {
            bool leafMatch = !node.IsFolder && node.Element != null && (
                node.Name.ToLower().Contains(filter) ||
                (node.Element?.AutomationId?.ToLower().Contains(filter) ?? false) ||
                (node.Element?.Name?.ToLower().Contains(filter) ?? false));

            bool anyChildVisible = false;
            foreach (var child in node.Children)
                if (ScoreNode(child, filter)) anyChildVisible = true;

            bool visible = leafMatch || anyChildVisible;
            node.IsVisible = visible;

            return visible;
        }

        /// <summary>
        /// D1: post-pass (only when FilterIncludesParents is true) that reveals
        /// and expands every ancestor folder whose subtree contains at least
        /// one visible leaf, so deep matches are reachable in the UI. Folders
        /// with no visible leaves keep whatever state ScoreNode left them
        /// (collapsed/hidden).
        /// </summary>
        private bool PropagateAncestors(ElementTreeNode node)
        {
            if (!node.IsFolder)
                return node.IsVisible;

            bool anyVisibleDescendant = false;
            foreach (var child in node.Children)
                if (PropagateAncestors(child)) anyVisibleDescendant = true;

            if (anyVisibleDescendant)
            {
                node.IsVisible = true;
                node.IsExpanded = true;
            }
            return anyVisibleDescendant;
        }

        /// <summary>D1: recompute the visible-leaf count after a filter pass.</summary>
        private void RecountVisible()
        {
            int visible = 0;
            foreach (var root in RootNodes)
                visible += CountVisibleLeaves(root);
            VisibleElementCount = visible;
        }

        private int CountVisibleLeaves(ElementTreeNode node)
        {
            if (!node.IsFolder && node.Element != null)
                return node.IsVisible ? 1 : 0;
            int n = 0;
            foreach (var child in node.Children)
                n += CountVisibleLeaves(child);
            return n;
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
