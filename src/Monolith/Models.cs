namespace Monolith;

public sealed record UserRow(long Id, string Email, string PasswordHash, string Plan);

// In the microservices this identity crossed a process boundary as string
// headers (X-User-Id / X-User-Plan). In-process it stays a typed object, so the
// user id is a real long and no "strip client copies then re-inject trusted
// headers" dance is needed — the identity never leaves the process to be forged.
public sealed record UserIdentity(long UserId, string Plan);

public sealed record LinkRow(string Domain, string Code, string TargetUrl, DateTime CreatedAt);

public sealed record ClickEvent(string Domain, string Code, string? Referer, string? UserAgent, DateTime ClickedAt);

public sealed record CreateLinkRequest(string Url, string? Domain, int? CodeLength);
public sealed record Credentials(string Email, string Password);
public sealed record SetPlanRequest(long UserId, string Plan);
