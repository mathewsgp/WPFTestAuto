using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Automation;
using WpfTestIde.Recording;
using WpfTestIde.Models;

namespace WpfTestIde.Dialogs
{
    public partial class SpyToolDialog : Window
    {
        private string _pipeName;
        private SpyAgentClient? _client;
        private TreeViewItem? _selectedTreeItem;
        private ElementTreeNode? _selectedElement;
        private readonly List<ElementTreeNode> _allNodes = new();
        private int _targetProcessId;
        private string? _appId;
        private readonly List<WpfTestIde.Models.AppContext> _attachedApps;

        public string? SelectedAlias { get; private set; }
        public string? SelectedXPath { get; private set; }
        public Dictionary<string, string> SelectedProperties { get; private set; } = new();
        public List<string> SelectedRecordingModes { get; private set; }
        public string SelectedMode { get; private set; } = "WPFSpy";

        public SpyToolDialog(string pipeName = "WPFSpyAgentPipe", List<string>? recordingModes = null, string selectedMode = "WPFSpy", int targetProcessId = 0, string? appId = null, List<WpfTestIde.Models.AppContext>? attachedApps = null)
        {
            InitializeComponent();
            _pipeName = pipeName;
            _targetProcessId = targetProcessId;
            _appId = appId;
            _attachedApps = attachedApps ?? new List<WpfTestIde.Models.AppContext>();
            SelectedRecordingModes = recordingModes ?? new List<string> { "FlaUI", "WPFSpy" };
            SelectedMode = selectedMode;
            
            // Populate app selector
            PopulateAppSelector(appId);
            
            // Set mode selector
            foreach (ComboBoxItem item in ModeSelector.Items)
            {
                if (item.Tag as string == selectedMode)
                {
                    ModeSelector.SelectedItem = item;
                    break;
                }
            }
            
            // Attach event handler after initialization to avoid firing during XAML load
            ModeSelector.SelectionChanged += ModeSelector_SelectionChanged;
            AppSelector.SelectionChanged += AppSelector_SelectionChanged;
            
            BuildPropertyGrid();
        }
        
        private void PopulateAppSelector(string? selectedAppId)
        {
            AppSelector.Items.Clear();
            
            // Add all attached apps
            if (_attachedApps.Count > 0)
            {
                foreach (var app in _attachedApps)
                {
                    var item = new ComboBoxItem
                    {
                        Content = app.DisplayText,
                        Tag = app.AppId
                    };
                    AppSelector.Items.Add(item);
                    
                    if (app.AppId == selectedAppId || (string.IsNullOrEmpty(selectedAppId) && app.IsDefault))
                    {
                        item.IsSelected = true;
                    }
                }
            }
            else
            {
                // Fallback: show current app context
                var currentApp = new ComboBoxItem
                {
                    Content = $"(Current) PID {_targetProcessId}",
                    Tag = selectedAppId ?? ""
                };
                AppSelector.Items.Add(currentApp);
                if (string.IsNullOrEmpty(selectedAppId))
                {
                    currentApp.IsSelected = true;
                }
            }
        }
        
        private void AppSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (AppSelector.SelectedItem is ComboBoxItem item && item.Tag is string appId)
            {
                _appId = string.IsNullOrEmpty(appId) ? null : appId;
                
                // Find the selected app context and update pipe/process
                var selectedApp = _attachedApps.FirstOrDefault(a => a.AppId == _appId);
                if (selectedApp != null)
                {
                    _pipeName = selectedApp.PipeName;
                    _targetProcessId = selectedApp.ProcessId;
                }
                
                // Reload tree for the selected app
                LoadElementTree();
            }
        }

