namespace HyPlayer.Services.Playback.Messages;

public record EnterForegroundFromBackgroundNotification;

public record PlaybarVisibilityChangedNotification(bool IsActivated);