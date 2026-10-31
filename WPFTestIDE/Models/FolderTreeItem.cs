using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace WpfTestIde.Models
{
    /// <summary>
    /// One node in the Working Folder tree view. Directories are expanded
    /// on demand (lazy child load) so deep trees don't block the UI thread.
    /// Files are leaf nodes; the .json/.py/.robot extension drives the
    /// import action bound to the selected node.
    /// </summary>
    public class FolderTreeItem : INotifyPropertyChanged
    {
        public string Name { get; }
        public string FullPath { get; }
        public bool IsDirectory { get; }
        public string? Extension => IsDirectory ? null : Path.GetExtension(FullPath);

        public ObservableCollection<FolderTreeItem> Children { get; } = new();
        public bool ChildrenLoaded { get; private set; }

        private bool _isExpanded;
        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                _isExpanded = value;
                OnPropertyChanged();
                if (value && !ChildrenLoaded && IsDirectory)
                {
                    LoadChildren();
                }
            }
        }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }

        private FolderTreeItem? _parent;
        public FolderTreeItem? Parent => _parent;

        private FolderTreeItem(string name, string fullPath, bool isDirectory, FolderTreeItem? parent = null)
        {
            Name = name;
            FullPath = fullPath;
            IsDirectory = isDirectory;
            _parent = parent;
        }

        public static FolderTreeItem CreateRoot(string path)
        {
            var dir = new DirectoryInfo(path);
            return new FolderTreeItem(dir.Name, dir.FullName, true, null);
        }

        public void LoadChildren()
        {
            if (!IsDirectory) return;
            try
            {
                Children.Clear();
                // Directories first, then files, both alphabetically.
                foreach (var sub in Directory.GetDirectories(FullPath))
                {
                    Children.Add(new FolderTreeItem(Path.GetFileName(sub)!, sub, true, this));
                }
                foreach (var file in Directory.GetFiles(FullPath))
                {
                    Children.Add(new FolderTreeItem(Path.GetFileName(file)!, file, false, this));
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Skip folders we can't read.
            }
            catch (IOException)
            {
                // Skip transiently-unavailable folders.
            }
            ChildrenLoaded = true;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
