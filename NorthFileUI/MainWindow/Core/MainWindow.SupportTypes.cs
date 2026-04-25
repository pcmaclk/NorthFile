using NorthFileUI.Workspace;
using Microsoft.UI.Xaml;
using System;
using System.Threading.Tasks;
using System.Windows.Input;

namespace NorthFileUI
{
    internal enum SelectionSurfaceId
    {
        Sidebar,
        PrimaryPane,
        SecondaryPane
    }

    internal sealed class DelegateCommand : ICommand
    {
        private readonly Action<object?> _execute;

        public DelegateCommand(Action<object?> execute)
        {
            _execute = execute;
        }

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => _execute(parameter);

        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }

    internal sealed class InlineEditCoordinator
    {
        private InlineEditSession? _activeSession;

        public bool HasActiveSession => _activeSession is not null;

        public void BeginSession(InlineEditSession session)
        {
            if (_activeSession is not null && !ReferenceEquals(_activeSession, session))
            {
                _activeSession.Cancel();
            }

            _activeSession = session;
        }

        public void ClearSession(InlineEditSession session)
        {
            if (ReferenceEquals(_activeSession, session))
            {
                _activeSession = null;
            }
        }

        public bool IsSourceWithinActiveSession(DependencyObject? source)
        {
            return _activeSession?.ContainsSource(source) == true;
        }

        public Task CommitActiveSessionAsync()
        {
            return _activeSession?.CommitAsync() ?? Task.CompletedTask;
        }

        public bool ShouldCommitActiveSessionOnExternalClick()
        {
            return _activeSession?.CommitOnExternalClick ?? true;
        }

        public void CancelActiveSession()
        {
            _activeSession?.Cancel();
        }
    }

    internal sealed class InlineEditSession
    {
        private readonly Func<Task> _commitAsync;
        private readonly Action _cancel;
        private readonly Func<DependencyObject?, bool> _containsSource;
        public bool CommitOnExternalClick { get; }

        public InlineEditSession(
            Func<Task> commitAsync,
            Action cancel,
            Func<DependencyObject?, bool> containsSource,
            bool commitOnExternalClick = true)
        {
            _commitAsync = commitAsync;
            _cancel = cancel;
            _containsSource = containsSource;
            CommitOnExternalClick = commitOnExternalClick;
        }

        public Task CommitAsync() => _commitAsync();

        public void Cancel() => _cancel();

        public bool ContainsSource(DependencyObject? source) => _containsSource(source);
    }

    internal sealed class SelectionSurfaceCoordinator
    {
        private bool _isWindowActive = true;

        public SelectionSurfaceCoordinator(SelectionSurfaceId initialActiveSurface)
        {
            ActiveSurface = initialActiveSurface;
        }

        public SelectionSurfaceId ActiveSurface { get; private set; }

        public bool SetWindowActive(bool isActive)
        {
            if (_isWindowActive == isActive)
            {
                return false;
            }

            _isWindowActive = isActive;
            return true;
        }

        public bool SetActiveSurface(SelectionSurfaceId surface)
        {
            if (ActiveSurface == surface)
            {
                return false;
            }

            ActiveSurface = surface;
            return true;
        }

        public bool IsSurfaceActive(SelectionSurfaceId surface)
        {
            return _isWindowActive && ActiveSurface == surface;
        }
    }
}
