using FluxDisplay.App.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace FluxDisplay.App.Views;

public sealed partial class PresetsPage : Page
{
    public PresetsPage() => InitializeComponent();

    private void PresetList_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        // Native reorder already moved Items; persist the new order so the
        // tray (which follows list order) and restarts pick it up. The save
        // is idempotent, so cancelled drags are harmless.
        try
        {
            if (DataContext is PresetsPageViewModel vm)
            {
                // Diagnostic: order persist is driven by Items.CollectionChanged;
                // this only records whether the ListView event fired at all.
                Helpers.AppLog.Info($"PresetsPage.DragCompleted drop={args.DropResult} items={string.Join(",", vm.Items.Select(i => i.Name))}");
                _ = vm.PersistOrderAsync();
            }
            else
            {
                Helpers.AppLog.Info("PresetsPage.DragCompleted: no viewmodel");
            }
        }
        catch (Exception ex)
        {
            Helpers.AppLog.Error(ex, "PresetsPage.DragCompleted");
        }
    }
}
