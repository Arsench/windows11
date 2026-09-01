using System.Collections.ObjectModel;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Zenith.App.Services;
using Zenith.Core.Abstractions;
using Zenith.Core.Safety;
using Zenith.Core.Settings;

namespace Zenith.App.ViewModels;

public sealed record ChoiceOption<T>(T Value, string Label, string Description);

public sealed partial class SettingsViewModel : ObservableObject, INavigationAware
{
    private readonly ISettingsStore _settings;
    private readonly ThemeService _theme;
    private readonly PathSafetyGuard _safety;
    private readonly IShellService _shell;
    private readonly IDialogService _dialogs;

    private bool _loading;

    [ObservableProperty] private ChoiceOption<ThemePreference>? _selectedTheme;
    [ObservableProperty] private ChoiceOption<int>? _selectedInterval;
    [ObservableProperty] private ChoiceOption<DeletionBehavior>? _selectedDeletion;
    [ObservableProperty] private string _defaultMoveFolderText = "Sin definir";

    public SettingsViewModel(
        ISettingsStore settings,
        ThemeService theme,
        PathSafetyGuard safety,
        IShellService shell,
        IDialogService dialogs)
    {
        _settings = settings;
        _theme = theme;
        _safety = safety;
        _shell = shell;
        _dialogs = dialogs;
    }

    public IReadOnlyList<ChoiceOption<ThemePreference>> ThemeOptions { get; } =
    [
        new(ThemePreference.System, "Seguir a Windows", "Cambia con el modo del sistema"),
        new(ThemePreference.Light, "Claro", "Siempre en claro"),
        new(ThemePreference.Dark, "Oscuro", "Siempre en oscuro")
    ];

    public IReadOnlyList<ChoiceOption<int>> IntervalOptions { get; } =
    [
        new(500, "Muy rápido", "Cada medio segundo. Solo si te importa la latencia."),
        new(1000, "Normal", "Cada segundo. Recomendado."),
        new(2000, "Ahorro", "Cada dos segundos. Menos consumo.")
    ];

    public IReadOnlyList<ChoiceOption<DeletionBehavior>> DeletionOptions { get; } =
    [
        new(DeletionBehavior.RecycleBin, "Papelera de reciclaje", "Recomendado: siempre se puede deshacer"),
        new(DeletionBehavior.Permanent, "Borrado definitivo", "Irreversible. Zenith pedirá confirmación reforzada.")
    ];

    public ObservableCollection<string> ExcludedPaths { get; } = [];

    public string VersionText
    {
        get
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            return version is null ? "Zenith" : $"Zenith {version.Major}.{version.Minor}.{version.Build}";
        }
    }

    public string LogFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Zenith", "logs");

    public void OnNavigatedTo()
    {
        _loading = true;
        try
        {
            var current = _settings.Current;

            SelectedTheme = ThemeOptions.FirstOrDefault(o => o.Value == current.Theme) ?? ThemeOptions[0];
            SelectedInterval = IntervalOptions.FirstOrDefault(o => o.Value == current.MonitorIntervalMs) ?? IntervalOptions[1];
            SelectedDeletion = DeletionOptions.FirstOrDefault(o => o.Value == current.DeletionBehavior) ?? DeletionOptions[0];
            DefaultMoveFolderText = string.IsNullOrWhiteSpace(current.DefaultMoveFolder)
                ? "Sin definir"
                : current.DefaultMoveFolder;

            ExcludedPaths.Clear();
            foreach (var path in current.ExcludedPaths) ExcludedPaths.Add(path);
        }
        finally
        {
            _loading = false;
        }
    }

    public void OnNavigatedFrom() { }

    partial void OnSelectedThemeChanged(ChoiceOption<ThemePreference>? value)
    {
        if (_loading || value is null) return;

        _theme.Apply(value.Value);
        _ = _settings.UpdateAsync(s => s.Theme = value.Value);
    }

    partial void OnSelectedIntervalChanged(ChoiceOption<int>? value)
    {
        if (_loading || value is null) return;
        _ = _settings.UpdateAsync(s => s.MonitorIntervalMs = value.Value);
    }

    partial void OnSelectedDeletionChanged(ChoiceOption<DeletionBehavior>? value)
    {
        if (_loading || value is null) return;
        _ = _settings.UpdateAsync(s => s.DeletionBehavior = value.Value);
    }

    [RelayCommand]
    private async Task ChooseMoveFolderAsync()
    {
        var folder = _dialogs.PickFolder("Carpeta por defecto para mover duplicados", _settings.Current.DefaultMoveFolder);
        if (folder is null) return;

        await _settings.UpdateAsync(s => s.DefaultMoveFolder = folder).ConfigureAwait(true);
        DefaultMoveFolderText = folder;
    }

    [RelayCommand]
    private async Task AddExclusionAsync()
    {
        var folder = _dialogs.PickFolder("Carpeta que Zenith nunca debe tocar");
        if (folder is null) return;

        if (ExcludedPaths.Any(p => string.Equals(p, folder, StringComparison.OrdinalIgnoreCase))) return;

        ExcludedPaths.Add(folder);
        await PersistExclusionsAsync().ConfigureAwait(true);
        _dialogs.Notify("Exclusión añadida.", ToastKind.Success);
    }

    [RelayCommand]
    private async Task RemoveExclusionAsync(string? path)
    {
        if (path is null) return;

        ExcludedPaths.Remove(path);
        await PersistExclusionsAsync().ConfigureAwait(true);
    }

    private async Task PersistExclusionsAsync()
    {
        var paths = ExcludedPaths.ToList();
        await _settings.UpdateAsync(s => s.ExcludedPaths = paths).ConfigureAwait(true);
        _safety.SetUserExclusions(paths);
    }

    [RelayCommand]
    private void OpenLogFolder()
    {
        Directory.CreateDirectory(LogFolder);
        _shell.OpenFolder(LogFolder);
    }
}
