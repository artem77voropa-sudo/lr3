namespace SecureLab.Api.Presentation.Contracts;

public sealed record RegistrationInput(string? UserName, string? Email, string? DisplayName, string? Password);
public sealed record LoginInput(string? UserName, string? Password);
public sealed record RegistrationResponse(Guid Id, string UserName, string DisplayName);
public sealed record CurrentUserResponse(Guid Id, string UserName, string Role);
