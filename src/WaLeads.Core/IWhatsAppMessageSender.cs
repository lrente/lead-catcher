namespace WaLeads.Core;

public interface IWhatsAppMessageSender
{
    Task<string> SendTextAsync(
        string phoneNumberId,
        string recipient,
        string body,
        CancellationToken cancellationToken);
}
