using EventTicketing.ServiceDefaults;
using Microsoft.AspNetCore.Builder;
using Xunit;

namespace Ticketing.UnitTests;

public sealed class AuthenticationConfigurationTests
{
    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void Authentication_bypass_is_rejected_outside_local_environments(string environment)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        builder.Configuration["Authentication:Enabled"] = "false";
        Assert.Throws<InvalidOperationException>(() => builder.AddPlatformDefaults("Test"));
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    public void Explicit_local_bypass_is_allowed(string environment)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        builder.Configuration["Authentication:Enabled"] = "false";
        builder.AddPlatformDefaults("Test");
    }
}
