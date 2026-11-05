using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace WpfTestIde
{
    /// <summary>
    /// Async <see cref="ICommand"/> companion to <see cref="RelayCommand"/>. Wraps a
    /// <c>Func<object?, Task></c> so the bound Button fires the execute on the
    /// UI thread and awaits the returned task (vs the older fire-and-forget
    /// <c>RelayCommand(async _ => ...)</c> which never observed the task and so
    /// could neither disable the button nor surface a fault). Kept dependency-free
    /// rather than pulling in an MVVM toolkit for a reference project — mirrors the
    /// existing <see cref="RelayCommand"/> boilerplate.
    /// </summary>
    /// <remarks>
    /// <b><see cref="CanExecute"/> returns <see langword="false"/> while
    /// <see cref="IsRunning"/> is <see langword="true"/></b> so the bound Button
    /// auto-disables while the task is in flight and re-enables on completion
    /// without any per-command bespoke wiring. <see cref="IsRunning"/> flips fire
    /// <see cref="CommandManager.InvalidateRequerySuggested"/> (rather than the
    /// piggyback on <see cref="CommandManager.RequerySuggested"/> used by
    /// <see cref="RelayCommand"/>) so the re-enable actually propagates to the
    /// view when the task finishes.
    /// <para>
    /// Unhandled exceptions thrown by the execute delegate are swallowed and their
    /// <see cref="Exception.Message"/> written back to the host window's
    /// <c>StatusText</c> (fail-soft — the same pattern the existing
    /// <c>CheckPipeConnection</c>/<c>RunAsync</c> already use for their inline
    /// "<c>... failed: {ex.Message}</c>" strings). Nothing is silently eaten.
    /// </para>
    /// </remarks>
    public sealed class AsyncRelayCommand : ICommand, INotifyPropertyChanged
    {
        private readonly Func<object?, Task> _execute;
        private readonly Func<object?, bool>? _canExecute;
        private bool _isRunning;

        public AsyncRelayCommand(Func<object?, Task> execute, Func<object?, bool>? canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

        /// <summary><see langword="true"/> while the wrapped task is in flight.
        /// Bindable (INPC) so callers can observe state, and consulted by
        /// <see cref="CanExecute"/> to keep the bound Button disabled mid-run.</summary>
        public bool IsRunning
        {
            get => _isRunning;
            private set
            {
                if (_isRunning == value) return;
                _isRunning = value;
                OnPropertyChanged();
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        public bool CanExecute(object? parameter) =>
            !_isRunning && (_canExecute?.Invoke(parameter) ?? true);

        public async void Execute(object? parameter)
        {
            if (_isRunning) return;
            IsRunning = true;
            try
            {
                await _execute(parameter);
            }
            catch (Exception ex)
            {
                // Fail-soft: report to the StatusBar slot the operator already
                // scans. Avoids losing the fault entirely while not crashing the
                // UI thread (an unobserved fire-and-forget would do worse).
                var status = Application.Current?.MainWindow?.DataContext
                    as ViewModels.MainViewModel;
                if (status != null)
                {
                    status.StatusText = $"Command failed: {ex.Message}";
                }
            }
            finally
            {
                IsRunning = false;
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
