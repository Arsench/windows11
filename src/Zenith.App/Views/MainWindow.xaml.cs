using System.Windows;
using Wpf.Ui.Controls;
using Zenith.App.ViewModels;
using Zenith.Core.Monitoring;

namespace Zenith.App.Views;

public partial class MainWindow : FluentWindow
{
    private readonly MonitoringService _monitoring;

    public MainWindow(ShellViewModel viewModel, MonitoringService monitoring)
    {
        _monitoring = monitoring;

        InitializeComponent();
        DataContext = viewModel;

        Loaded += (_, _) => viewModel.SelectFirst();

        // Con la ventana en segundo plano bajamos la cadencia de muestreo:
        // una app de estadísticas no debe consumir CPU cuando no la miras.
        Activated += (_, _) => _monitoring.SetForeground(true);
        Deactivated += (_, _) => _monitoring.SetForeground(false);
        StateChanged += OnStateChanged;
    }

    private void OnStateChanged(object? sender, EventArgs e) =>
        _monitoring.SetForeground(WindowState != WindowState.Minimized);
}
