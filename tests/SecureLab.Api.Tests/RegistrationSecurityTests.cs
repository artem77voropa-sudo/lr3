using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;

namespace SecureLab.Api.Tests;

public sealed class RegistrationSecurityTests(SecureLabApiFactory factory) : IClassFixture<SecureLabApiFactory>
{
    private static string Password => Environment.GetEnvironmentVariable("SeedUsers__Password")
        ?? throw new InvalidOperationException("Set local SeedUsers__Password outside Git.");

    [Fact]
    public async Task T01_Register_SafeDtoReporterAndServerId_ThenDuplicate409()
    {
        using var client = factory.CreateClient();
        var name = "reg" + Guid.NewGuid().ToString("N");
        var forgedId = Guid.NewGuid();
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        try
        {
            var input = new { userName = "  " + name + "  ", email = name + "@example.test",
                displayName = "  Навчальний користувач  ", password = Password, id = forgedId, role = "Administrator" };
            using var created = await client.PostAsJsonAsync("/api/auth/register", input);
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            Assert.Equal("/api/me", created.Headers.Location?.ToString());
            using var json = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
            Assert.Equal(new[] { "displayName", "id", "userName" }, json.RootElement.EnumerateObject().Select(p => p.Name).Order().ToArray());
            Assert.NotEqual(forgedId, json.RootElement.GetProperty("id").GetGuid());
            Assert.Equal("Навчальний користувач", json.RootElement.GetProperty("displayName").GetString());
            var user = await users.FindByNameAsync(name);
            Assert.NotNull(user);
            Assert.True(await users.IsInRoleAsync(user, "Reporter"));
            Assert.False(await users.IsInRoleAsync(user, "Administrator"));
            using var anonymous = await client.GetAsync("/api/me");
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
            using var duplicate = await client.PostAsJsonAsync("/api/auth/register", input);
            Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
            Assert.Equal("application/problem+json", duplicate.Content.Headers.ContentType?.MediaType);
            using var loggedIn = await factory.LoginAsync(name, Password);
            using var me = await loggedIn.GetAsync("/api/me");
            Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        }
        finally
        {
            var user = await users.FindByNameAsync(name);
            if (user is not null) await users.DeleteAsync(user);
        }
    }

    [Theory]
    [InlineData("name")]
    [InlineData("email")]
    public async Task Duplicate_NormalizedIdentifiers_ReturnSame409(string duplicateField)
    {
        using var client = factory.CreateClient();
        var name = "dup" + Guid.NewGuid().ToString("N");
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        try
        {
            using var created = await client.PostAsJsonAsync("/api/auth/register", new
            { userName = name, email = name + "@example.test", displayName = "Тест", password = Password });
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            using var duplicate = await client.PostAsJsonAsync("/api/auth/register", new
            { userName = duplicateField == "name" ? name.ToUpperInvariant() : "other" + name,
                email = duplicateField == "email" ? (name + "@example.test").ToUpperInvariant() : "other" + name + "@example.test",
                displayName = "Тест", password = Password });
            Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
            using var json = JsonDocument.Parse(await duplicate.Content.ReadAsStringAsync());
            Assert.Equal("Реєстрація недоступна", json.RootElement.GetProperty("title").GetString());
        }
        finally
        {
            foreach (var candidate in new[] { name, "other" + name })
            {
                var user = await users.FindByNameAsync(candidate);
                if (user is not null) await users.DeleteAsync(user);
            }
        }
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("name-format")]
    [InlineData("name-long")]
    [InlineData("email-format")]
    [InlineData("email-long")]
    [InlineData("display-long")]
    [InlineData("display-blank")]
    [InlineData("password-weak")]
    public async Task T02_InvalidRegistration_Returns400WithoutAccount(string scenario)
    {
        var name = "invalid" + Guid.NewGuid().ToString("N");
        var body = new Dictionary<string, object?> { ["userName"] = name, ["email"] = name + "@example.test",
            ["displayName"] = "Тест", ["password"] = Password };
        switch (scenario)
        {
            case "missing": body["password"] = null; break;
            case "name-format": body["userName"] = name + "!"; break;
            case "name-long": body["userName"] = new string('a', 257); break;
            case "email-format": body["email"] = "invalid"; break;
            case "email-long": body["email"] = new string('a', 250) + "@example.test"; break;
            case "display-long": body["displayName"] = new string('a', 121); break;
            case "display-blank": body["displayName"] = "   "; break;
            case "password-weak": body["password"] = "weak"; break;
        }
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
        var before = await db.Users.CountAsync();
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/auth/register", body);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(before, await db.Users.CountAsync());
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.All(json.RootElement.EnumerateObject(), p =>
            Assert.Contains(p.Name, new[] { "type", "title", "status", "errors", "traceId" }));
    }
}
