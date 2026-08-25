using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Sama.Shared.Kernel;
using Sama.Shared.Kernel.Results;

namespace Sama.Ordering.Integration;

public sealed record BasketLine(Guid ProductId, string Sku, string Name, int Quantity, decimal UnitPrice, string Currency);

public sealed record BasketSnapshot(IReadOnlyList<BasketLine> Items, int TotalQuantity, decimal Subtotal, string Currency);

public sealed record ProductPricing(
    Guid Id, string Sku, string Name, decimal Price, string Currency, bool IsPurchasable, string? PrimaryImageUrl);

public interface IBasketClient
{
    Task<Result<BasketSnapshot>> GetAsync(CancellationToken cancellationToken);
}

public interface ICatalogClient
{
    Task<Result<IReadOnlyList<ProductPricing>>> LookupAsync(IReadOnlyList<Guid> productIds, CancellationToken cancellationToken);
}

public sealed class BasketClient(HttpClient httpClient, ILogger<BasketClient> logger) : IBasketClient
{
    public async Task<Result<BasketSnapshot>> GetAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.GetAsync("/api/v1/basket", cancellationToken);

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                // The caller's own token was forwarded, so this means their session lapsed
                // mid-checkout rather than a misconfiguration on our side.
                return Error.Unauthorized("ordering.basket.unauthorized", "Your session has expired. Sign in and try again.");
            }

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Basket fetch failed with {StatusCode}.", (int)response.StatusCode);
                return Error.Unavailable("ordering.basket.unavailable", "Your basket is temporarily unavailable. Please try again.");
            }

            var snapshot = await response.Content.ReadFromJsonAsync<BasketSnapshot>(SamaJson.Lenient, cancellationToken);

            return snapshot is null
                ? Error.Unavailable("ordering.basket.unavailable", "Your basket could not be read.")
                : Result.Success(snapshot);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogError(ex, "Basket service is unreachable during checkout.");
            return Error.Unavailable("ordering.basket.unavailable", "Your basket is temporarily unavailable. Please try again.");
        }
    }
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
                "/api/v1/products/lookup", new { productIds }, SamaJson.Default, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Catalogue lookup failed with {StatusCode}.", (int)response.StatusCode);
                return Error.Unavailable("ordering.catalog.unavailable", "Pricing is temporarily unavailable. Please try again.");
            }

            var pricing = await response.Content.ReadFromJsonAsync<List<ProductPricing>>(SamaJson.Lenient, cancellationToken);
            return Result.Success<IReadOnlyList<ProductPricing>>(pricing ?? []);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogError(ex, "Catalogue is unreachable during checkout.");
            return Error.Unavailable("ordering.catalog.unavailable", "Pricing is temporarily unavailable. Please try again.");
        }
    }
}
