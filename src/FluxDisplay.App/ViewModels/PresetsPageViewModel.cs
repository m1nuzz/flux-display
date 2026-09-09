using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluxDisplay.App.Models;
using FluxDisplay.App.Services;

namespace FluxDisplay.App.ViewModels;

public sealed partial class PresetsPageViewModel : ObservableObject
{
    private readonly AppServices _services;

    public PresetsPageViewModel(AppServices services)
    {
        _services = services;
        Items = new ObservableCollection<PresetViewModel>();
    }

    public ObservableCollection<PresetViewModel> Items { get; }

    [ObservableProperty]
    private bool _isEmpty = true;

    [ObservableProperty]
    private bool _isBusy;

    public event EventHandler? PresetsChanged;
    public event EventHandler? AddRequested;

    [RelayCommand]
    public void RequestAdd() => AddRequested?.Invoke(this, EventArgs.Empty);

    public async Task ReloadAsync()
    {
        using var _ = Helpers.AppLog.Scope("PresetsPage.ReloadAsync");
        IsBusy = true;
        try
        {
            var collection = await _services.Presets.LoadAsync().ConfigureAwait(true);
            Items.Clear();
            foreach (var preset in collection.Presets)
            {
                Items.Add(Bind(preset));
            }

            await RefreshActiveAsync().ConfigureAwait(true);
            IsEmpty = Items.Count == 0;
            PresetsChanged?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task AddPresetAsync(Preset preset)
    {
        var collection = await _services.Presets.LoadAsync().ConfigureAwait(true);
        collection.Presets.Add(preset);
        await _services.Presets.SaveAsync(collection).ConfigureAwait(true);
        Items.Add(Bind(preset));
        IsEmpty = false;
        PresetsChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task ApplyByIdAsync(Guid id)
    {
        var item = Items.FirstOrDefault(x => x.Id == id);
        if (item is not null)
        {
            await ApplyAsync(item).ConfigureAwait(true);
        }
    }

    [RelayCommand]
    public async Task ApplyAsync(PresetViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        Helpers.AppLog.Info($"PresetsPage.ApplyAsync '{item.Name}'");

        var settings = await _services.SettingsStore.LoadAsync().ConfigureAwait(true);
        if (settings.ConfirmBeforeApply)
        {
            var ok = await _services.Dialog.ShowConfirmAsync("Apply preset", $"Apply '{item.Name}'?").ConfigureAwait(true);
            if (!ok)
            {
                return;
            }
        }

        item.IsApplying = true;
        try
        {
            var applied = await _services.Applier.ApplyAsync(item.Preset).ConfigureAwait(true);
            if (applied)
            {
                await RefreshActiveAsync().ConfigureAwait(true);
                PresetsChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        finally
        {
            item.IsApplying = false;
        }
    }

    [RelayCommand]
    public async Task DeleteAsync(PresetViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        Helpers.AppLog.Info($"PresetsPage.DeleteAsync '{item.Name}'");

        var confirm = await _services.Dialog.ShowConfirmAsync("Delete preset", $"Delete '{item.Name}'?", "Delete", "Cancel").ConfigureAwait(true);
        if (!confirm)
        {
            return;
        }

        var collection = await _services.Presets.LoadAsync().ConfigureAwait(true);
        collection.Presets.RemoveAll(p => p.Id == item.Id);
        await _services.Presets.SaveAsync(collection).ConfigureAwait(true);
        Items.Remove(item);
        IsEmpty = Items.Count == 0;
        PresetsChanged?.Invoke(this, EventArgs.Empty);
    }

    private PresetViewModel Bind(Preset preset)
    {
        var vm = new PresetViewModel(preset)
        {
            ApplyCommand = ApplyCommand,
            DeleteCommand = DeleteCommand
        };
        return vm;
    }

    private async Task RefreshActiveAsync()
    {
        using var _ = Helpers.AppLog.Scope("PresetsPage.RefreshActiveAsync", $"items={Items.Count}");
        foreach (var item in Items)
        {
            item.IsActive = await _services.Applier.IsPresetActiveAsync(item.Preset).ConfigureAwait(true);
        }
    }
}
