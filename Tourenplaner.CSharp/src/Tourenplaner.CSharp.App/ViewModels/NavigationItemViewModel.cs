namespace Tourenplaner.CSharp.App.ViewModels;

public sealed class NavigationItemViewModel
{
    public NavigationItemViewModel(string displayName, object section, string groupName = "")
    {
        DisplayName = displayName;
        Section = section;
        GroupName = groupName;
    }

    public string DisplayName { get; }

    public object Section { get; }

    public string GroupName { get; }
}
