namespace SanApp.Application.Identity;

public interface IPasswordRecoveryEmailSender
{
    bool IsAvailable { get; }

    Task<bool> SendAsync(string recipient, Uri link, CancellationToken cancellationToken);
}
