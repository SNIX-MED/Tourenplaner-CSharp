namespace Tourenplaner.CSharp.App.ViewModels;

public sealed class NavigationItemViewModel
{
    public NavigationItemViewModel(string displayName, object section, string groupName = "", string iconGlyph = "\uE8A5")
    {
        DisplayName = displayName;
        Section = section;
        GroupName = groupName;
        IconGlyph = iconGlyph;
    }

    public string DisplayName { get; }

    public object Section { get; }

    public string GroupName { get; }

    public string IconGlyph { get; }
}
