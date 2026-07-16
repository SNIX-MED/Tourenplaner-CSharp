using Tourenplaner.CSharp.App.Services;
using Tourenplaner.CSharp.App.Views.Dialogs;
using Tourenplaner.CSharp.Application.Common;

namespace Tourenplaner.CSharp.App.ViewModels;

public sealed partial class MainShellViewModel
{
    private async Task InitializeUserSelectionAsync()
    {
        try
        {
            var settingsTask = _appSettingsRepository.LoadAsync();
            var employeesTask = _employeesRepository.LoadAsync();
            var localUserTask = LocalUserSessionService.LoadAsync();
            await Task.WhenAll(settingsTask, employeesTask);

            var settings = await settingsTask;
            var names = (await employeesTask)
                .Where(x => x is not null && x.HasProgramProfile && !string.IsNullOrWhiteSpace(x.DisplayName))
                .Select(x => x.DisplayName.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            var localUserName = (await localUserTask).Trim();
            var preferred = string.IsNullOrWhiteSpace(localUserName)
                ? ResolvePreferredStartupUserName((settings.CurrentUserName ?? string.Empty).Trim(), names)
                : ResolvePreferredUserName(localUserName, names);

            if (string.IsNullOrWhiteSpace(localUserName) && names.Count > 0)
            {
                var prompted = PromptForUserSelection(names, preferred);
                if (!string.IsNullOrWhiteSpace(prompted))
                {
                    preferred = prompted;
                }
            }

            _suppressUserSelectionChange = true;
            AvailableUserNames.Clear();
            foreach (var name in names)
            {
                AvailableUserNames.Add(name);
            }

            SelectedUserName = preferred;
            _suppressUserSelectionChange = false;
            CurrentUserName = preferred;
            await LocalUserSessionService.SaveAsync(preferred);
        }
        catch
        {
            _suppressUserSelectionChange = true;
            AvailableUserNames.Clear();
            AvailableUserNames.Add(CurrentUserName);
            SelectedUserName = CurrentUserName;
            _suppressUserSelectionChange = false;
        }
    }

    private async Task SwitchCurrentUserAsync(string userName)
    {
        var normalized = (userName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized) ||
            string.Equals(CurrentUserName, normalized, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        await LocalUserSessionService.SaveAsync(normalized);
        CurrentUserName = normalized;
        if (!AvailableUserNames.Contains(normalized, StringComparer.OrdinalIgnoreCase))
        {
            if (AvailableUserNames.Count == 0)
            {
                AvailableUserNames.Add(normalized);
            }
            else
            {
                return;
            }
        }

        await _mapSection.RefreshAsync();
        await _startSection.RefreshAsync();
        await _calendarSection.RefreshAsync();
        await _settingsSection.RefreshAsync();
        ApplyToolSettingsFromSettingsSection();
        RebuildSidebarNavigation();
        ToastNotificationService.ShowInfo($"Aktiver Benutzer: {normalized}");
    }

    private void OnDataChanged(object? sender, AppDataChangedEventArgs args)
    {
        if (!args.Kinds.HasFlag(AppDataKind.Employees))
        {
            return;
        }

        ReloadAvailableUsersAsync().Forget();
    }

    private async Task ReloadAvailableUsersAsync()
    {
        try
        {
            var employees = await _employeesRepository.LoadAsync();
            var names = employees
                .Where(x => x is not null && x.HasProgramProfile && !string.IsNullOrWhiteSpace(x.DisplayName))
                .Select(x => x.DisplayName.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            var resolvedCurrent = ResolvePreferredUserName(CurrentUserName, names);
            if (!string.Equals(resolvedCurrent, CurrentUserName, StringComparison.OrdinalIgnoreCase))
            {
                await LocalUserSessionService.SaveAsync(resolvedCurrent);
                CurrentUserName = resolvedCurrent;
                _suppressUserSelectionChange = true;
                SelectedUserName = resolvedCurrent;
                _suppressUserSelectionChange = false;
            }

            _suppressUserSelectionChange = true;
            AvailableUserNames.Clear();
            foreach (var name in names)
            {
                AvailableUserNames.Add(name);
            }

            _suppressUserSelectionChange = false;
        }
        catch
        {
        }
    }

    private static string ResolvePreferredUserName(string preferred, IReadOnlyList<string> employeeNames)
    {
        if (employeeNames.Count == 0)
        {
            return string.IsNullOrWhiteSpace(preferred) ? "default" : preferred;
        }

        var normalizedPreferred = (preferred ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedPreferred))
        {
            return employeeNames[0];
        }

        var exact = employeeNames.FirstOrDefault(x => string.Equals(x, normalizedPreferred, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            return exact;
        }

        var firstNameMatches = employeeNames
            .Where(x =>
            {
                var firstToken = x.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
                return string.Equals(firstToken, normalizedPreferred, StringComparison.OrdinalIgnoreCase);
            })
            .ToList();

        if (firstNameMatches.Count == 1)
        {
            return firstNameMatches[0];
        }

        return employeeNames[0];
    }

    private static string ResolvePreferredStartupUserName(string sharedPreferred, IReadOnlyList<string> employeeNames)
    {
        if (!string.IsNullOrWhiteSpace(sharedPreferred))
        {
            return ResolvePreferredUserName(sharedPreferred, employeeNames);
        }

        var environmentUser = (Environment.UserName ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(environmentUser))
        {
            return ResolvePreferredUserName(environmentUser, employeeNames);
        }

        return ResolvePreferredUserName("default", employeeNames);
    }

    private static string PromptForUserSelection(IReadOnlyList<string> names, string preferred)
    {
        var dialog = new UserSelectionDialogWindow(names, preferred)
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };

        return dialog.ShowDialog() == true
            ? (dialog.SelectedUserName ?? string.Empty).Trim()
            : (preferred ?? string.Empty).Trim();
    }
}
