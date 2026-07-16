namespace Tourenplaner.CSharp.App.ViewModels.Sections;

public sealed class SettingsCategoryNavigationItem
{
    public SettingsCategoryNavigationItem(string key, string title, string description, string iconGlyph)
    {
        Key = key;
        Title = title;
        Description = description;
        IconGlyph = iconGlyph;
    }

    public string Key { get; }

    public string Title { get; }

    public string Description { get; }

    public string IconGlyph { get; }
}
