namespace GameService.Web.Services;

public class CookieHandler(IHttpContextAccessor httpContextAccessor) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var context = httpContextAccessor.HttpContext;
        if (context != null)
        {
            if (context.Request.Headers.TryGetValue("Cookie", out var cookie))
            {
                request.Headers.Add("Cookie", cookie.ToString());
            }
            
            if (context.Request.Headers.TryGetValue("Authorization", out var auth))
            {
                request.Headers.Add("Authorization", auth.ToString());
            }
        }
        
        return await base.SendAsync(request, cancellationToken);
    }
}
