using BlueID.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Server;
using Microsoft.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;
using static OpenIddict.Server.OpenIddictServerEvents;

namespace BlueID.Application.OAuth;

/// <summary>
/// docs/00 D16: OpenIddict accepts client_secret_post for every confidential client once the method is enabled. BlueID
/// accepts it only from applications registered with it (AllowClientSecretPost); every other application keeps
/// client_secret_basic or private_key_jwt. Applies to every endpoint that authenticates clients.
/// </summary>
public sealed class ClientSecretPostGuard(BlueIdDbContext db) : IOpenIddictServerHandler<ProcessAuthenticationContext>
{
    public async ValueTask HandleAsync(ProcessAuthenticationContext context)
    {
        var request = context.Request;
        if (request is null || string.IsNullOrEmpty(request.ClientSecret))
        {
            return;
        }

        var http = context.Transaction.GetHttpRequest();
        var viaHeader = http?.Headers.Authorization.ToString().StartsWith("Basic ", StringComparison.OrdinalIgnoreCase) == true;
        if (viaHeader)
        {
            return;
        }

        var allowed = await db.ClientApplications.IgnoreQueryFilters()
            .AnyAsync(a => a.ClientId == request.ClientId && a.DeletedAt == null && a.AllowClientSecretPost);
        if (!allowed)
        {
            context.Reject(Errors.InvalidClient, "This application must authenticate with client_secret_basic or private_key_jwt.");
        }
    }
}
