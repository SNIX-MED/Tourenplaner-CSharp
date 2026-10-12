using Tourenplaner.CSharp.Domain.Models;
using System.Text.RegularExpressions;

namespace Tourenplaner.CSharp.Application.Services;

public sealed class OrderDeliveryService
{
    public const int MaximumDeliveryCount = 2;

    private static readonly Regex DeliveryNotePrefixPattern = new(
        @"^Lieferung\s+\d+\s*/\s*\d+\s*:\s*",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    public bool Normalize(Order order)
    {
        ArgumentNullException.ThrowIfNull(order);
        var changed = false;
        order.Products ??= [];
        order.Deliveries ??= [];

        foreach (var product in order.Products)
        {
            if (string.IsNullOrWhiteSpace(product.PositionId))
            {
                product.PositionId = Guid.NewGuid().ToString("N");
                changed = true;
            }
        }

        var nextSequence = 1;
        foreach (var delivery in order.Deliveries)
        {
            delivery.Lines ??= [];
            if (string.Equals(delivery.Notes?.Trim(), "Automatisch erzeugte Restlieferung", StringComparison.Ordinal))
            {
                delivery.Notes = string.Empty;
                changed = true;
            }
            if (string.IsNullOrWhiteSpace(delivery.Id))
            {
                delivery.Id = Guid.NewGuid().ToString("N");
                changed = true;
            }
            if (delivery.SequenceNumber <= 0)
            {
                delivery.SequenceNumber = nextSequence;
                changed = true;
            }
            nextSequence = Math.Max(nextSequence, delivery.SequenceNumber + 1);
        }

        // Existing installations assigned the whole order directly to one tour.
        if (order.Deliveries.Count == 0 && !string.IsNullOrWhiteSpace(order.AssignedTourId) && order.Products.Count > 0)
        {
            order.Deliveries.Add(new OrderDelivery
            {
                Id = Guid.NewGuid().ToString("N"),
                SequenceNumber = 1,
                Status = OrderDeliveryStatus.Planned,
                PlannedDeliveryDate = order.DeliveryDate ?? order.ScheduledDate,
                AssignedTourId = order.AssignedTourId,
                Lines = order.Products
                    .Where(x => !string.IsNullOrWhiteSpace(x.Name))
                    .Select(x => new OrderDeliveryLine
                    {
                        OrderProductPositionId = x.PositionId,
                        Quantity = Math.Max(0, OrderProductInfo.NormalizeQuantity(x.Quantity))
                    })
                    .Where(x => x.Quantity > 0)
                    .ToList()
            });
            changed = true;
        }

        return changed;
    }

    public IReadOnlyDictionary<string, int> GetRemainingQuantities(Order order)
    {
        Normalize(order);
        var allocated = order.Deliveries
            .Where(x => x.Status != OrderDeliveryStatus.Cancelled)
            .SelectMany(x => x.Lines ?? [])
            .GroupBy(x => x.OrderProductPositionId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.Sum(y => Math.Max(0, y.Quantity)), StringComparer.OrdinalIgnoreCase);

        return order.Products.ToDictionary(
            x => x.PositionId,
            x => Math.Max(0, OrderProductInfo.NormalizeQuantity(x.Quantity) - allocated.GetValueOrDefault(x.PositionId)),
            StringComparer.OrdinalIgnoreCase);
    }

    public OrderDelivery Create(Order order, IEnumerable<OrderDeliveryLine> lines, DateOnly? plannedDate, string? notes = null)
    {
        Normalize(order);
        if (order.Deliveries.Count >= MaximumDeliveryCount)
        {
            throw new InvalidOperationException("Ein Auftrag kann maximal zwei Lieferungen enthalten.");
        }

        var requested = (lines ?? [])
            .Where(x => x.Quantity > 0 && !string.IsNullOrWhiteSpace(x.OrderProductPositionId))
            .GroupBy(x => x.OrderProductPositionId, StringComparer.OrdinalIgnoreCase)
            .Select(x => new OrderDeliveryLine { OrderProductPositionId = x.Key, Quantity = x.Sum(y => y.Quantity) })
            .ToList();
        if (requested.Count == 0)
        {
            throw new InvalidOperationException("Eine Lieferung muss mindestens eine Produktmenge enthalten.");
        }

        var remaining = GetRemainingQuantities(order);
        foreach (var line in requested)
        {
            if (!remaining.TryGetValue(line.OrderProductPositionId, out var available))
            {
                throw new InvalidOperationException("Die referenzierte Auftragsposition existiert nicht.");
            }
            if (line.Quantity > available)
            {
                throw new InvalidOperationException($"Die Liefermenge {line.Quantity} überschreitet die Restmenge {available}.");
            }
        }

        var delivery = new OrderDelivery
        {
            Id = Guid.NewGuid().ToString("N"),
            SequenceNumber = order.Deliveries.Select(x => x.SequenceNumber).DefaultIfEmpty(0).Max() + 1,
            Status = OrderDeliveryStatus.ReadyForPlanning,
            PlannedDeliveryDate = plannedDate,
            Notes = (notes ?? string.Empty).Trim(),
            Lines = requested
        };
        order.Deliveries.Add(delivery);
        return delivery;
    }

    public void Cancel(Order order, string deliveryId)
    {
        var delivery = Find(order, deliveryId);
        if (delivery.Status == OrderDeliveryStatus.Delivered)
        {
            throw new InvalidOperationException("Eine bereits gelieferte Lieferung kann nicht storniert werden.");
        }
        delivery.Status = OrderDeliveryStatus.Cancelled;
        delivery.AssignedTourId = null;
    }

    public OrderDelivery? EnsureRemainderDelivery(Order order)
    {
        Normalize(order);
        if (order.Deliveries.Count == 0)
        {
            return null;
        }

        var lines = GetRemainingQuantities(order)
            .Where(x => x.Value > 0)
            .Select(x => new OrderDeliveryLine { OrderProductPositionId = x.Key, Quantity = x.Value })
            .ToList();
        if (lines.Count == 0)
        {
            return null;
        }

        if (order.Deliveries.Count >= MaximumDeliveryCount)
        {
            throw new InvalidOperationException("Die Restmenge kann nicht angelegt werden, weil bereits zwei Lieferungen vorhanden sind.");
        }

        var delivery = new OrderDelivery
        {
            Id = Guid.NewGuid().ToString("N"),
            SequenceNumber = order.Deliveries.Select(x => x.SequenceNumber).DefaultIfEmpty(0).Max() + 1,
            Status = OrderDeliveryStatus.ReadyForPlanning,
            Lines = lines,
            Notes = string.Empty
        };
        order.Deliveries.Add(delivery);
        return delivery;
    }

    public bool DistributeOrderNotesToDeliveries(Order order)
    {
        Normalize(order);
        var sourceNotes = (order.Notes ?? string.Empty).Trim();
        if (order.Deliveries.Count < 2)
        {
            if (order.Deliveries.Count == 0)
            {
                return false;
            }

            // A single internal delivery can exist for legacy whole-order tour assignments.
            // It is not a partial delivery and must keep using the regular order note.
            var delivery = order.Deliveries[0];
            var deliveryNotes = (delivery.Notes ?? string.Empty).Trim();
            var restoredNotes = DeliveryNotePrefixPattern.Replace(deliveryNotes, string.Empty).Trim();
            var singleDeliveryChanged = false;
            if (string.IsNullOrWhiteSpace(sourceNotes) && !string.IsNullOrWhiteSpace(restoredNotes))
            {
                order.Notes = restoredNotes;
                singleDeliveryChanged = true;
            }
            if (!string.IsNullOrWhiteSpace(deliveryNotes))
            {
                delivery.Notes = string.Empty;
                singleDeliveryChanged = true;
            }
            return singleDeliveryChanged;
        }

        var changed = false;
        var activeDeliveries = order.Deliveries
            .OrderBy(x => x.SequenceNumber)
            .ToList();
        var total = activeDeliveries.Count;
        for (var index = 0; index < activeDeliveries.Count; index++)
        {
            var delivery = activeDeliveries[index];
            var existingContent = DeliveryNotePrefixPattern.Replace((delivery.Notes ?? string.Empty).Trim(), string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(existingContent))
            {
                existingContent = sourceNotes;
            }
            var nextNotes = $"Lieferung {index + 1}/{total}:";
            if (!string.IsNullOrWhiteSpace(existingContent))
            {
                nextNotes += $" {existingContent}";
            }
            if (!string.Equals(delivery.Notes, nextNotes, StringComparison.Ordinal))
            {
                delivery.Notes = nextNotes;
                changed = true;
            }
        }
        if (!string.IsNullOrWhiteSpace(order.Notes))
        {
            order.Notes = string.Empty;
            changed = true;
        }
        return changed;
    }

    public void MarkDelivered(Order order, string deliveryId, DateTimeOffset deliveredAt)
    {
        var delivery = Find(order, deliveryId);
        if (delivery.Status == OrderDeliveryStatus.Cancelled)
        {
            throw new InvalidOperationException("Eine stornierte Lieferung kann nicht geliefert werden.");
        }
        delivery.Status = OrderDeliveryStatus.Delivered;
        delivery.DeliveredAt = deliveredAt;
        delivery.AssignedTourId = null;
        order.IsArchived = (order.Deliveries ?? [])
            .Where(x => x.Status != OrderDeliveryStatus.Cancelled)
            .All(x => x.Status == OrderDeliveryStatus.Delivered);
    }

    public double ResolveWeightKg(Order order, OrderDelivery delivery)
    {
        Normalize(order);
        var products = order.Products.ToDictionary(x => x.PositionId, StringComparer.OrdinalIgnoreCase);
        return (delivery.Lines ?? []).Sum(line =>
            products.TryGetValue(line.OrderProductPositionId, out var product)
                ? Math.Max(0d, product.UnitWeightKg) * Math.Max(0, line.Quantity)
                : 0d);
    }

    public bool TryValidate(Order order, out string error)
    {
        Normalize(order);
        if (order.Deliveries.Count > MaximumDeliveryCount)
        {
            error = "Ein Auftrag kann maximal zwei Lieferungen enthalten.";
            return false;
        }

        var products = order.Products.ToDictionary(x => x.PositionId, StringComparer.OrdinalIgnoreCase);
        var allocations = order.Deliveries
            .Where(x => x.Status != OrderDeliveryStatus.Cancelled)
            .SelectMany(x => x.Lines ?? [])
            .GroupBy(x => x.OrderProductPositionId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.Sum(y => Math.Max(0, y.Quantity)), StringComparer.OrdinalIgnoreCase);
        foreach (var allocation in allocations)
        {
            if (!products.TryGetValue(allocation.Key, out var product))
            {
                error = "Eine Lieferung verweist auf eine nicht mehr vorhandene Produktposition.";
                return false;
            }
            var allocated = allocation.Value;
            if (allocated > Math.Max(0, OrderProductInfo.NormalizeQuantity(product.Quantity)))
            {
                error = $"Für «{product.Name}» sind bereits {allocated} Stück Lieferungen zugeteilt. Die Auftragsmenge darf nicht darunter liegen.";
                return false;
            }
        }

        if (order.Deliveries.Count >= 2)
        {
            foreach (var product in order.Products.Where(x => !string.IsNullOrWhiteSpace(x.Name)))
            {
                var ordered = Math.Max(0, OrderProductInfo.NormalizeQuantity(product.Quantity));
                var allocated = allocations.GetValueOrDefault(product.PositionId);
                if (allocated != ordered)
                {
                    error = $"Für «{product.Name}» müssen genau {ordered} Stück auf die beiden Lieferungen verteilt sein. Aktuell zugeteilt: {allocated}.";
                    return false;
                }
            }
        }

        error = string.Empty;
        return true;
    }

    public bool TryValidateUpdate(Order existing, Order updated, out string error)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(updated);
        if (!TryValidate(updated, out error))
        {
            return false;
        }

        var updatedProducts = updated.Products.ToDictionary(x => x.PositionId, StringComparer.OrdinalIgnoreCase);
        var existingProducts = existing.Products.ToDictionary(x => x.PositionId, StringComparer.OrdinalIgnoreCase);
        var deliveredPositionIds = (existing.Deliveries ?? [])
            .Where(x => x.Status == OrderDeliveryStatus.Delivered)
            .SelectMany(x => x.Lines ?? [])
            .Select(x => x.OrderProductPositionId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var positionId in deliveredPositionIds)
        {
            if (!existingProducts.TryGetValue(positionId, out var previousProduct) ||
                !updatedProducts.TryGetValue(positionId, out var updatedProduct))
            {
                error = "Ein bereits geliefertes Produkt darf nicht gelöscht werden.";
                return false;
            }

            if (OrderProductInfo.NormalizeQuantity(previousProduct.Quantity) !=
                OrderProductInfo.NormalizeQuantity(updatedProduct.Quantity))
            {
                error = $"Die Menge des bereits gelieferten Produkts «{previousProduct.Name}» darf nicht verändert werden.";
                return false;
            }
        }

        foreach (var previousDelivery in (existing.Deliveries ?? []).Where(x => x.Status == OrderDeliveryStatus.Delivered))
        {
            var updatedDelivery = (updated.Deliveries ?? []).FirstOrDefault(x =>
                string.Equals(x.Id, previousDelivery.Id, StringComparison.OrdinalIgnoreCase));
            if (updatedDelivery is null || updatedDelivery.Status != OrderDeliveryStatus.Delivered ||
                !DeliveryLinesEqual(previousDelivery.Lines, updatedDelivery.Lines))
            {
                error = $"Die bereits gelieferte Lieferung {previousDelivery.SequenceNumber} darf nicht verändert werden.";
                return false;
            }
        }

        error = string.Empty;
        return true;
    }

