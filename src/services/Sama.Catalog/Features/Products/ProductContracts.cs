using FluentValidation;
using Sama.Catalog.Domain;
using Sama.Shared.Kernel.Primitives;

namespace Sama.Catalog.Features.Products;

public sealed record ProductSummary(
    Guid Id,
    string Sku,
    string Name,
    string Slug,
    string? Brand,
    decimal Price,
    string Currency,
    Guid CategoryId,
    string? PrimaryImageUrl);

public sealed record ProductDetail(
    Guid Id,
    string Sku,
    string Name,
    string Slug,
    string Description,
    string? Brand,
    decimal Price,
    string Currency,
    // Serialised via ToString() at the call site, matching Ordering and Payment. Exposing the
    // enum directly would render it camelCase, leaving one service's casing out of step with
    // the rest of the API.
    string Status,
    Guid CategoryId,
    IReadOnlyList<ProductImageDto> Images);

public sealed record ProductImageDto(string Url, string? AltText, int SortOrder);

public sealed record CreateProductRequest(
    string Sku,
    string Name,
    string Slug,
    string Description,
    Guid CategoryId,
    decimal Price,
    string Currency,
    string? Brand,
    IReadOnlyList<ProductImageDto>? Images);

public sealed record UpdateProductRequest(
    string Name,
    string Slug,
    string Description,
    Guid CategoryId,
    string? Brand,
    IReadOnlyList<ProductImageDto>? Images);

public sealed record ChangePriceRequest(decimal Price, string Currency);

/// <summary>
/// Back-office view of a product. Carries the status and audit timestamps the storefront
/// summary deliberately omits.
/// </summary>
public sealed record ManagedProductSummary(
    Guid Id,
    string Sku,
    string Name,
    string Slug,
    string? Brand,
    decimal Price,
    string Currency,
    string Status,
    Guid CategoryId,
    string? PrimaryImageUrl,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

/// <summary>Service-to-service batch price lookup.</summary>
public sealed record ProductLookupRequest(IReadOnlyList<Guid> ProductIds);

public sealed record ProductPricing(
    Guid Id,
    string Sku,
    string Name,
    decimal Price,
    string Currency,
    bool IsPurchasable,
    string? PrimaryImageUrl);

public sealed class CreateProductRequestValidator : AbstractValidator<CreateProductRequest>
{
    public CreateProductRequestValidator()
    {
        RuleFor(r => r.Sku).NotEmpty().MaximumLength(64)
            .Matches("^[A-Za-z0-9._-]+$")
            .WithMessage("A SKU may contain only letters, digits, dot, underscore and hyphen.");

        RuleFor(r => r.Name).NotEmpty().MaximumLength(300);
        RuleFor(r => r.Slug).NotEmpty().MaximumLength(300).ApplySlugPolicy();
        RuleFor(r => r.Description).NotEmpty().MaximumLength(8000);
        RuleFor(r => r.CategoryId).NotEmpty();
        RuleFor(r => r.Brand).MaximumLength(200);

        RuleFor(r => r.Price).GreaterThanOrEqualTo(0);
        RuleFor(r => r.Currency).ApplyCurrencyPolicy();
    }
}

public sealed class UpdateProductRequestValidator : AbstractValidator<UpdateProductRequest>
{
    public UpdateProductRequestValidator()
    {
        RuleFor(r => r.Name).NotEmpty().MaximumLength(300);
        RuleFor(r => r.Slug).NotEmpty().MaximumLength(300).ApplySlugPolicy();
        RuleFor(r => r.Description).NotEmpty().MaximumLength(8000);
        RuleFor(r => r.CategoryId).NotEmpty();
        RuleFor(r => r.Brand).MaximumLength(200);
    }
}

public sealed class ChangePriceRequestValidator : AbstractValidator<ChangePriceRequest>
{
    public ChangePriceRequestValidator()
    {
        RuleFor(r => r.Price).GreaterThanOrEqualTo(0);
        RuleFor(r => r.Currency).ApplyCurrencyPolicy();
    }
}

internal static class CatalogRules
{
    /// <summary>
    /// Slugs appear in URLs and therefore in cache keys. Restricting the alphabet keeps a
    /// slug from carrying path separators or encoded characters that would let two different
    /// products collide on one cache entry.
    /// </summary>
    public static IRuleBuilderOptions<T, string> ApplySlugPolicy<T>(this IRuleBuilder<T, string> rule)
        => rule.Matches("^[a-z0-9]+(?:-[a-z0-9]+)*$")
            .WithMessage("A slug must be lower-case words separated by single hyphens.");

    public static IRuleBuilderOptions<T, string> ApplyCurrencyPolicy<T>(this IRuleBuilder<T, string> rule)
        => rule.NotEmpty()
            .Must(currency => Money.SupportedCurrencies.Contains(currency.ToUpperInvariant()))
            .WithMessage(_ => $"Supported currencies: {string.Join(", ", Money.SupportedCurrencies.Order())}.");
}
