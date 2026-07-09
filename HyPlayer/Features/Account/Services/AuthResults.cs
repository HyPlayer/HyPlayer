namespace HyPlayer.Features.Account.Services;

public sealed record AuthResult(bool IsSuccess, string? ErrorMessage = null);

public sealed record AuthQrKeyResult(bool IsSuccess, string? Key = null, string? ErrorMessage = null);

public sealed record AuthQrCheckResult(int Code = 0, string? ErrorMessage = null);

public sealed record AuthDeviceRegisterResult(bool IsSuccess, string? TemporaryUserId = null, string? ErrorMessage = null);
