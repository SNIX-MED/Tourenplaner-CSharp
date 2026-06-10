using Tourenplaner.CSharp.Infrastructure.Services;

namespace Tourenplaner.CSharp.Tests.Infrastructure;

public class XmlOrderImportServiceTests
{
    [Fact]
    public void LoadOrdersFromFileDetailed_CollectsErrors_AndContinuesWithValidOrders()
    {
        var root = Path.Combine(Path.GetTempPath(), "tourenplaner-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var xmlPath = Path.Combine(root, "orders.xml");

        try
        {
            File.WriteAllText(xmlPath,
                """
                <Orders>
                  <Order>
                    <AuftragsDatum>2026-06-10</AuftragsDatum>
                    <KundeFirma>Ohne Nummer</KundeFirma>
                  </Order>
                  <Order>
                    <AuftragNr>A-200</AuftragNr>
                    <AuftragsDatum>2026-06-10</AuftragsDatum>
                    <KundeFirma>Gueltiger Kunde</KundeFirma>
                  </Order>
                </Orders>
                """);

            var service = new XmlOrderImportService();
            var result = service.LoadOrdersFromFileDetailed(xmlPath);

            Assert.Equal(2, result.TotalOrderElements);
            Assert.Single(result.Orders);
            Assert.Equal("A-200", result.Orders[0].AuftragNr);
            Assert.Single(result.Errors);
            Assert.Contains("AuftragNr fehlt", result.Errors[0], StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
