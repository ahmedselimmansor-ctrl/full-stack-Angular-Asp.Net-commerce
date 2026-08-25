using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Sama.Shared.Kernel;
using Sama.Shared.Kernel.Results;

namespace Sama.Basket.Integration;

/// <summary>Catalogue data a basket line needs, as returned by Sama.Catalog.</summary>
public sealed record ProductPricing(
    Guid Id,
    string Sku,
    string Name,
    decimal Price,
    string Currency,
    bool IsPurchasable,
    string? PrimaryImageUrl);

public interface ICatalogClient
{
    Task<Result<IReadOnlyList<ProductPricing>>> LookupAsync(
        IReadOnlyList<Guid> productIds,
        CancellationToken cancellationToken);
}

public sealed class CatalogClient(HttpClient httpClient, ILogger<CatalogClient> logger) : ICatalogClient
{
    public async Task<Result<IReadOnlyList<ProductPricing>>> LookupAsync(
        IReadOnlyList<Guid> productIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(productIds);

        if (productIds.Count == 0)
        {
            return Result.Success<IReadOnlyList<ProductPricing>>([]);
        }

        try
        {
            using var response = await httpClient.PostAsJsonAsync(
                "/api/v1/products/lookup",
                new { productIds },
                SamaJson.Default,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Catalogue lookup failed with {StatusCode} for {Count} products.",
                    (int)response.StatusCode, productIds.Count);

                return Error.Unavailable(
                    "basket.catalog.unavailable",
                    "Product information is temporarily unavailable. Please try again.");
            }

            var pricing = await response.Content.ReadFromJsonAsync<List<ProductPricing>>(
                SamaJson.Default, cancellationToken);

            return Result.Success<IReadOnlyList<ProductPricing>>(pricing ?? []);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // The resilience pipeline has already retried and, if the failure rate warranted
            // it, opened the circuit. Reaching here means the catalogue is genuinely down,
            // which is a 503 for the shopper rather than a 500.
            logger.LogError(ex, "Catalogue is unreachable during a basket operation.");

            return Error.Unavailable(
                "basket.catalog.unavailable",
                "Product information is temporarily unavailable. Please try again.");
        }
    }
}
