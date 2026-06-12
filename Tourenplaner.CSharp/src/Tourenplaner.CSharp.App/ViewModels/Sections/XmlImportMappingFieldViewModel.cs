using Tourenplaner.CSharp.App.ViewModels;

namespace Tourenplaner.CSharp.App.ViewModels.Sections;

public sealed class XmlImportMappingFieldViewModel : ObservableObject
{
    private string _xmlName;

    public XmlImportMappingFieldViewModel(string programField, string defaultXmlName, string xmlName)
    {
        ProgramField = programField;
        DefaultXmlName = defaultXmlName;
        _xmlName = xmlName;
    }

    public string ProgramField { get; }

    public string DefaultXmlName { get; }

    public string XmlName
    {
        get => _xmlName;
        set => SetProperty(ref _xmlName, value);
    }
}
