using System.IO;
using System.Windows;
using Tourenplaner.CSharp.App.ViewModels;
using Tourenplaner.CSharp.Application.Services;
using Tourenplaner.CSharp.Infrastructure.Repositories;

namespace Tourenplaner.CSharp.App;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var dataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Tourenplaner.CSharp",
            "data");

        Directory.CreateDirectory(dataRoot);

        var orderRepository = new JsonOrderRepository(Path.Combine(dataRoot, "orders.json"));
        var tourRepository = new JsonTourRepository(Path.Combine(dataRoot, "tours.json"));
        var employeeRepository = new JsonEmployeeRepository(Path.Combine(dataRoot, "employees.json"));
        var vehicleRepository = new JsonVehicleRepository(Path.Combine(dataRoot, "vehicles.json"));

        var snapshotService = new AppSnapshotService(orderRepository, tourRepository, employeeRepository, vehicleRepository);
        var toursJsonPath = Path.Combine(dataRoot, "tours.json");

        var mainWindow = new MainWindow
        {
            DataContext = new MainShellViewModel(snapshotService, toursJsonPath)
        };

        mainWindow.Show();
    }
}
