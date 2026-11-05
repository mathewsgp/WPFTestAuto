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

        /// <summary>Human-readable file category shown in the tree view:
        /// "Element JSON", "Script JSON", "Python", "Robot", "Directory", or
        /// "File". Computed lazily from the extension and, for .json files,
        /// the file's parent directory name so element vs. script JSONs are
        /// visually distinct before the user clicks Import. The result is
        /// cached after the first computation so a deep tree doesn't re-read
        /// every file on each render pass.</summary>
        public string FileType
        {
            get
            {
                if (_fileType is not null) return _fileType;
                _fileType = ComputeFileType();
                return _fileType;
            }
        }

        private string? _fileType;

        private string ComputeFileType()
        {
            if (IsDirectory) return "Directory";
            var ext = (Extension ?? "").ToLowerInvariant();
            return ext switch
            {
                ".json" => IsScriptJson() ? "Script JSON" : "Element JSON",
                ".py" => "Python",
                ".robot" => "Robot",
                _ => "File",
            };
        }

        /// <summary>Heuristic: a .json file is treated as a *script* JSON when
        /// its parent directory name contains "script" or "step", or when the
        /// file's own root key is "steps". Otherwise it is an *element* JSON
        /// (root key "elements"). Falls back to extension-only detection if
        /// the file can't be read or parsed.</summary>
        public bool IsScriptJson()
        {
            var parentDir = Path.GetFileName(Path.GetDirectoryName(FullPath) ?? "");
            if (!string.IsNullOrEmpty(parentDir) &&
                (parentDir.Contains("script", StringComparison.OrdinalIgnoreCase) ||
                 parentDir.Contains("step", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            try
            {
                string json = File.ReadAllText(FullPath);
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                var root = doc.RootElement;
                // A script JSON has a top-level "steps" array; an element JSON
                // has a top-level "elements" object. If both are present (unlikely),
                // prefer "steps" because that is the file the user most likely
                // intended to import as a script.
                if (root.TryGetProperty("steps", out _)) return true;
                if (root.TryGetProperty("elements", out _)) return false;
            }
            catch
            {
                // Unreadable / unparseable — fall back to extension-only.
            }
            return false;
        }

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
