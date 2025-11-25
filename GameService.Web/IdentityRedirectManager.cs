using Microsoft.AspNetCore.Components;
using System.Diagnostics.CodeAnalysis;

namespace GameService.Web;

public class IdentityRedirectManager(NavigationManager navigationManager)
{
    [DoesNotReturn]
    public void RedirectTo(string uri, Dictionary<string, object?> queryParameters)
    {
        var uriWithoutQuery = navigationManager.ToAbsoluteUri(uri).GetLeftPart(UriPartial.Path);
        var newUri = navigationManager.GetUriWithQueryParameters(uriWithoutQuery, queryParameters);
        RedirectTo(newUri);
    }

    [DoesNotReturn]
    public void RedirectTo(string? uri)
    {
        uri ??= "";
        // Prevent open redirects.
        if (!Uri.IsWellFormedUriString(uri, UriKind.Relative))
        {
            uri = navigationManager.ToBaseRelativePath(uri);
        }
        navigationManager.NavigateTo(uri);
        
        // Add this line to satisfy [DoesNotReturn]
        throw new InvalidOperationException($"{nameof(IdentityRedirectManager)} failed to terminate execution.");
    }
}