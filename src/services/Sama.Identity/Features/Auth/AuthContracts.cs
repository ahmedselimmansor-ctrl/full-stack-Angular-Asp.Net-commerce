using FluentValidation;

namespace Sama.Identity.Features.Auth;

public sealed record RegisterRequest(string Email, string Password, string DisplayName);

public sealed record LoginRequest(string Email, string Password);

public sealed record RefreshRequest(string RefreshToken);

public sealed record LogoutRequest(string? RefreshToken);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public sealed record TokenResponse(
    string AccessToken,
    string RefreshToken,
    string TokenType,
    int ExpiresIn);

public sealed record UserProfileResponse(
    Guid Id,
    string Email,
    string DisplayName,
    bool EmailConfirmed,
    IReadOnlyCollection<string> Roles,
    IReadOnlyCollection<string> Permissions);

public sealed class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(r => r.Email)
            .NotEmpty()
            .MaximumLength(320)
            .EmailAddress();

        RuleFor(r => r.DisplayName)
            .NotEmpty()
            .MaximumLength(200);

        RuleFor(r => r.Password).ApplyPasswordPolicy();
    }
}

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(r => r.Email).NotEmpty().MaximumLength(320);

        // Deliberately no complexity rules on login: they would tell an attacker which
        // candidate passwords are even worth submitting.
        RuleFor(r => r.Password).NotEmpty().MaximumLength(256);
    }
}

public sealed class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordRequestValidator()
    {
        RuleFor(r => r.CurrentPassword).NotEmpty();
        RuleFor(r => r.NewPassword).ApplyPasswordPolicy();
        RuleFor(r => r.NewPassword)
            .NotEqual(r => r.CurrentPassword)
            .WithMessage("The new password must be different from the current one.");
    }
}

internal static class PasswordRules
{
    /// <summary>
    /// Length first, composition second. A 12-character passphrase resists guessing far
    /// better than an 8-character one forced through four character classes, which mostly
    /// produces "Password1!".
    /// </summary>
    public static IRuleBuilderOptions<T, string> ApplyPasswordPolicy<T>(this IRuleBuilder<T, string> rule)
        => rule
            .NotEmpty()
            .MinimumLength(12).WithMessage("Use at least 12 characters.")

            // Upper bound guards the hasher: PBKDF2 cost scales with input length, so an
            // unbounded password is a cheap way to make every login expensive.
            .MaximumLength(256).WithMessage("Passwords may be at most 256 characters.")
            .Must(password => password.Any(char.IsLetter) && password.Any(char.IsDigit))
            .WithMessage("Include at least one letter and one digit.");
}
