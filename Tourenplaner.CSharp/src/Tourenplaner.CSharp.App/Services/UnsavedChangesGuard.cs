using System.Reflection;
using System.Text.Json;
using System.Windows;

namespace Tourenplaner.CSharp.App.Services;

public sealed class UnsavedChangesTracker(Func<string> capture)
{
    private string _savedState = capture();
    public bool HasChanges => !string.Equals(_savedState, capture(), StringComparison.Ordinal);
    public void AcceptChanges() => _savedState = capture();
}

public static class UnsavedChangesGuard
{
    private static readonly Dictionary<Window, Func<bool>> OpenEditors = new();
    public static bool HasOpenUnsavedEditors => OpenEditors.Values.Any(hasChanges => hasChanges());
    // Read raw editable values, including incomplete/invalid input. Validation must
    // not make a partially filled form appear saved. Ignore navigation-only fields.
    public static string Capture(object editor, object? children = null, params string[] ignoredProperties)
    {
        var values = editor.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(p => p.SetMethod?.IsPublic == true && p.GetIndexParameters().Length == 0 &&
                        !ignoredProperties.Contains(p.Name, StringComparer.Ordinal))
            .Where(p => IsScalar(Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType))
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .ToDictionary(p => p.Name, p => p.GetValue(editor));
        return JsonSerializer.Serialize(new { Values = values, Children = children });
    }

    private static bool IsScalar(Type type) => type.IsPrimitive || type.IsEnum ||
        type == typeof(string) || type == typeof(decimal) || type == typeof(DateTime) ||
        type == typeof(DateOnly) || type == typeof(TimeOnly) || type == typeof(Guid);

    public static void Attach(Window window, Func<string> capture, Func<bool> isCompleted)
    {
        var tracker = new UnsavedChangesTracker(capture);
        OpenEditors[window] = () => !isCompleted() && tracker.HasChanges;
        window.Closed += (_, _) => OpenEditors.Remove(window);
        window.Closing += (_, e) =>
        {
            if (e.Cancel || isCompleted() || !tracker.HasChanges) return;
            e.Cancel = !ConfirmDiscard(window, "Dieses Fenster enthält ungespeicherte Änderungen. Trotzdem schliessen und die Änderungen verwerfen?");
        };
    }

    public static bool ConfirmDiscard(Window owner, string message) =>
        AppMessageBox.ShowConfirmation(owner, message, "Ungespeicherte Änderungen",
            "Abbrechen", "Trotzdem schliessen") == MessageBoxResult.Yes;
}
