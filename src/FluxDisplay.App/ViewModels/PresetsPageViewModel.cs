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
        // Drag-reorder (and any reordering) mutates Items directly; the
        // ListView's DragItemsCompleted proved unreliable (never fired), so
        // the collection itself is the source of truth for persisting order.
        Items.CollectionChanged += OnItemsCollectionChanged;
    }

    public ObservableCollection<PresetViewModel> Items { get; }

    [ObservableProperty]
    private bool _isEmpty = true;

    [ObservableProperty]
    private bool _isBusy;

    public event EventHandler? PresetsChanged;
    public event EventHandler? AddRequested;
    public event EventHandler<Guid>? EditRequested;

    [RelayCommand]
    public void RequestAdd() => AddRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    public void Edit(PresetViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        Helpers.AppLog.Info($"PresetsPage.Edit '{item.Name}'");
        EditRequested?.Invoke(this, item.Id);
    }

    public async Task UpdatePresetAsync(Preset preset)
    {
        using var _ = Helpers.AppLog.Scope("PresetsPage.UpdatePresetAsync", $"'{preset.Name}'");
        var collection = await _services.Presets.LoadAsync().ConfigureAwait(true);
        var index = collection.Presets.FindIndex(p => p.Id == preset.Id);
        if (index < 0)
        {
            // Edited preset vanished meanwhile — fall back to adding it back.
            await AddPresetAsync(preset).ConfigureAwait(true);
            return;
        }

        collection.Presets[index] = preset;
        await _services.Presets.SaveAsync(collection).ConfigureAwait(true);
        var itemIndex = Items.ToList().FindIndex(i => i.Id == preset.Id);
        if (itemIndex >= 0)
        {
            _suppressOrderPersist = true;
            try
            {
                Items[itemIndex] = Bind(preset);
            }
            finally
            {
                _suppressOrderPersist = false;
            }
        }

        await RefreshActiveAsync().ConfigureAwait(true);
        PresetsChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task ReloadAsync()
    {
        using var _ = Helpers.AppLog.Scope("PresetsPage.ReloadAsync");
        IsBusy = true;
        try
        {
        var collection = await _services.Presets.LoadAsync().ConfigureAwait(true);
        _suppressOrderPersist = true;
        try
        {
            Items.Clear();
            foreach (var preset in collection.Presets)
            {
                Items.Add(Bind(preset));
            }
        }
        finally
        {
            _suppressOrderPersist = false;
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
        _suppressOrderPersist = true;
        try
        {
            Items.Add(Bind(preset));
        }
        finally
        {
            _suppressOrderPersist = false;
        }

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
        _suppressOrderPersist = true;
        try
        {
            Items.Remove(item);
        }
        finally
        {
            _suppressOrderPersist = false;
        }

        IsEmpty = Items.Count == 0;
        PresetsChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    public async Task ToggleTrayAsync(PresetViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        Helpers.AppLog.Info($"PresetsPage.ToggleTray '{item.Name}' show={item.Preset.ShowInTray}");
        item.Preset.ShowInTray = !item.Preset.ShowInTray;
        item.ShowInTray = item.Preset.ShowInTray;
        var collection = await _services.Presets.LoadAsync().ConfigureAwait(true);
        var stored = collection.Presets.FirstOrDefault(p => p.Id == item.Id);
        if (stored is not null)
        {
            stored.ShowInTray = item.Preset.ShowInTray;
        }

        await _services.Presets.SaveAsync(collection).ConfigureAwait(true);
        PresetsChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    public void MoveUp(PresetViewModel? item) => Move(item, -1);

    [RelayCommand]
    public void MoveDown(PresetViewModel? item) => Move(item, +1);

    private void Move(PresetViewModel? item, int delta)
    {
        if (item is null)
        {
            return;
        }

        var index = Items.IndexOf(item);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= Items.Count)
        {
            return;
        }

        Helpers.AppLog.Info($"PresetsPage.Move '{item.Name}' {index}->{target}");
        _suppressOrderPersist = true;
        try
        {
            Items.Move(index, target);
        }
        finally
        {
            _suppressOrderPersist = false;
        }

        _ = PersistOrderAsync();
    }

    internal async Task PersistOrderAsync()
    {
        try
        {
            Helpers.AppLog.Info($"PresetsPage.PersistOrder items={Items.Count}");
            var collection = await _services.Presets.LoadAsync().ConfigureAwait(true);
            var byId = collection.Presets.ToDictionary(p => p.Id);
            var ordered = new List<Preset>(Items.Count);
            foreach (var item in Items)
            {
                if (byId.TryGetValue(item.Id, out var stored))
                {
                    ordered.Add(stored);
                }
            }

            // Items dropped from the list meanwhile (deleted elsewhere) keep
            // their relative tail order instead of vanishing from disk.
            foreach (var stored in collection.Presets)
            {
                if (!ordered.Any(p => p.Id == stored.Id))
                {
                    ordered.Add(stored);
                }
            }

            collection.Presets = ordered;
            await _services.Presets.SaveAsync(collection).ConfigureAwait(true);
            Helpers.AppLog.Info($"PresetsPage.PersistOrder saved order={string.Join(",", ordered.Select(p => p.Name))}");
            PresetsChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Helpers.AppLog.Error(ex, "PresetsPage.PersistOrder");
        }
    }

    private bool _suppressOrderPersist;

    private void OnItemsCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        // Programmatic mutations (reload/add/delete/edit/arrows) persist
        // explicitly; this catches the native drag-reorder, which ListView
        // applies straight to Items.
        if (_suppressOrderPersist)
        {
            return;
        }

        switch (e.Action)
        {
            case System.Collections.Specialized.NotifyCollectionChangedAction.Move:
            case System.Collections.Specialized.NotifyCollectionChangedAction.Add:
            case System.Collections.Specialized.NotifyCollectionChangedAction.Remove:
            case System.Collections.Specialized.NotifyCollectionChangedAction.Replace:
                Helpers.AppLog.Info($"PresetsPage.ItemsChanged action={e.Action}");
                _ = PersistOrderAsync();
                break;
        }
    }

    private PresetViewModel Bind(Preset preset)
    {
        var vm = new PresetViewModel(preset)
        {
            ApplyCommand = ApplyCommand,
            DeleteCommand = DeleteCommand,
            EditCommand = EditCommand,
            ToggleTrayCommand = ToggleTrayCommand,
            MoveUpCommand = MoveUpCommand,
            MoveDownCommand = MoveDownCommand,
            ShowInTray = preset.ShowInTray
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
