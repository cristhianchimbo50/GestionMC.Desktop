using System;
using System.Threading.Tasks;
using System.Windows.Input;

namespace GestionMC.Desktop.ViewModels;

public class RelayCommand : ICommand
{
    private readonly Func<bool>? _canExecute;
    private readonly Func<Task>? _executeAsync;

    public RelayCommand(Func<Task> executeAsync, Func<bool>? canExecute = null)
    {
        _executeAsync = executeAsync ?? throw new ArgumentNullException(nameof(executeAsync));
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

    public async void Execute(object? parameter)
    {
        await _executeAsync();
    }

    public event EventHandler? CanExecuteChanged;

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
