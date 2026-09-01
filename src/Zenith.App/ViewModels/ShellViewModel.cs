using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Zenith.App.Services;

namespace Zenith.App.ViewModels;

public sealed partial class NavigationItemViewModel : ObservableObject
{
    private readonly Action<NavigationItemViewModel> _onSelected;

    public NavigationItemViewModel(
        string title,
        string glyph,
        ObservableObject content,
        Action<NavigationItemViewModel> onSelected)
    {
        Title = title;
        Glyph = glyph;
        Content = content;
        _onSelected = onSelected;
    }

    [ObservableProperty] private bool _isSelected;

    public string Title { get; }

    /// <summary>Punto de código de Segoe Fluent Icons.</summary>
    public string Glyph { get; }

    public ObservableObject Content { get; }

    partial void OnIsSelectedChanged(bool value)
    {
        if (value) _onSelected(this);
    }
}

/// <summary>Contenedor de la ventana: navegación, página activa, diálogos y avisos.</summary>
public sealed partial class ShellViewModel : ObservableObject
{
    [ObservableProperty] private ObservableObject? _currentPage;
    [ObservableProperty] private string _currentTitle = "Panel";
    [ObservableProperty] private string _currentSubtitle = string.Empty;

    public ShellViewModel(
        DashboardViewModel dashboard,
        SystemViewModel system,
        StorageViewModel storage,
        DuplicatesViewModel duplicates,
        SettingsViewModel settings,
        DialogService dialogs)
    {
        Dialogs = dialogs;

        // Glifos de Segoe Fluent Icons: sin dependencias de iconos de terceros.
        Items =
        [
            new NavigationItemViewModel("Panel", "\uE80F", dashboard, Select),
            new NavigationItemViewModel("Sistema", "\uE950", system, Select),
            new NavigationItemViewModel("Almacenamiento", "\uEDA2", storage, Select),
            new NavigationItemViewModel("Duplicados", "\uE8C8", duplicates, Select)
        ];

        FooterItems =
        [
            new NavigationItemViewModel("Configuración", "\uE713", settings, Select)
        ];
    }

    public DialogService Dialogs { get; }

    public ObservableCollection<NavigationItemViewModel> Items { get; }

    public ObservableCollection<NavigationItemViewModel> FooterItems { get; }

    public void SelectFirst() => Items[0].IsSelected = true;

    private void Select(NavigationItemViewModel item)
    {
        if (ReferenceEquals(CurrentPage, item.Content)) return;

        // Solo un elemento marcado a la vez, aunque vengan de dos listas distintas.
        foreach (var other in Items.Concat(FooterItems))
        {
            if (!ReferenceEquals(other, item)) other.IsSelected = false;
        }

        (CurrentPage as INavigationAware)?.OnNavigatedFrom();

        CurrentPage = item.Content;
        CurrentTitle = item.Title;
        CurrentSubtitle = SubtitleFor(item.Title);

        (item.Content as INavigationAware)?.OnNavigatedTo();
    }

    private static string SubtitleFor(string title) => title switch
    {
        "Panel" => "Estado general del equipo en tiempo real",
        "Sistema" => "Procesador, memoria, gráfica y sensores",
        "Almacenamiento" => "Unidades y en qué se está yendo el espacio",
        "Duplicados" => "Busca y elimina copias idénticas con seguridad",
        "Configuración" => "Preferencias de Zenith",
        _ => string.Empty
    };
}
