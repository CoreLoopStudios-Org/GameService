using Bunit;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using TestContext = Bunit.TestContext;

namespace GameService.ComponentTests;

public class LoginTests : TestContext
{
    [Test]
    public void Login_Page_Renders_Correctly()
    {
        Services.AddLogging();
        Services.AddScoped<AuthenticationStateProvider, FakeAuthenticationStateProvider>();

        var cut = RenderComponent<Web.Components.Pages.Error>();

        cut.MarkupMatches(@"
            <h1 class=""text-danger"">Error.</h1>
            <h2 class=""text-danger"">An error occurred while processing your request.</h2>
            <h3>Development Mode</h3>
            <p>
                Swapping to <strong>Development</strong> environment will display more detailed information about the error that occurred.
            </p>
            <p>
                <strong>The Development environment shouldn't be enabled for deployed applications.</strong>
                It can result in displaying sensitive information from exceptions to end users.
                For local debugging, enable the <strong>Development</strong> environment by setting the <strong>ASPNETCORE_ENVIRONMENT</strong> environment variable to <strong>Development</strong>
                and restarting the app.
            </p>");
    }
}

public class FakeAuthenticationStateProvider : AuthenticationStateProvider
{
    public override Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        return Task.FromResult(new AuthenticationState(new System.Security.Claims.ClaimsPrincipal()));
    }
}