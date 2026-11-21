using System.Collections.ObjectModel;
using System.Windows;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.App.Views.Dialogs;

public partial class AdditionalMaterialSelectionDialogWindow : Window
{
    private readonly Func<Task<IReadOnlyList<AdditionalMaterialGroup>?>> _manageGroupsAsync;

    public AdditionalMaterialSelectionDialogWindow(IEnumerable<AdditionalMaterialGroup> groups,
        IEnumerable<TourAdditionalMaterial> selected,
        Func<Task<IReadOnlyList<AdditionalMaterialGroup>?>> manageGroupsAsync)
    {
        InitializeComponent();
        _manageGroupsAsync = manageGroupsAsync;
        var selectedIds = selected.Select(item => item.GroupId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Options = new ObservableCollection<SelectableAdditionalMaterial>(groups.Select(group =>
            new SelectableAdditionalMaterial(group, selectedIds.Contains(group.Id))));
        DataContext = this;
    }

    public ObservableCollection<SelectableAdditionalMaterial> Options { get; }
    public IReadOnlyList<TourAdditionalMaterial> Result { get; private set; } = [];

    private async void OnManage(object sender, RoutedEventArgs e)
    {
        var selectedIds = Options.Where(option => option.IsSelected).Select(option => option.CreateSnapshot().GroupId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var groups = await _manageGroupsAsync();
        if (groups is null) return;
        Options.Clear();
        foreach (var group in groups)
            Options.Add(new SelectableAdditionalMaterial(group, selectedIds.Contains(group.Id)));
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        Result = Options.Where(option => option.IsSelected).Select(option => option.CreateSnapshot()).ToList();
        DialogResult = true;
    }
}
