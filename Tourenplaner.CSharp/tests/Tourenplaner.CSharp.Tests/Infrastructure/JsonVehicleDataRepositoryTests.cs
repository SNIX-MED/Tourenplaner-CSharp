using Tourenplaner.CSharp.Domain.Models;
using Tourenplaner.CSharp.Infrastructure.Repositories.Parity;

namespace Tourenplaner.CSharp.Tests.Infrastructure;

public class JsonVehicleDataRepositoryTests
{
    [Fact]
    public async Task SaveAndLoad_NormalizesAndDeduplicatesVehicles()
    {
        var root = CreateTempRoot();
        var file = Path.Combine(root, "vehicles.json");

        try
        {
            var repository = new JsonVehicleDataRepository(file);
            var payload = new VehicleDataRecord
            {
                Vehicles =
                [
                    new Vehicle
                    {
                        Id = "v1",
                        Type = "TRUCK",
                        Name = "  LKW 1 ",
                        LicensePlate = "ag 123",
                        MaxPayloadKg = 1000,
                        Active = true
                    },
                    new Vehicle
                    {
                        Id = "v2",
                        Type = "van",
                        Name = "LKW 1",
                        LicensePlate = "AG123",
                        MaxPayloadKg = 900,
                        Active = false
                    }
                ]
            };

            await repository.SaveAsync(payload);
            var loaded = await repository.LoadAsync();

            Assert.Single(loaded.Vehicles);
            Assert.Equal("truck", loaded.Vehicles[0].Type);
            Assert.Equal("LKW 1", loaded.Vehicles[0].Name);
            Assert.Equal("AG 123", loaded.Vehicles[0].LicensePlate);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task SaveAndLoad_KeepsValidVehicleCombinations()
    {
        var root = CreateTempRoot();
        var file = Path.Combine(root, "vehicles.json");

        try
        {
            var repository = new JsonVehicleDataRepository(file);
            var payload = new VehicleDataRecord
            {
                Vehicles =
                [
                    new Vehicle
                    {
                        Id = "vehicle-1",
                        Name = "LKW 1",
                        LicensePlate = "AG 123",
                        MaxPayloadKg = 1000
                    }
                ],
                Trailers =
                [
                    new TrailerRecord
                    {
                        Id = "trailer-1",
                        Name = "Anhänger 1",
                        LicensePlate = "AG 999",
                        MaxPayloadKg = 600
                    }
                ],
                VehicleCombinations =
                [
                    new VehicleCombinationRecord
                    {
                        Id = "combo-1",
                        VehicleId = "vehicle-1",
                        TrailerId = "trailer-1",
                        VehiclePayloadKg = 750,
                        TrailerLoadKg = 500
                    },
                    new VehicleCombinationRecord
                    {
                        Id = "combo-2",
                        VehicleId = "vehicle-1",
                        TrailerId = "trailer-1",
                        VehiclePayloadKg = 800,
                        TrailerLoadKg = 550
                    },
                    new VehicleCombinationRecord
                    {
                        Id = "combo-3",
                        VehicleId = "vehicle-404",
                        TrailerId = "trailer-1",
                        VehiclePayloadKg = 700,
                        TrailerLoadKg = 450
                    }
                ]
            };

            await repository.SaveAsync(payload);
            var loaded = await repository.LoadAsync();

            Assert.Single(loaded.VehicleCombinations);
            Assert.Equal("vehicle-1", loaded.VehicleCombinations[0].VehicleId);
            Assert.Equal("trailer-1", loaded.VehicleCombinations[0].TrailerId);
            Assert.Equal(750, loaded.VehicleCombinations[0].VehiclePayloadKg);
            Assert.Equal(500, loaded.VehicleCombinations[0].TrailerLoadKg);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task SaveAsync_ThrowsOnNegativePayload()
    {
        var root = CreateTempRoot();
        var file = Path.Combine(root, "vehicles.json");

        try
        {
            var repository = new JsonVehicleDataRepository(file);
            var payload = new VehicleDataRecord
            {
                Vehicles =
                [
                    new Vehicle
                    {
                        Name = "Bad",
                        MaxPayloadKg = -1
                    }
                ]
            };

            await Assert.ThrowsAsync<ArgumentException>(() => repository.SaveAsync(payload));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task SaveAsync_ThrowsOnNegativeCombinationWeights()
    {
        var root = CreateTempRoot();
        var file = Path.Combine(root, "vehicles.json");

        try
        {
            var repository = new JsonVehicleDataRepository(file);
            var payload = new VehicleDataRecord
            {
                Vehicles =
                [
                    new Vehicle
                    {
                        Id = "vehicle-1",
                        Name = "LKW 1"
                    }
                ],
                Trailers =
                [
                    new TrailerRecord
                    {
                        Id = "trailer-1",
                        Name = "Anhänger 1"
                    }
                ],
                VehicleCombinations =
                [
                    new VehicleCombinationRecord
                    {
                        VehicleId = "vehicle-1",
                        TrailerId = "trailer-1",
                        VehiclePayloadKg = -1
                    }
                ]
            };

            await Assert.ThrowsAsync<ArgumentException>(() => repository.SaveAsync(payload));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "tourenplaner-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
