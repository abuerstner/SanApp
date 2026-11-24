namespace SanApp.Application.Identity;

public enum InvitationSendResult
{
    Sent,
    DeliveryFailed,
    UserNotFound,
    AlreadyActivated
}

public enum InitialPasswordResult
{
    Activated,
    InvalidLink,
    InvalidPassword
}

public interface IInitialPasswordEmailSender
{
    Task<bool> SendAsync(string recipient, Uri link, CancellationToken cancellationToken);
}

public interface IInitialPasswordInvitationService
{
    Task<InvitationSendResult> SendAsync(string userId, CancellationToken cancellationToken);

    Task<InitialPasswordResult> SetPasswordAsync(
        string userId,
        string token,
        string password,
        CancellationToken cancellationToken);
}