    public bool TryReconcileImportedProducts(
        Order existing,
        IReadOnlyList<OrderProductInfo> importedProducts,
        out string error,
        bool applyChanges = true)
    {
        ArgumentNullException.ThrowIfNull(existing);
        importedProducts ??= [];
        Normalize(existing);

        var deliveries = existing.Deliveries
            .Where(x => x.Status != OrderDeliveryStatus.Cancelled)
            .OrderBy(x => x.SequenceNumber)
            .ToList();
        if (deliveries.Count < 2)
        {
            var candidate = BuildValidationCandidate(existing, importedProducts, existing.Deliveries);
            return TryValidateUpdate(existing, candidate, out error);
        }

        var remainderDelivery = deliveries[^1];
        var fixedAllocations = deliveries
            .Where(x => !ReferenceEquals(x, remainderDelivery))
            .SelectMany(x => x.Lines ?? [])
            .GroupBy(x => x.OrderProductPositionId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.Sum(y => Math.Max(0, y.Quantity)), StringComparer.OrdinalIgnoreCase);
        var importedById = importedProducts.ToDictionary(x => x.PositionId, StringComparer.OrdinalIgnoreCase);

        foreach (var fixedAllocation in fixedAllocations.Where(x => x.Value > 0))
        {
            if (!importedById.ContainsKey(fixedAllocation.Key))
            {
                error = "Der XML-Import würde ein bereits einer Lieferung zugeteiltes Produkt entfernen.";
                return false;
            }
        }

        var nextRemainderLines = new List<OrderDeliveryLine>();
        foreach (var product in importedProducts.Where(x => !string.IsNullOrWhiteSpace(x.Name)))
        {
            var importedQuantity = Math.Max(0, OrderProductInfo.NormalizeQuantity(product.Quantity));
            var fixedQuantity = fixedAllocations.GetValueOrDefault(product.PositionId);
            if (fixedQuantity > importedQuantity)
            {
                error = $"Die XML-Menge für «{product.Name}» ({importedQuantity}) liegt unter der bereits Lieferungen zugeteilten Menge ({fixedQuantity}).";
                return false;
            }

            var remainderQuantity = importedQuantity - fixedQuantity;
            if (remainderQuantity > 0)
            {
                nextRemainderLines.Add(new OrderDeliveryLine
                {
                    OrderProductPositionId = product.PositionId,
                    Quantity = remainderQuantity
                });
            }
        }

        var candidateDeliveries = existing.Deliveries.Select(CloneDelivery).ToList();
        var candidateRemainder = candidateDeliveries.First(x =>
            string.Equals(x.Id, remainderDelivery.Id, StringComparison.OrdinalIgnoreCase));
        candidateRemainder.Lines = nextRemainderLines;
        var validationCandidate = BuildValidationCandidate(existing, importedProducts, candidateDeliveries);
        if (!TryValidateUpdate(existing, validationCandidate, out error))
        {
            return false;
        }

        if (applyChanges)
        {
            remainderDelivery.Lines = nextRemainderLines;
        }
        error = string.Empty;
        return true;
    }

