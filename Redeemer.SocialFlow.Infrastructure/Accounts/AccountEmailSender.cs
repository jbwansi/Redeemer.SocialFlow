using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Redeemer.SocialFlow.Application.Accounts;

namespace Redeemer.SocialFlow.Infrastructure.Accounts;

public sealed class AccountEmailSender(IConfiguration config, IHostEnvironment environment) : IAccountEmailSender
{
    public async Task SendAsync(string email, string purpose, string link, CancellationToken token)
    {
        using var message = new MailMessage(config["Authentication:Email:From"] ?? "accounts@localhost", email)
        {
            Subject = purpose == "workspace-invitation" ? "Invitation à une équipe SocialFlow" : purpose == "confirm-email" ? "Confirmez votre compte SocialFlow" : "Réinitialisez votre mot de passe SocialFlow",
            Body = "Utilisez ce lien personnel dans l'heure qui suit :\n\n" + link +
                "\n\nSi vous n'êtes pas à l'origine de cette demande, ignorez ce message."
        };
        using var client = new SmtpClient();
        if (environment.IsDevelopment() && config.GetValue<bool>("Authentication:Email:UseLocalPickup"))
        {
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Redeemer.SocialFlow", "AccountMail");
            Directory.CreateDirectory(root);
            client.DeliveryMethod = SmtpDeliveryMethod.SpecifiedPickupDirectory;
            client.PickupDirectoryLocation = root;
        }
        else
        {
            client.Host = config["Authentication:Email:Host"] ?? throw new InvalidOperationException("Email delivery unavailable.");
            client.Port = config.GetValue("Authentication:Email:Port", 587);
            client.EnableSsl = true;
            client.Credentials = new NetworkCredential(config["Authentication:Email:Username"], config["Authentication:Email:Password"]);
        }
        await client.SendMailAsync(message, token);
    }
}
