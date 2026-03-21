namespace Tourenplaner.CSharp.App.ViewModels;

public sealed class NavigationItemViewModel
{
    public NavigationItemViewModel(string displayName, object section)
    {
        DisplayName = displayName;
        Section = section;
    }

    public string DisplayName { get; }

    public object Section { get; }
}
