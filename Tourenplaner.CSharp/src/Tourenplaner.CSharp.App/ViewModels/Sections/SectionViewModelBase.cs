namespace Tourenplaner.CSharp.App.ViewModels.Sections;

public abstract class SectionViewModelBase : ObservableObject
{
    protected SectionViewModelBase(string title, string description)
    {
        Title = title;
        Description = description;
    }

    public string Title { get; }

    public string Description { get; }
}
