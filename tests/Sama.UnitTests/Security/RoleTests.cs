using Sama.Shared.Security.Permissions;
using Shouldly;

namespace Sama.UnitTests.Security;

public class RoleTests
{
    [Fact]
    public void Every_permission_a_role_grants_exists_in_the_catalogue()
    {
        // Catches a typo in a role definition, which would otherwise present as a permission
        // that can never be satisfied and an endpoint nobody can reach.
        foreach (var role in SamaRoles.AllRoles)
        {
            foreach (var permission in SamaRoles.PermissionsFor(role))
            {
                SamaPermissions.All.ShouldContain(permission, $"role '{role}' grants unknown '{permission}'");
            }
        }
    }

    [Fact]
    public void A_customer_can_read_the_catalogue_and_manage_their_own_basket_and_orders()
    {
        var granted = SamaRoles.PermissionsFor(SamaRoles.Customer);

        granted.ShouldContain(SamaPermissions.Catalog.ReadProduct);
        granted.ShouldContain(SamaPermissions.Basket.WriteOwn);
        granted.ShouldContain(SamaPermissions.Ordering.Create);
        granted.ShouldContain(SamaPermissions.Ordering.ReadOwn);
    }

    [Fact]
    public void A_customer_cannot_read_other_customers_data_or_write_the_catalogue()
    {
        var granted = SamaRoles.PermissionsFor(SamaRoles.Customer);

        granted.ShouldNotContain(SamaPermissions.Ordering.ReadAny);
        granted.ShouldNotContain(SamaPermissions.Payment.ReadAny);
        granted.ShouldNotContain(SamaPermissions.Catalog.WriteProduct);
        granted.ShouldNotContain(SamaPermissions.Identity.ReadUser);
    }

    [Fact]
    public void Support_can_look_but_cannot_touch_money_or_the_catalogue()
    {
        var granted = SamaRoles.PermissionsFor(SamaRoles.Support);

        granted.ShouldContain(SamaPermissions.Ordering.ReadAny);
        granted.ShouldContain(SamaPermissions.Payment.ReadAny);
        granted.ShouldNotContain(SamaPermissions.Payment.Refund);
        granted.ShouldNotContain(SamaPermissions.Catalog.WriteProduct);
        granted.ShouldNotContain(SamaPermissions.Ordering.CancelAny);
    }

    [Fact]
    public void A_catalogue_manager_cannot_see_orders_or_payments()
    {
        var granted = SamaRoles.PermissionsFor(SamaRoles.CatalogManager);

        granted.ShouldContain(SamaPermissions.Catalog.WriteProduct);
        granted.ShouldNotContain(SamaPermissions.Ordering.ReadAny);
        granted.ShouldNotContain(SamaPermissions.Payment.ReadAny);
    }

    [Fact]
    public void An_administrator_holds_every_permission()
    {
        SamaRoles.PermissionsFor(SamaRoles.Administrator)
            .ShouldBe(SamaPermissions.All, ignoreOrder: true);
    }

    [Fact]
    public void Combining_roles_unions_their_permissions()
    {
        var combined = SamaRoles.PermissionsFor([SamaRoles.Customer, SamaRoles.Support]);

        combined.ShouldContain(SamaPermissions.Basket.WriteOwn);   // from customer
        combined.ShouldContain(SamaPermissions.Ordering.ReadAny);  // from support
    }

    [Fact]
    public void An_unknown_role_grants_nothing_rather_than_throwing()
    {
        // A role removed from the catalogue but still recorded against a user must degrade to
        // no permissions, not break every token issue for that account.
        SamaRoles.PermissionsFor("not_a_real_role").ShouldBeEmpty();
        SamaRoles.IsKnownRole("not_a_real_role").ShouldBeFalse();
    }
}
