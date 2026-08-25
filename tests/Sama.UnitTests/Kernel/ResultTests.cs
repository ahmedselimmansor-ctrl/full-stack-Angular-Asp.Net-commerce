using Sama.Shared.Kernel.Results;
using Shouldly;

namespace Sama.UnitTests.Kernel;

public class ResultTests
{
    [Fact]
    public void A_successful_result_exposes_its_value()
    {
        Result.Success(42).Value.ShouldBe(42);
    }

    [Fact]
    public void Reading_the_value_of_a_failure_throws_rather_than_returning_a_default()
    {
        // Returning default here would let a caller who skipped the IsSuccess check carry a
        // silently-wrong zero or null into business logic.
        var result = Result.Failure<int>(Error.NotFound("x.notFound", "gone"));

        Should.Throw<InvalidOperationException>(() => result.Value);
    }

    [Fact]
    public void A_success_cannot_be_constructed_carrying_an_error()
    {
        Should.Throw<ArgumentException>(() => Result.Failure(Error.None));
    }

    [Fact]
    public void An_error_converts_implicitly_to_a_failed_result()
    {
        Result<int> result = Error.Conflict("x.conflict", "clash");

        result.IsFailure.ShouldBeTrue();
        result.Error.Type.ShouldBe(ErrorType.Conflict);
    }

    [Fact]
    public void Match_forces_both_branches_to_be_handled()
    {
        Result.Success("ok").Match(v => v, e => e.Code).ShouldBe("ok");
        Result.Failure<string>(Error.NotFound("a.b", "c")).Match(v => v, e => e.Code).ShouldBe("a.b");
    }

    [Fact]
    public void Map_transforms_a_success_and_passes_a_failure_through_untouched()
    {
        Result.Success(2).Map(x => x * 2).Value.ShouldBe(4);

        var failure = Result.Failure<int>(Error.Validation("bad", "no")).Map(x => x * 2);
        failure.IsFailure.ShouldBeTrue();
        failure.Error.Code.ShouldBe("bad");
    }

    [Fact]
    public void Validation_errors_carry_per_field_detail()
    {
        var error = Error.Validation("request.invalid", "nope", new Dictionary<string, string[]>
        {
            ["Email"] = ["Required"],
        });

        error.Details.ShouldNotBeNull();
        error.Details!["Email"].ShouldBe(["Required"]);
    }
}
