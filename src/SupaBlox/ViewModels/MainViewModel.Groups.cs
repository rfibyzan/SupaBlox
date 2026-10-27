using System.Collections.ObjectModel;
using SupaBlox.Core;

namespace SupaBlox.ViewModels;

public sealed class GroupChip : ObservableObject
{
    public GroupChip(string name, string? key, int count, bool custom, MainViewModel vm)
    {
        Name = name; Key = key; Count = count; IsCustom = custom;
        SelectCommand = new RelayCommand(() => vm.GroupFilter = name);
        DeleteCommand = new RelayCommand(() => vm.DeleteGroup(this));
    }

    public string Name { get; }
    /// <summary>null = semua grup, "" = tanpa grup, selain itu nama grup.</summary>
    public string? Key { get; }
    public int Count { get; }
    public bool IsCustom { get; }
    public bool AcceptsDrop => Key != null;
    public RelayCommand SelectCommand { get; }
    public RelayCommand DeleteCommand { get; }

    private bool _active;
    public bool IsActive { get => _active; set => Set(ref _active, value); }
    private bool _dropTarget;
    public bool IsDropTarget { get => _dropTarget; set => Set(ref _dropTarget, value); }
}

public sealed partial class MainViewModel
{
    public const string AllGroupsName = "Semua grup";
    public const string NoGroupName = "Tanpa grup";

    public ObservableCollection<GroupChip> GroupChips { get; } = new();

    private bool _isNewGroupOpen;
    public bool IsNewGroupOpen { get => _isNewGroupOpen; set => Set(ref _isNewGroupOpen, value); }
    private string _newGroupName = "";
    public string NewGroupName { get => _newGroupName; set => Set(ref _newGroupName, value); }

    private RelayCommand? _openNewGroup, _createGroup, _cancelNewGroup;
    public RelayCommand OpenNewGroupCommand => _openNewGroup ??= new RelayCommand(() => { NewGroupName = ""; IsNewGroupOpen = true; });
    public RelayCommand CreateGroupCommand => _createGroup ??= new RelayCommand(CreateGroup);
    public RelayCommand CancelNewGroupCommand => _cancelNewGroup ??= new RelayCommand(() => IsNewGroupOpen = false);

    public void CreateGroup()
    {
        var name = _newGroupName.Trim();
        if (name.Length == 0) { IsNewGroupOpen = false; return; }
        if (name is AllGroupsName or NoGroupName) { ShowMessage("Nama itu dipakai sistem, pilih nama lain.", true); return; }
        if (!AllGroupNames().Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            _settings.Groups.Add(name);
            QueueSave();
        }
        IsNewGroupOpen = false;
        RebuildGroups();
        ShowMessage($"Grup {name} dibuat. Seret kartu akun ke grup untuk memindahkan.");
    }

    public void DeleteGroup(GroupChip chip)
    {
        if (chip.Key is not { Length: > 0 } g) return;
        int moved = 0;
        foreach (var a in AllAccounts.Where(a => a.Model.Group == g)) { a.Model.Group = ""; a.RefreshTexts(); moved++; }
        _settings.Groups.RemoveAll(x => x == g);
        QueueSave();
        if (moved > 0) SaveAccounts();
        if (_groupFilter == g) _groupFilter = AllGroupsName;
        RebuildGroups();
        RebuildVisibleAccounts();
        ShowMessage(moved > 0 ? $"Grup {g} dihapus, {moved} akun jadi tanpa grup." : $"Grup {g} dihapus.");
    }

    /// <summary>Drag kartu ke chip grup. Kalau kartu yang diseret sedang dicentang, semua yang dicentang ikut pindah.</summary>
    public void MoveAccountsToGroup(string accountId, string groupKey)
    {
        var dragged = AllAccounts.FirstOrDefault(a => a.Model.Id == accountId);
        if (dragged == null) return;
        var list = dragged.IsSelected ? SelectedAccounts : new List<AccountItem> { dragged };
        int n = 0;
        foreach (var a in list)
        {
            if (a.Model.Group == groupKey) continue;
            a.Model.Group = groupKey;
            a.RefreshTexts();
            n++;
        }
        if (n == 0) return;
        if (groupKey.Length > 0 && !_settings.Groups.Contains(groupKey)) { _settings.Groups.Add(groupKey); QueueSave(); }
        SaveAccounts();
        RebuildGroups();
        RebuildVisibleAccounts();
        ShowMessage(n == 1 ? $"{list[0].Name} dipindah ke {(groupKey.Length == 0 ? NoGroupName : groupKey)}." : $"{n} akun dipindah ke {(groupKey.Length == 0 ? NoGroupName : groupKey)}.");
    }

    public void SetDropTarget(GroupChip? chip)
    {
        foreach (var c in GroupChips) c.IsDropTarget = c == chip;
    }

    private List<string> AllGroupNames() =>
        _settings.Groups.Concat(AllAccounts.Select(a => a.Model.Group))
            .Where(g => !string.IsNullOrWhiteSpace(g)).Distinct(StringComparer.Ordinal).OrderBy(g => g, StringComparer.OrdinalIgnoreCase).ToList();

    private void RebuildGroupChips()
    {
        GroupChips.Clear();
        GroupChips.Add(new GroupChip(AllGroupsName, null, AllAccounts.Count, false, this));
        foreach (var g in AllGroupNames())
            GroupChips.Add(new GroupChip(g, g, AllAccounts.Count(a => a.Model.Group == g), true, this));
        int none = AllAccounts.Count(a => string.IsNullOrWhiteSpace(a.Model.Group));
        GroupChips.Add(new GroupChip(NoGroupName, "", none, false, this));
        foreach (var c in GroupChips) c.IsActive = c.Name == (_groupFilter ?? AllGroupsName);
    }
}
