using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Zenith.App.Localization;
using Zenith.App.Services;

namespace Zenith.App.ViewModels;

public sealed partial class NavigationItemViewModel : ObservableObject
{
    private readonly Action<NavigationItemViewModel> _onSelected;

    public NavigationItemViewModel(
        string titleKey,
        string subtitleKey,
        string glyph,
        ObservableObject content,
        Action<NavigationItemViewModel> onSelected)
    {
        TitleKey = titleKey;
        SubtitleKey = subtitleKey;
        Glyph = glyph;
        Content = content;
        _onSelected = onSelected;
    }

    [ObservableProperty] private bool _isSelected;

    public string TitleKey { get; }

    public string SubtitleKey { get; }

    public string Title => Loc.Instance[TitleKey];

    public string Subtitle => Loc.Instance[SubtitleKey];

    /// <summary>Punto de código de Segoe Fluent Icons.</summary>
    public string Glyph { get; }

    public ObservableObject Content { get; }

    public void RefreshTitle()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Subtitle));
    }

    partial void OnIsSelectedChanged(bool value)
    {
        if (value) _onSelected(this);
    }
}

/// <summary>Contenedor de la ventana: navegación, página activa, diálogos y avisos.</summary>
public sealed partial class ShellViewModel : ObservableObject
{
    private NavigationItemViewModel? _current;

    [ObservableProperty] private ObservableObject? _currentPage;
    [ObservableProperty] private string _currentTitle = string.Empty;
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

        // Glifos de Segoe Fluent Icons (la fuente de iconos que ya trae Windows 11):
        // cero dependencias de iconografía externa. Centralizados aquí a propósito.
        Items =
        [
            new NavigationItemViewModel("NavDashboard", "NavDashboardSubtitle", "\uE80F", dashboard, Select),
            new NavigationItemViewModel("NavSystem", "NavSystemSubtitle", "\uE950", system, Select),
            new NavigationItemViewModel("NavStorage", "NavStorageSubtitle", "\uEDA2", storage, Select),
            new NavigationItemViewModel("NavDuplicates", "NavDuplicatesSubtitle", "\uE8C8", duplicates, Select)
        ];

        FooterItems =
        [
            new NavigationItemViewModel("NavSettings", "NavSettingsSubtitle", "\uE713", settings, Select)
        ];

        Loc.Instance.LanguageChanged += (_, _) => RefreshLocalizedText();
    }

    public DialogService Dialogs { get; }

    public ObservableCollection<NavigationItemViewModel> Items { get; }

    public ObservableCollection<NavigationItemViewModel> FooterItems { get; }

    public void SelectFirst() => Items[0].IsSelected = true;

    private void RefreshLocalizedText()
    {
        foreach (var item in Items.Concat(FooterItems)) item.RefreshTitle();

        if (_current is null) return;
        CurrentTitle = _current.Title;
        CurrentSubtitle = _current.Subtitle;
    }

    private void Select(NavigationItemViewModel item)
    {
        if (ReferenceEquals(CurrentPage, item.Content)) return;

        // Solo un elemento marcado a la vez, aunque vengan de dos listas distintas.
        foreach (var other in Items.Concat(FooterItems))
        {
            if (!ReferenceEquals(other, item)) other.IsSelected = false;
        }

        (CurrentPage as INavigationAware)?.OnNavigatedFrom();

        _current = item;
        CurrentPage = item.Content;
        CurrentTitle = item.Title;
        CurrentSubtitle = item.Subtitle;

        (item.Content as INavigationAware)?.OnNavigatedTo();
    }
}
