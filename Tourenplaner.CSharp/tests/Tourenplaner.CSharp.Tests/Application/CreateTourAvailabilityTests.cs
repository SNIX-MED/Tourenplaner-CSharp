using Tourenplaner.CSharp.App.Views.Dialogs;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Tests.Application;

public sealed class CreateTourAvailabilityTests
{
    [Fact]
    public void WpfDropdownCanSwitchVehiclesAfterAvailabilityBindingsChange()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var first = new TourLookupOption("first", "Erstes Fahrzeug");
                var second = new TourLookupOption("second", "Zweites Fahrzeug");
                var dropdown = new System.Windows.Controls.ComboBox { ItemsSource = new[] { first, second } };
                dropdown.SelectedItem = first;
                first.PropertyChanged += (_, _) => { };
                second.PropertyChanged += (_, _) => { };
                second.IsAvailable = false;
                second.IsAvailable = true;
                dropdown.SelectedItem = second;
                Assert.Same(second, dropdown.SelectedItem);
                dropdown.SelectedItem = first;
                Assert.Same(first, dropdown.SelectedItem);
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "WPF-Auswahlprüfung wurde nicht beendet.");
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [Fact]
    public void ResourceIdentityStaysStableWhenBindingsSubscribeAndAvailabilityChanges()
    {
        var option = new TourLookupOption("vehicle", "Fahrzeug");
        var indexedOptions = new HashSet<TourLookupOption> { option };
        var originalHash = option.GetHashCode();
        var changes = new List<string?>();
        option.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        Assert.Equal(originalHash, option.GetHashCode());
        Assert.Contains(option, indexedOptions);
        option.IsAvailable = false;
        Assert.Equal(originalHash, option.GetHashCode());
        Assert.Contains(option, indexedOptions);
        Assert.Contains(nameof(option.IsAvailable), changes);
    }

    [Fact]
    public void ChangingDateRestoresResourcesAfterAbsenceAndClearsUnavailableAssignments()
    {
        var today = DateTime.Today;
        var date = today.ToString("dd.MM.yyyy");
        var periods = new List<ResourceUnavailabilityPeriod>
        {
            new() { StartDate = date, EndDate = date }
        };
        var model = new CreateTourDialogViewModel(date, "", "08", "00",
            [new("vehicle", "Vehicle", periods)], [new("trailer", "Trailer", periods)],
            [new("employee", "Employee", false, periods)]);

        Assert.Equal(2, model.VehicleOptions.Count);
        Assert.Equal(2, model.TrailerOptions.Count);
        Assert.False(model.Employees.Single().IsAvailable);

        model.DateText = today.AddDays(7).ToString("dd.MM.yyyy");
        model.SelectedVehicle = model.VehicleOptions.Single(x => x.Id == "vehicle");
        model.SelectedTrailer = model.TrailerOptions.Single(x => x.Id == "trailer");
        model.Employees.Single().IsSelected = true;
        Assert.True(model.TryBuildResult(out var result, out var error), error);
        Assert.Equal("vehicle", result.VehicleId);
        Assert.Equal("trailer", result.TrailerId);
        Assert.Equal("employee", Assert.Single(result.EmployeeIds));

        model.SelectedMinute = "05";
        Assert.Equal("vehicle", model.SelectedVehicle?.Id);
        Assert.True(model.Employees.Single().IsSelected);

        model.DateText = date;
        Assert.Equal("", model.SelectedVehicle?.Id);
        Assert.Equal("", model.SelectedTrailer?.Id);
        Assert.False(model.Employees.Single().IsAvailable);
        model.DateText = today.AddDays(7).ToString("dd.MM.yyyy");
        Assert.False(model.Employees.Single().IsSelected);
    }

    [Fact]
    public void ChangingStartTimeRefreshesAvailability()
    {
        var date = DateTime.Today.ToString("dd.MM.yyyy");
        var periods = new List<ResourceUnavailabilityPeriod>
        {
            new() { StartDate = date, EndDate = date, StartTime = "08:00", EndTime = "09:00" }
        };
        var model = new CreateTourDialogViewModel(date, "", "08", "00",
            [new("vehicle", "Vehicle", periods)], [], [new("employee", "Employee", false, periods)]);
        Assert.False(model.Employees.Single().IsAvailable);
        model.SelectedHour = "10";
        Assert.True(model.Employees.Single().IsAvailable);
        Assert.Equal(2, model.VehicleOptions.Count);
        model.SelectedHour = "09";
        Assert.False(model.Employees.Single().IsAvailable);
        model.SelectedMinute = "05";
        Assert.True(model.Employees.Single().IsAvailable);
    }
}
