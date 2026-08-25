using Sama.Shared.Infrastructure.Persistence;
using Shouldly;

namespace Sama.UnitTests.Infrastructure;

/// <summary>
/// PostgreSQL folds unquoted identifiers to lower case, so EF's PascalCase names would end up
/// permanently quoted in every hand-written query. These cover the awkward cases.
/// </summary>
public class NamingTests
{
    [Theory]
    [InlineData("Product", "product")]
    [InlineData("ProductId", "product_id")]
    [InlineData("OrderNumber", "order_number")]
    [InlineData("IsDeleted", "is_deleted")]
    [InlineData("QuantityOnHand", "quantity_on_hand")]
    // Acronym runs: the break goes before the final letter, not between every capital.
    [InlineData("HTTPStatus", "http_status")]
    [InlineData("OrderID", "order_id")]
    [InlineData("SKU", "sku")]
    // Digits attach to the word they follow.
    [InlineData("Address1", "address1")]
    [InlineData("Line1Text", "line1_text")]
    // Already snake_case, or trivially short.
    [InlineData("already_snake", "already_snake")]
    [InlineData("Id", "id")]
    [InlineData("", "")]
    public void Identifiers_convert_to_snake_case(string input, string expected)
    {
        ModelBuilderExtensions.ToSnakeCase(input).ShouldBe(expected);
    }
}
