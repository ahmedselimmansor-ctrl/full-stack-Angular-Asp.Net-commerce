using NSubstitute;
using Sama.Shared.Kernel.Domain;
using Sama.Shared.Security.AccessControl;
using Sama.Shared.Security.Authentication;
using Sama.Shared.Security.Permissions;
using Shouldly;

namespace Sama.UnitTests.Security;

/// <summary>
/// Row-level access control. These are the tests that matter most in this file: a mistake
/// here does not throw, it quietly returns another customer's orders.
/// </summary>
public class AccessControlTests
{
    private sealed record Order(Guid OwnerId) : IUserOwned;

    private static ICurrentUser UserWith(params string[] permissions)
    {
        var user = Substitute.For<ICurrentUser>();
        var set = new HashSet<string>(permissions, StringComparer.Ordinal);

        user.Permissions.Returns(set);
        user.HasPermission(Arg.Any<string>()).Returns(call => set.Contains(call.Arg<string>()));

        return user;
    }

    [Fact]
    public void Holding_the_any_permission_grants_the_widest_scope()
    {
        var user = UserWith(SamaPermissions.Ordering.ReadAny);

        user.ScopeFor(SamaPermissions.Ordering.ReadOwn, SamaPermissions.Ordering.ReadAny)
            .ShouldBe(AccessScope.All);
    }

    [Fact]
    public void Holding_both_permissions_still_grants_the_widest_scope()
    {
        // An administrator who also happens to hold the .own permission must not be narrowed
        // to their own rows by the order the checks happen to run in.
        var user = UserWith(SamaPermissions.Ordering.ReadOwn, SamaPermissions.Ordering.ReadAny);

        user.ScopeFor(SamaPermissions.Ordering.ReadOwn, SamaPermissions.Ordering.ReadAny)
            .ShouldBe(AccessScope.All);
    }

    [Fact]
    public void Holding_only_the_own_permission_grants_the_narrow_scope()
    {
        UserWith(SamaPermissions.Ordering.ReadOwn)
            .ScopeFor(SamaPermissions.Ordering.ReadOwn, SamaPermissions.Ordering.ReadAny)
            .ShouldBe(AccessScope.Own);
    }

    [Fact]
    public void Holding_neither_permission_grants_nothing()
    {
        UserWith()
            .ScopeFor(SamaPermissions.Ordering.ReadOwn, SamaPermissions.Ordering.ReadAny)
            .ShouldBe(AccessScope.None);
    }

    [Fact]
    public void The_own_scope_filters_a_query_down_to_the_callers_rows()
    {
        var mine = Guid.CreateVersion7();
        var theirs = Guid.CreateVersion7();
        var orders = new[] { new Order(mine), new Order(theirs), new Order(mine) }.AsQueryable();

        orders.WithinScope(AccessScope.Own, mine).Count().ShouldBe(2);
    }

    [Fact]
    public void The_none_scope_yields_an_empty_result_rather_than_throwing()
    {
        // An empty page does not reveal whether matching rows exist; an exception, or a 403,
        // would confirm they do.
        var orders = new[] { new Order(Guid.CreateVersion7()) }.AsQueryable();

        orders.WithinScope(AccessScope.None, Guid.CreateVersion7()).ShouldBeEmpty();
    }

    [Fact]
    public void The_own_scope_with_no_user_yields_nothing()
    {
        var orders = new[] { new Order(Guid.CreateVersion7()) }.AsQueryable();

        orders.WithinScope(AccessScope.Own, userId: null).ShouldBeEmpty();
    }

    [Fact]
    public void CanAccess_authorises_a_single_loaded_entity()
    {
        var mine = Guid.CreateVersion7();
        var order = new Order(mine);

        AccessScope.All.CanAccess(order, Guid.CreateVersion7()).ShouldBeTrue();
        AccessScope.Own.CanAccess(order, mine).ShouldBeTrue();
        AccessScope.Own.CanAccess(order, Guid.CreateVersion7()).ShouldBeFalse();
        AccessScope.None.CanAccess(order, mine).ShouldBeFalse();
    }
}