    public bool SynchronizeTourAssignments(IEnumerable<Order> orders, IEnumerable<TourRecord> tours)
    {
        var activeTours = (tours ?? []).Where(x => x is not null && !x.IsArchived).ToList();
        var changed = false;
        foreach (var order in orders ?? [])
        {
            Normalize(order);
            var orderId = (order.Id ?? string.Empty).Trim();
            var tourIdsByDeliveryId = activeTours
                .SelectMany(tour => (tour.Stops ?? [])
                    .Where(stop => string.Equals((stop.Auftragsnummer ?? string.Empty).Trim(), orderId, StringComparison.OrdinalIgnoreCase) &&
                                   !string.IsNullOrWhiteSpace(stop.DeliveryId))
                    .Select(stop => new { DeliveryId = stop.DeliveryId.Trim(), TourId = tour.Id.ToString() }))
                .GroupBy(x => x.DeliveryId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.Select(y => y.TourId).Distinct(StringComparer.OrdinalIgnoreCase).ToList(), StringComparer.OrdinalIgnoreCase);

            foreach (var delivery in order.Deliveries ?? [])
            {
                if (delivery.Status is OrderDeliveryStatus.Cancelled or OrderDeliveryStatus.Delivered)
                {
                    continue;
                }

                tourIdsByDeliveryId.TryGetValue(delivery.Id, out var matchingTourIds);
                var currentTourId = (delivery.AssignedTourId ?? string.Empty).Trim();
                var nextTourId = matchingTourIds?.FirstOrDefault(x => string.Equals(x, currentTourId, StringComparison.OrdinalIgnoreCase))
                                 ?? matchingTourIds?.FirstOrDefault()
                                 ?? string.Empty;
                if (!string.Equals(currentTourId, nextTourId, StringComparison.OrdinalIgnoreCase))
                {
                    delivery.AssignedTourId = string.IsNullOrWhiteSpace(nextTourId) ? null : nextTourId;
                    changed = true;
                }

                var nextStatus = string.IsNullOrWhiteSpace(nextTourId)
                    ? OrderDeliveryStatus.ReadyForPlanning
                    : OrderDeliveryStatus.Planned;
                if (delivery.Status != nextStatus)
                {
                    delivery.Status = nextStatus;
                    changed = true;
                }
            }

            var activeDeliveries = (order.Deliveries ?? [])
                .Where(x => x.Status is not OrderDeliveryStatus.Cancelled and not OrderDeliveryStatus.Delivered)
                .ToList();
            var assignedTourIds = activeDeliveries
                .Select(x => (x.AssignedTourId ?? string.Empty).Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var nextOrderTourId = assignedTourIds.Count == 1 && !string.IsNullOrWhiteSpace(assignedTourIds[0])
                ? assignedTourIds[0]
                : string.Empty;
            if (!string.Equals((order.AssignedTourId ?? string.Empty).Trim(), nextOrderTourId, StringComparison.OrdinalIgnoreCase))
            {
                order.AssignedTourId = nextOrderTourId;
                changed = true;
            }
        }

        return changed;
    }

    private static OrderDelivery Find(Order order, string deliveryId) =>
        (order.Deliveries ?? []).FirstOrDefault(x => string.Equals(x.Id, deliveryId, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException("Die Lieferung wurde nicht gefunden.");

    private static bool DeliveryLinesEqual(IEnumerable<OrderDeliveryLine>? left, IEnumerable<OrderDeliveryLine>? right)
    {
        static Dictionary<string, int> NormalizeLines(IEnumerable<OrderDeliveryLine>? lines) =>
            (lines ?? [])
            .GroupBy(x => x.OrderProductPositionId ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.Sum(y => Math.Max(0, y.Quantity)), StringComparer.OrdinalIgnoreCase);

        var leftLines = NormalizeLines(left);
        var rightLines = NormalizeLines(right);
        return leftLines.Count == rightLines.Count && leftLines.All(x => rightLines.GetValueOrDefault(x.Key) == x.Value);
    }

    private static Order BuildValidationCandidate(
        Order source,
        IReadOnlyList<OrderProductInfo> products,
        IEnumerable<OrderDelivery> deliveries) => new()
        {
            Id = source.Id,
            Products = products.ToList(),
            Deliveries = deliveries.Select(CloneDelivery).ToList()
        };

    private static OrderDelivery CloneDelivery(OrderDelivery source) => new()
    {
        Id = source.Id,
        SequenceNumber = source.SequenceNumber,
        Status = source.Status,
        PlannedDeliveryDate = source.PlannedDeliveryDate,
        DeliveredAt = source.DeliveredAt,
        AssignedTourId = source.AssignedTourId,
        Notes = source.Notes,
        Lines = (source.Lines ?? []).Select(x => new OrderDeliveryLine
        {
            OrderProductPositionId = x.OrderProductPositionId,
            Quantity = x.Quantity
        }).ToList()
    };
}
