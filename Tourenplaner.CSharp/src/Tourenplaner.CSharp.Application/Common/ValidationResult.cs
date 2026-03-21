namespace Tourenplaner.CSharp.Application.Common;

public sealed class ValidationResult
{
    public ValidationResult(IReadOnlyList<string> errors)
    {
        Errors = errors;
    }

    public IReadOnlyList<string> Errors { get; }

    public bool IsValid => Errors.Count == 0;
}
