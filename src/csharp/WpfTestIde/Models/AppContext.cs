using System.ComponentModel;

namespace WpfTestIde.Models
{
    /// <summary>
    /// Represents a single application under automation in the IDE.
    /// Mirrors the Python framework's AppContext for consistency.
    /// </summary>
    public class AppContext : INotifyPropertyChanged
    {
        private string _appId = "";
        private string _appName = "";
        private string _driver = "FlaUI";
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

        public string Driver
        {
            get => _driver;
            set { _driver = value; OnPropertyChanged(); }
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

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
