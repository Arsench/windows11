using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Zenith.App.Services;
using Zenith.Core.Models;
using Zenith.Core.Monitoring;

namespace Zenith.App.ViewModels;

/// <summary>
/// Base de las pantallas que muestran datos en vivo. Se suscribe al muestreo
/// solo mientras la página está visible: entrar en "Duplicados" no debe seguir
/// repintando gráficos que nadie ve.
/// </summary>
public abstract partial class MonitoringViewModelBase : ObservableObject, INavigationAware
{
    private readonly Dispatcher _dispatcher;
    private bool _subscribed;

    protected MonitoringViewModelBase(MonitoringService monitoring)
    {
        Monitoring = monitoring;
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
    }

    protected MonitoringService Monitoring { get; }

    public virtual void OnNavigatedTo()
    {
        if (!_subscribed)
        {
            Monitoring.SnapshotAvailable += OnSnapshot;
            _subscribed = true;
        }

        // Pinta de inmediato con lo último conocido en lugar de esperar un tick.
        Apply(Monitoring.Latest);
    }

    public virtual void OnNavigatedFrom()
    {
        if (!_subscribed) return;
        Monitoring.SnapshotAvailable -= OnSnapshot;
        _subscribed = false;
    }

    private void OnSnapshot(object? sender, SystemSnapshot snapshot)
    {
        // El muestreo ocurre en segundo plano; la UI solo se toca desde su hilo.
        if (_dispatcher.CheckAccess()) Apply(snapshot);
        else _dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => Apply(snapshot)));
    }

    protected abstract void Apply(SystemSnapshot snapshot);
}
