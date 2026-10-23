using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace SecureLab.Api.Tests;

public sealed class SecureLabApiFactory : WebApplicationFactory<Program>
{
    private HttpClient? _aliceClient;

    // Один реальний сеанс на fixture: регресії ЛР2 не витрачають login quota на кожний POST.
    public async Task<HttpClient> AliceClientAsync() => _aliceClient ??= await this.LoginAsync("alice",
        Environment.GetEnvironmentVariable("SeedUsers__Password")
            ?? throw new InvalidOperationException("Set local SeedUsers__Password outside Git."));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
    }
}
