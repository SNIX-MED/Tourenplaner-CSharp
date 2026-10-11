using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.Infrastructure.Repositories.Parity;

internal static class EmployeeNormalizer
{
    public static Employee Normalize(Employee source)
    {
        source.Id = string.IsNullOrWhiteSpace(source.Id) ? Guid.NewGuid().ToString() : source.Id.Trim();
        source.DisplayName = (source.DisplayName ?? string.Empty).Trim();
        source.Role = (source.Role ?? string.Empty).Trim();
        source.ShortCode = (source.ShortCode ?? string.Empty).Trim();
        source.Phone = (source.Phone ?? string.Empty).Trim();
        source.WebfleetObjectUid = (source.WebfleetObjectUid ?? string.Empty).Trim();
        source.WebfleetObjectNumber = (source.WebfleetObjectNumber ?? string.Empty).Trim();
        source.WebfleetDriverUid = (source.WebfleetDriverUid ?? string.Empty).Trim();
        source.WebfleetDriverNumber = (source.WebfleetDriverNumber ?? string.Empty).Trim();
        source.WebfleetDriverName = (source.WebfleetDriverName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(source.ShortCode) && !string.IsNullOrWhiteSpace(source.Role))
        {
            source.ShortCode = source.Role;
        }

        source.UnavailabilityPeriods = ResourceUnavailabilityNormalizer.NormalizePeriods(source.UnavailabilityPeriods);
        return source;
    }
}