        private void BuildPropertyGrid()
        {
            PropertyGrid.RowDefinitions.Clear();
            PropertyGrid.Children.Clear();

            var properties = new[]
            {
                ("AutomationId", "AutomationId"),
                ("Name", "Name"),
                ("ControlType", "ControlType"),
                ("ClassName", "ClassName"),
                ("Text", "Text"),
                ("XPath", "XPath"),
                ("IsEnabled", "IsEnabled"),
                ("IsVisible", "IsVisible"),
                ("Bounds", "BoundingRectangle")
            };

            for (int i = 0; i < properties.Length; i++)
            {
                PropertyGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                var label = new TextBlock
                {
                    Text = properties[i].Item1 + ":",
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(8, 4, 8, 4),
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetRow(label, i);
                Grid.SetColumn(label, 0);
                PropertyGrid.Children.Add(label);

                var valueBox = new TextBox
                {
                    Name = "Prop_" + properties[i].Item2,
                    IsReadOnly = true,
                    Margin = new Thickness(0, 2, 8, 2),
                    Padding = new Thickness(4),
                    FontFamily = new FontFamily("Consolas"),
                    BorderThickness = new Thickness(0)
                };
                Grid.SetRow(valueBox, i);
                Grid.SetColumn(valueBox, 1);
                PropertyGrid.Children.Add(valueBox);

                var copyBtn = new Button
                {
                    Content = "📋",
                    Width = 30,
                    Height = 24,
                    Margin = new Thickness(0, 2, 8, 2),
                    Tag = properties[i].Item2,
                    ToolTip = "Copy to clipboard"
                };
                copyBtn.Click += CopyProperty_Click;
                Grid.SetRow(copyBtn, i);
                Grid.SetColumn(copyBtn, 2);
                PropertyGrid.Children.Add(copyBtn);
            }
        }

        private void RefreshTree_Click(object sender, RoutedEventArgs e)
        {
            LoadElementTree();
        }

        private void ModeSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ModeSelector.SelectedItem is ComboBoxItem selectedItem && selectedItem.Tag is string mode)
            {
                SelectedMode = mode;
                try
                {
                    LoadElementTree();
                }
                catch (Exception ex)
                {
                    XPathStatus.Text = $"Error loading tree: {ex.Message}";
                    XPathStatus.Foreground = Brushes.Red;
                }
            }
        }

        private void LoadElementTree()
        {
            try
            {
                if (SelectedMode == "FlaUI")
                {
                    LoadFlaUITree();
                }
                else
                {
                    LoadWPFSpyTree();
                }
            }
            catch (Exception ex)
            {
                XPathStatus.Text = $"Error: {ex.Message}";
                XPathStatus.Foreground = Brushes.Red;
            }
        }

        private void LoadWPFSpyTree()
        {
            _client = new SpyAgentClient(_pipeName);

            var response = _client.Send("GetFullTree");

            ElementTree.Items.Clear();
            _allNodes.Clear();

            if (response.Success && !string.IsNullOrEmpty(response.Data))
            {
                var treeData = System.Text.Json.JsonSerializer.Deserialize<ElementTreeData>(
                    response.Data,
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                );

                if (treeData?.Nodes != null)
                {
                    foreach (var node in treeData.Nodes)
                    {
                        RebuildParentRefs(node, null);
                        var treeItem = CreateTreeItem(node);
                        ElementTree.Items.Add(treeItem);
                        _allNodes.Add(node);
                    }
                }
            }
            else
            {
                var rootNode = new ElementTreeNode
                {
                    Name = "Root (No App Connected)",
                    ControlType = "Window",
                    AutomationId = null
                };
                var item = CreateTreeItem(rootNode);
                ElementTree.Items.Add(item);
            }

            XPathStatus.Text = $"Loaded {_allNodes.Count} elements (WPFSpy Visual Tree)";
            XPathStatus.Foreground = Brushes.Green;
        }

        private void LoadFlaUITree()
        {
            ElementTree.Items.Clear();
            _allNodes.Clear();

            try
            {
                System.Windows.Automation.AutomationElement? rootElement = null;

                if (_targetProcessId > 0)
                {
                    // Find the target app window by process ID
                    rootElement = FindAppWindowByPid(_targetProcessId);
                }

                if (rootElement == null)
                {
                    // Fallback: use the focused element's root window, or RootElement
                    try
                    {
                        var focused = System.Windows.Automation.AutomationElement.FocusedElement;
                        if (focused != null)
                        {
                            rootElement = focused;
                            while (rootElement.Current.NativeWindowHandle == IntPtr.Zero && rootElement != System.Windows.Automation.AutomationElement.RootElement)
                            {
                                var parent = System.Windows.Automation.TreeWalker.RawViewWalker.GetParent(rootElement);
                                if (parent == null || parent == System.Windows.Automation.AutomationElement.RootElement)
                                    break;
                                rootElement = parent;
                            }
                        }
                    }
                    catch { }

                    if (rootElement == null)
                    {
                        rootElement = System.Windows.Automation.AutomationElement.RootElement;
                    }
                }

                var nodes = BuildTreeFromAutomationElement(rootElement, null);
                foreach (var node in nodes)
                {
                    var treeItem = CreateTreeItem(node);
                    ElementTree.Items.Add(treeItem);
                    _allNodes.Add(node);
                }

                XPathStatus.Text = $"Loaded {_allNodes.Count} elements (FlaUI Automation Tree)";
                XPathStatus.Foreground = Brushes.Green;
            }
            catch (Exception ex)
            {
                XPathStatus.Text = $"FlaUI Error: {ex.Message}";
                XPathStatus.Foreground = Brushes.Red;
            }
        }

        private System.Windows.Automation.AutomationElement? FindAppWindowByPid(int pid)
        {
            try
            {
                var condition = new System.Windows.Automation.PropertyCondition(
                    System.Windows.Automation.AutomationElement.ControlTypeProperty,
                    System.Windows.Automation.ControlType.Window);
                
                var windows = System.Windows.Automation.AutomationElement.RootElement.FindAll(
                    System.Windows.Automation.TreeScope.Children, condition);
                
                foreach (System.Windows.Automation.AutomationElement window in windows)
                {
                    try
                    {
                        if (window.Current.ProcessId == pid)
                        {
                            return window;
                        }
                    }
                    catch { }
                }
            }
            catch { }
            return null;
        }

        private List<ElementTreeNode> BuildTreeFromAutomationElement(System.Windows.Automation.AutomationElement element, ElementTreeNode? parent)
        {
            var nodes = new List<ElementTreeNode>();
            
            try
            {
                var node = new ElementTreeNode
                {
                    Name = element.Current.Name,
                    ControlType = element.Current.ControlType.ProgrammaticName?.Replace("ControlType.", ""),
                    AutomationId = element.Current.AutomationId,
                    ClassName = element.Current.ClassName,
                    Text = element.Current.Name,
                    IsEnabled = element.Current.IsEnabled,
                    IsVisible = element.Current.IsOffscreen == false,
                    Parent = parent,
                    Children = new List<ElementTreeNode>(),
                    Bounds = new Rect(element.Current.BoundingRectangle.X, element.Current.BoundingRectangle.Y, 
                                     element.Current.BoundingRectangle.Width, element.Current.BoundingRectangle.Height)
                };

                // Build XPath from parent chain
                if (parent != null)
                {
                    var segment = $"*[@AutomationId='{node.AutomationId}']";
                    if (string.IsNullOrEmpty(node.AutomationId))
                    {
                        segment = $"*[@Name='{node.Name}']";
                    }
                    if (string.IsNullOrEmpty(node.Name))
                    {
                        segment = node.ControlType ?? "*";
                    }
                    node.XPath = $"{parent.XPath}/{segment}";
                    parent.Children!.Add(node);
                }
                else
                {
                    node.XPath = $"/{node.ControlType ?? "*"}[@AutomationId='{node.AutomationId}']";
                    if (string.IsNullOrEmpty(node.AutomationId))
                    {
                        node.XPath = $"/{node.ControlType ?? "*"}[@Name='{node.Name}']";
                    }
                    nodes.Add(node);
                }

                // Get children
                try
                {
                    var condition = new System.Windows.Automation.PropertyCondition(System.Windows.Automation.AutomationElement.IsControlElementProperty, true);
                    var children = element.FindAll(System.Windows.Automation.TreeScope.Children, condition);
                    
                    foreach (System.Windows.Automation.AutomationElement child in children)
                    {
                        BuildTreeFromAutomationElement(child, node);
                    }
                }
                catch { }
            }
            catch { }

            return nodes;
        }

        private static void RebuildParentRefs(ElementTreeNode node, ElementTreeNode? parent)
        {
            node.Parent = parent;
            if (node.Children != null)
            {
                foreach (var child in node.Children)
                {
                    RebuildParentRefs(child, node);
                }
            }
        }

        private TreeViewItem CreateTreeItem(ElementTreeNode node)
        {
            var item = new TreeViewItem
            {
                Header = node.DisplayName,
                Tag = node,
                DataContext = node
            };

            // Set icon based on control type
            string icon = node.ControlType?.ToLower() switch
            {
                "button" => "🔘",
                "textbox" => "📝",
                "checkbox" => "☑️",
                "combobox" or "dropdown" => "�-dropdown",
                "listbox" => "📋",
                "datagrid" => "🗂️",
                "menu" => "📜",
                "menuitem" => "📄",
                "window" => "🪟",
                "tab" => "📇",
                "tabitem" => "📄",
                "image" => "🖼️",
                "link" or "hyperlink" => "🔗",
                "radiobutton" => "⭕",
                "slider" => "🎚️",
                "progressbar" => "📊",
                "treeview" or "treeitem" => "🌳",
                _ => "⬜"
            };

            // Update display with icon
            var stackPanel = new StackPanel { Orientation = Orientation.Horizontal };
            stackPanel.Children.Add(new TextBlock { Text = icon, Margin = new Thickness(0, 0, 4, 0) });
            stackPanel.Children.Add(new TextBlock { Text = node.DisplayName });
            item.Header = stackPanel;

            // Recursively add children
            if (node.Children != null)
            {
                foreach (var child in node.Children)
                {
                    item.Items.Add(CreateTreeItem(child));
                    _allNodes.Add(child);
                }
            }

            return item;
        }

        private void ElementTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (e.NewValue is TreeViewItem item && item.Tag is ElementTreeNode node)
            {
                _selectedElement = node;
                UpdatePropertyGrid(node);
                UpdateXPath(node);
            }
        }

        private void UpdatePropertyGrid(ElementTreeNode node)
        {
            SetProperty("AutomationId", node.AutomationId ?? "(none)");
            SetProperty("Name", node.Name ?? "(none)");
            SetProperty("ControlType", node.ControlType ?? "(none)");
            SetProperty("ClassName", node.ClassName ?? "(none)");
            SetProperty("Text", TruncateText(node.Text, 200));
            SetProperty("XPath", node.XPath ?? "(none)");
            SetProperty("IsEnabled", node.IsEnabled?.ToString() ?? "Unknown");
            SetProperty("IsVisible", node.IsVisible?.ToString() ?? "Unknown");
            SetProperty("BoundingRectangle", FormatBounds(node.Bounds));
        }

        private void SetProperty(string name, string value)
        {
            var valueBox = PropertyGrid.Children.OfType<TextBox>()
                .FirstOrDefault(t => t.Name == "Prop_" + name);
            if (valueBox != null)
            {
                valueBox.Text = value;
            }
        }

        private void UpdateXPath(ElementTreeNode node)
        {
            if (!string.IsNullOrEmpty(node.XPath))
            {
                XPathEditor.Text = node.XPath;
                XPathStatus.Text = "✓ XPath available";
                XPathStatus.Foreground = Brushes.Green;
            }
            else
            {
                XPathEditor.Text = GenerateXPath(node);
                XPathStatus.Text = "⚠ XPath generated (not from agent)";
                XPathStatus.Foreground = Brushes.Orange;
            }
        }

        private string GenerateXPath(ElementTreeNode node)
        {
            var sb = new StringBuilder();
            var current = node;
            var path = new List<string>();

            while (current != null)
            {
                var segment = current.ControlType ?? "*";
                if (!string.IsNullOrEmpty(current.AutomationId))
                {
                    segment = $"*[@AutomationId='{current.AutomationId}']";
                }
                else if (!string.IsNullOrEmpty(current.Name))
                {
                    segment = $"*[@Name='{current.Name}']";
                }
                path.Insert(0, segment);
                current = current.Parent;
            }

            sb.Append('/');
            sb.Append(string.Join("/", path));
            return sb.ToString();
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Highlight matching elements as user types
            var searchText = SearchBox.Text.ToLower();
            HighlightMatchingElements(ElementTree.Items, searchText);
        }

        private void Search_Click(object sender, RoutedEventArgs e)
        {
            var searchText = SearchBox.Text.ToLower();
            if (string.IsNullOrEmpty(searchText)) return;

            // Find first matching element
            var result = FindElementByName(ElementTree.Items, searchText);
            if (result != null)
            {
                result.IsSelected = true;
                result.BringIntoView();
            }
        }

        private TreeViewItem? FindElementByName(ItemCollection items, string searchText)
        {
            foreach (TreeViewItem item in items)
            {
                if (item.Tag is ElementTreeNode node)
                {
                    var name = (node.Name ?? "").ToLower();
                    var autoId = (node.AutomationId ?? "").ToLower();
                    
                    if (name.Contains(searchText) || autoId.Contains(searchText))
                    {
                        return item;
                    }
                }

                var childResult = FindElementByName(item.Items, searchText);
                if (childResult != null) return childResult;
            }
            return null;
        }

        private void HighlightMatchingElements(ItemCollection items, string searchText)
        {
            foreach (TreeViewItem item in items)
            {
                if (item.Tag is ElementTreeNode node)
                {
                    var name = (node.Name ?? "").ToLower();
                    var autoId = (node.AutomationId ?? "").ToLower();
                    var matches = name.Contains(searchText) || autoId.Contains(searchText);
                    
                    item.FontWeight = matches ? FontWeights.Bold : FontWeights.Normal;
                }

                HighlightMatchingElements(item.Items, searchText);
            }
        }

        private void ValidateXPath_Click(object sender, RoutedEventArgs e)
        {
            var xpath = XPathEditor.Text;
            if (string.IsNullOrEmpty(xpath))
            {
                XPathStatus.Text = "⚠ XPath is empty";
                XPathStatus.Foreground = Brushes.Orange;
                return;
            }

            try
            {
                // Basic XPath validation
                if (xpath.StartsWith("//") || xpath.StartsWith("/"))
                {
                    XPathStatus.Text = "✓ XPath syntax looks valid";
                    XPathStatus.Foreground = Brushes.Green;
                }
                else
                {
                    XPathStatus.Text = "⚠ XPath should start with / or //";
                    XPathStatus.Foreground = Brushes.Orange;
                }
            }
            catch (Exception ex)
            {
                XPathStatus.Text = $"✗ Invalid XPath: {ex.Message}";
                XPathStatus.Foreground = Brushes.Red;
            }
        }

        private void CopyXPath_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(XPathEditor.Text))
            {
                Clipboard.SetText(XPathEditor.Text);
                XPathStatus.Text = "✓ XPath copied to clipboard";
                XPathStatus.Foreground = Brushes.Green;
            }
        }

        private void CopyAllProps_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedElement == null) return;

            var sb = new StringBuilder();
            sb.AppendLine($"Name: {_selectedElement.Name}");
            sb.AppendLine($"AutomationId: {_selectedElement.AutomationId ?? "(none)"}");
            sb.AppendLine($"ControlType: {_selectedElement.ControlType}");
            sb.AppendLine($"ClassName: {_selectedElement.ClassName ?? "(none)"}");
            sb.AppendLine($"Text: {TruncateText(_selectedElement.Text, 100)}");
            sb.AppendLine($"XPath: {_selectedElement.XPath ?? "(none)"}");
            sb.AppendLine($"IsEnabled: {_selectedElement.IsEnabled}");
            sb.AppendLine($"IsVisible: {_selectedElement.IsVisible}");
            sb.AppendLine($"Bounds: {FormatBounds(_selectedElement.Bounds)}");

            Clipboard.SetText(sb.ToString());
            XPathStatus.Text = "✓ All properties copied to clipboard";
            XPathStatus.Foreground = Brushes.Green;
        }

        private void CopyProperty_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string propName)
            {
                var valueBox = PropertyGrid.Children.OfType<TextBox>()
                    .FirstOrDefault(t => t.Name == "Prop_" + propName);
                if (valueBox != null)
                {
                    Clipboard.SetText(valueBox.Text);
                }
            }
        }

        private void AddToRepository_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedElement != null)
            {
                SelectedAlias = _selectedElement.AutomationId ?? _selectedElement.Name ?? "Unknown";
                SelectedXPath = XPathEditor.Text;
                SelectedProperties = new Dictionary<string, string>
                {
                    ["AutomationId"] = _selectedElement.AutomationId ?? "",
                    ["Name"] = _selectedElement.Name ?? "",
                    ["ControlType"] = _selectedElement.ControlType ?? "",
                    ["XPath"] = _selectedElement.XPath ?? "",
                    ["ClassName"] = _selectedElement.ClassName ?? ""
                };

                DialogResult = true;
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private static string TruncateText(string? text, int maxLength)
        {
            if (string.IsNullOrEmpty(text)) return "(none)";
            if (text.Length <= maxLength) return text;
            return text.Substring(0, maxLength) + "...";
        }

        private static string FormatBounds(Rect? bounds)
        {
            if (!bounds.HasValue) return "(none)";
            var r = bounds.Value;
            return $"X={r.X}, Y={r.Y}, Width={r.Width}, Height={r.Height}";
        }
    }

    // Data classes for tree deserialization
    public class ElementTreeData
    {
        public List<ElementTreeNode> Nodes { get; set; } = new();
    }

    public class ElementTreeNode
    {
        public string? Name { get; set; }
        public string? AutomationId { get; set; }
        public string? ControlType { get; set; }
        public string? ClassName { get; set; }
        public string? Text { get; set; }
        public string? XPath { get; set; }
        public bool? IsEnabled { get; set; }
        public bool? IsVisible { get; set; }
        public Rect? Bounds { get; set; }
        public List<ElementTreeNode> Children { get; set; } = new();
        public ElementTreeNode? Parent { get; set; }

        public string DisplayName => !string.IsNullOrEmpty(AutomationId) 
            ? $"{AutomationId}" 
            : !string.IsNullOrEmpty(Name) 
                ? Name 
                : ControlType ?? "Element";

        public string ControlTypeSuffix => !string.IsNullOrEmpty(ControlType) && !string.IsNullOrEmpty(AutomationId)
            ? $"[{ControlType}]"
            : "";

        public bool HasAutomationId => !string.IsNullOrEmpty(AutomationId);
    }
}
