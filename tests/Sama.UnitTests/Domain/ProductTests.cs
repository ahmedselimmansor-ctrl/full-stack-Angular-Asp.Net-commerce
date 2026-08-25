using Sama.Catalog.Domain;
using Sama.Shared.Kernel.Primitives;

namespace Sama.UnitTests.Domain;

/// <summary>
/// The catalogue holds a product's text in English and, optionally, in Arabic.
/// The storefront falls back to the English text when there is no translation, so
/// what counts as "no translation" has to be unambiguous.
/// </summary>
public sealed class ProductTests
{
    private static readonly Guid Tenant = Guid.Parse("0192f000-0000-7000-8000-000000000001");
    private static readonly Guid Category = Guid.Parse("0192f000-0000-7000-8000-0000000000c1");

    private static Product Create(string? nameAr = null, string? descriptionAr = null) =>
        Product.Create(
            Tenant, "SMA-CL-001", "Faded linen shirt", "faded-linen-shirt",
            "Washed European linen.", Category, Money.Create(1450m, "EGP").Value,
            brand: "Sama Studio", nameAr: nameAr, descriptionAr: descriptionAr);

    [Fact]
    public void Keeps_a_supplied_translation()
    {
        var product = Create(nameAr: "قميص كتان مغسول", descriptionAr: "كتان أوروبي مغسول.");

        Assert.Equal("قميص كتان مغسول", product.NameAr);
        Assert.Equal("كتان أوروبي مغسول.", product.DescriptionAr);
    }

    [Fact]
    public void Treats_an_absent_translation_as_null()
    {
        var product = Create();

        Assert.Null(product.NameAr);
        Assert.Null(product.DescriptionAr);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void Stores_a_blank_translation_as_null(string blank)
    {
        // An empty string satisfies a null-check, so storing one would defeat the
        // storefront's fallback and render a product with no name at all on an
        // Arabic page.
        var product = Create(nameAr: blank, descriptionAr: blank);

        Assert.Null(product.NameAr);
        Assert.Null(product.DescriptionAr);
    }

    [Fact]
    public void Trims_surrounding_whitespace_from_a_translation()
    {
        var product = Create(nameAr: "  قميص كتان  ");

        Assert.Equal("قميص كتان", product.NameAr);
    }

    [Fact]
    public void Clears_a_translation_that_is_edited_away()
    {
        // Emptying the field in the back office has to actually remove the
        // translation, not leave the previous one in place.
        var product = Create(nameAr: "قميص كتان مغسول");

        product.UpdateDetails(
            "Faded linen shirt", "faded-linen-shirt", "Washed European linen.",
            Category, "Sama Studio", nameAr: "  ", descriptionAr: null);

        Assert.Null(product.NameAr);
    }
}
