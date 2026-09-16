using System.Windows.Input;
using Tourenplaner.CSharp.App.ViewModels.Commands;

namespace Tourenplaner.CSharp.App.ViewModels.Sections;

public sealed class XmlImportPinIssueListItemViewModel
{
    private XmlImportPinIssueListItemViewModel(
        string issueLabel,
        string issueBackground,
        string issueBorder,
        string issueForeground,
        string orderId,
        string customerName,
        string addressLine,
        string issueSummary,
        string matchSummary,
        Func<string, Task>? editOrderAsync,
        Func<string, Task>? recheckOrderAsync)
    {
        IssueLabel = issueLabel;
        IssueBackground = issueBackground;
        IssueBorder = issueBorder;
        IssueForeground = issueForeground;
        OrderId = orderId;
        CustomerName = customerName;
        AddressLine = addressLine;
        IssueSummary = issueSummary;
        MatchSummary = matchSummary;
        EditCommand = new AsyncCommand(
            () => editOrderAsync?.Invoke(OrderId) ?? Task.CompletedTask,
            () => editOrderAsync is not null && !string.IsNullOrWhiteSpace(OrderId));
        RecheckCommand = new AsyncCommand(
            () => recheckOrderAsync?.Invoke(OrderId) ?? Task.CompletedTask,
            () => recheckOrderAsync is not null && !string.IsNullOrWhiteSpace(OrderId));
    }

    public string IssueLabel { get; }
    public string IssueBackground { get; }
    public string IssueBorder { get; }
    public string IssueForeground { get; }
    public string OrderId { get; }
    public string CustomerName { get; }
    public string AddressLine { get; }
    public string IssueSummary { get; }
    public string MatchSummary { get; }
    public ICommand EditCommand { get; }
    public ICommand RecheckCommand { get; }

    public string CustomerLine => string.IsNullOrWhiteSpace(CustomerName) ? "(ohne Kundenname)" : CustomerName;
    public string AddressDisplayLine => string.IsNullOrWhiteSpace(AddressLine) ? "(ohne Lieferadresse)" : AddressLine;

    public static XmlImportPinIssueListItemViewModel CreateMissing(
        string orderId,
        string customerName,
        string addressLine,
        string failureSummary,
        Func<string, Task>? editOrderAsync,
        Func<string, Task>? recheckOrderAsync)
    {
        return new XmlImportPinIssueListItemViewModel(
            "Keine Zuordnung",
            "#FEF2F2",
            "#FECACA",
            "#B91C1C",
            orderId,
            customerName,
            addressLine,
            "Der Pin konnte keiner konkreten Adresse zugeordnet werden.",
            failureSummary,
            editOrderAsync,
            recheckOrderAsync);
    }

    public static XmlImportPinIssueListItemViewModel CreateApproximate(
        string orderId,
        string customerName,
        string addressLine,
        string matchType,
        string? entityType,
        Func<string, Task>? editOrderAsync,
        Func<string, Task>? recheckOrderAsync)
    {
        var matchSummary = string.IsNullOrWhiteSpace(entityType)
            ? matchType
            : $"{matchType} / {entityType}";

        return new XmlImportPinIssueListItemViewModel(
            "Ungefaehr",
            "#FFFBEB",
            "#FDE68A",
            "#B45309",
            orderId,
            customerName,
            addressLine,
            "Der Pin wurde nur ungefaehr aufgeloest und sollte manuell geprueft werden.",
            string.IsNullOrWhiteSpace(matchSummary) ? "Unscharfer Treffer" : matchSummary,
            editOrderAsync,
            recheckOrderAsync);
    }
}
