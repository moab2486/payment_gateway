namespace CardManagement.Application.PlatformServices.Webhooks.Commands;

/// <summary>
/// Command to replay a dead-lettered webhook delivery with a fresh HMAC signature.
/// </summary>
public record ReplayFromDlqCommand(
    Guid DlqItemId
);
