using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Linq;

namespace WpfTestIde.Models
{
    /// <summary>
    /// Represents a single application under automation in the IDE.
    /// Mirrors the Python framework's AppContext for consistency.
    /// </summary>
    public class AppContext : INotifyPropertyChanged
    {
        private static readonly string[] FixedDriverOrder = { "UIA3", "WPFSpy", "OculiX" };

        private string _appId = "";
        private string _appName = "";
        private string _driver = "UIA3";
        private List<string>? _applicableDrivers;
        private int _processId;
        private string _pipeName = "WPFSpyAgentPipe";
        private string _appPath = "";
        private bool _isDefault;
        private bool _isAttached;

        public string AppId
        {
            get => _appId;
            set { _appId = value; OnPropertyChanged(); }
        }

        public string AppName
        {
            get => _appName;
            set { _appName = value; OnPropertyChanged(); }
        }

        /// <summary>Primary/legacy driver hint. Runtime priority remains fixed UIA3-&gt;WPFSpy-&gt;OculiX.</summary>
        public string Driver
        {
            get => _driver;
            set { _driver = value; OnPropertyChanged(); }
        }

        /// <summary>Drivers applicable to this app (checkbox membership, not ordered).
        /// Null/empty means all drivers are applicable.</summary>
        public List<string>? ApplicableDrivers
        {
            get => _applicableDrivers;
            set { _applicableDrivers = NormalizeApplicable(value); OnPropertyChanged(); }
        }

        public int ProcessId
        {
            get => _processId;
            set { _processId = value; OnPropertyChanged(); }
        }

        public string PipeName
        {
            get => _pipeName;
            set { _pipeName = value; OnPropertyChanged(); }
        }

        public string AppPath
        {
            get => _appPath;
            set { _appPath = value; OnPropertyChanged(); }
        }

        public bool IsDefault
        {
            get => _isDefault;
            set { _isDefault = value; OnPropertyChanged(); }
        }

        public bool IsAttached
        {
            get => _isAttached;
            set { _isAttached = value; OnPropertyChanged(); }
        }

        public string DisplayText => string.IsNullOrEmpty(AppName)
            ? $"PID {ProcessId}"
            : $"{AppName} (PID {ProcessId})";

        public static List<string>? NormalizeApplicable(IEnumerable<string>? drivers)
        {
            if (drivers == null) return null;
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var d in drivers)
            {
                if (string.IsNullOrWhiteSpace(d)) continue;
                var match = FixedDriverOrder.FirstOrDefault(f => f.Equals(d.Trim(), StringComparison.OrdinalIgnoreCase));
                if (match != null) set.Add(match);
            }
            if (set.Count == 0) return null;
            return FixedDriverOrder.Where(set.Contains).ToList();
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
