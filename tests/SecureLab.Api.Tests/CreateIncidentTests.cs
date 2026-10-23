using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;
using SecureLab.Api.Presentation.Contracts;

namespace SecureLab.Api.Tests;

public sealed class CreateIncidentTests(SecureLabApiFactory factory) : IClassFixture<SecureLabApiFactory>, IAsyncLifetime
{
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _client = await factory.AliceClientAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static CreateIncidentRequest Valid(string title) => new(title, "Навчальний опис події.",
        "Low", new DateTimeOffset(2026, 9, 1, 13, 0, 0, TimeSpan.FromHours(3)));

    private async Task CleanupAsync(string title)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
        await db.Incidents.Where(row => row.Title == title).ExecuteDeleteAsync();
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string? field = null)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal((int)status, doc.RootElement.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(doc.RootElement.GetProperty("title").GetString()));
        if (field is not null)
            Assert.True(doc.RootElement.GetProperty("errors").TryGetProperty(field, out _));
        foreach (var forbidden in new[] { "Exception", "stackTrace", "SELECT ", "INSERT ", "Host=", "Password=", "Npgsql", "SecureLabDbContext" })
            Assert.DoesNotContain(forbidden, json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task T01_Create_NormalizesTextAndUtc_AndIgnoresServerFields()
    {
        var title = $"T01-{Guid.NewGuid():N}";
        var requestedId = Guid.NewGuid();
        try
        {
            using var response = await _client.PostAsJsonAsync("/api/incidents", new
            {
                title = $"  {title}  ", description = "  Навчальний опис події.  ", severity = "low",
                occurredAtUtc = "2026-09-01T13:00:00+03:00", id = requestedId,
                ownerUserId = DbSeeder.BobId, status = "Closed",
                createdAtUtc = "2000-01-01T00:00:00Z", updatedAtUtc = "2000-01-01T00:00:00Z"
            });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var created = await response.Content.ReadFromJsonAsync<CreatedIncidentResponse>();
            Assert.NotNull(created);
            Assert.NotEqual(requestedId, created.Id);
            Assert.Equal(title, created.Title);
            Assert.Equal("Навчальний опис події.", created.Description);
            Assert.Equal("Low", created.Severity);
            Assert.Equal("New", created.Status);
            Assert.Equal(TimeSpan.Zero, created.OccurredAtUtc.Offset);
            Assert.Equal(DateTimeOffset.Parse("2026-09-01T10:00:00Z"), created.OccurredAtUtc);
            Assert.Equal(created.CreatedAtUtc, created.UpdatedAtUtc);
            Assert.True(created.CreatedAtUtc > DateTimeOffset.UtcNow.AddMinutes(-1));
            Assert.Equal($"/api/incidents/{created.Id}", response.Headers.Location?.ToString());
            var json = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain("ownerUserId", json);
            Assert.DoesNotContain("email", json);
            var details = await _client.GetFromJsonAsync<IncidentDetailsResponse>(response.Headers.Location);
            Assert.NotNull(details);
            Assert.Equal("Аліса Коваль", details.OwnerDisplayName);
            Assert.Equal("New", details.Status);
        }
        finally { await CleanupAsync(title); }
    }

    [Theory]
    [InlineData("title-null", "title")]
    [InlineData("title-blank", "title")]
    [InlineData("title-long", "title")]
    [InlineData("title-before-trim", "title")]
    [InlineData("description-null", "description")]
    [InlineData("description-blank", "description")]
    [InlineData("description-long", "description")]
    [InlineData("description-before-trim", "description")]
    [InlineData("severity-null", "severity")]
    [InlineData("severity-unknown", "severity")]
    [InlineData("severity-seven", "severity")]
    [InlineData("date-null", "occurredAtUtc")]
    [InlineData("date-future", "occurredAtUtc")]
    public async Task T02_InvalidDto_Returns400WithoutWriting(string scenario, string field)
    {
        var request = Valid($"T02-{Guid.NewGuid():N}");
        request = scenario switch
        {
            "title-null" => request with { Title = null },
            "title-blank" => request with { Title = "   " },
            "title-long" => request with { Title = new string('x', 161) },
            "title-before-trim" => request with { Title = " " + new string('x', 159) + " " },
            "description-null" => request with { Description = null },
            "description-blank" => request with { Description = "   " },
            "description-long" => request with { Description = new string('x', 4001) },
            "description-before-trim" => request with { Description = " " + new string('x', 3999) + " " },
            "severity-null" => request with { Severity = null },
            "severity-unknown" => request with { Severity = "Unknown" },
            "severity-seven" => request with { Severity = "7" },
            "date-null" => request with { OccurredAtUtc = null },
            "date-future" => request with { OccurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(10) },
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
        var countBefore = await db.Incidents.CountAsync();
        try
        {
            using var response = await _client.PostAsJsonAsync("/api/incidents", request);
            await AssertProblemAsync(response, HttpStatusCode.BadRequest, field);
            Assert.Equal(countBefore, await db.Incidents.CountAsync());
        }
        finally { await CleanupAsync(request.Title?.Trim() ?? ""); }
    }

    [Theory]
    [InlineData("High", 39, HttpStatusCode.BadRequest)]
    [InlineData("High", 40, HttpStatusCode.Created)]
    [InlineData("Critical", 39, HttpStatusCode.BadRequest)]
    [InlineData("Critical", 40, HttpStatusCode.Created)]
    public async Task T09_CrossField_UsesTrimmedDescription(string severity, int length, HttpStatusCode expected)
    {
        var title = $"T09-{Guid.NewGuid():N}";
        try
        {
            using var response = await _client.PostAsJsonAsync("/api/incidents",
                Valid(title) with { Severity = severity, Description = "  " + new string('x', length) + "  " });
            Assert.Equal(expected, response.StatusCode);
            if (expected == HttpStatusCode.BadRequest)
                await AssertProblemAsync(response, expected, "description");
        }
        finally { await CleanupAsync(title); }
    }

    [Theory]
    [InlineData(IncidentStatus.New)]
    [InlineData(IncidentStatus.Triaged)]
    [InlineData(IncidentStatus.InProgress)]
    [InlineData(IncidentStatus.Resolved)]
    [InlineData(IncidentStatus.Closed)]
    public async Task T03_Duplicate_ConflictsUnlessClosed(IncidentStatus status)
    {
        var title = $"T03-{Guid.NewGuid():N}";
        try
        {
            using var first = await _client.PostAsJsonAsync("/api/incidents", Valid(title));
            Assert.Equal(HttpStatusCode.Created, first.StatusCode);
            var created = await first.Content.ReadFromJsonAsync<CreatedIncidentResponse>();
            Assert.NotNull(created);
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
            var row = await db.Incidents.SingleAsync(item => item.Id == created.Id);
            row.Status = status;
            await db.SaveChangesAsync();
            using var second = await _client.PostAsJsonAsync("/api/incidents", Valid($"  {title}  "));
            if (status == IncidentStatus.Closed)
                Assert.Equal(HttpStatusCode.Created, second.StatusCode);
            else
                await AssertProblemAsync(second, HttpStatusCode.Conflict);
            Assert.Equal(status == IncidentStatus.Closed ? 2 : 1,
                await db.Incidents.CountAsync(item => item.Title == title));
        }
        finally { await CleanupAsync(title); }
    }

    [Fact]
    public async Task T03_TitleComparison_IsCaseSensitive()
    {
        var title = $"Case-{Guid.NewGuid():N}";
        try
        {
            using var first = await _client.PostAsJsonAsync("/api/incidents", Valid(title));
            using var second = await _client.PostAsJsonAsync("/api/incidents", Valid(title.ToLowerInvariant()));
            Assert.Equal(HttpStatusCode.Created, first.StatusCode);
            Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        }
        finally { await CleanupAsync(title); await CleanupAsync(title.ToLowerInvariant()); }
    }

    [Fact]
    public async Task T01_MaximumLengths_AndNearFutureAreAccepted()
    {
        var title = Guid.NewGuid().ToString("N") + new string('x', 128);
        try
        {
            using var response = await _client.PostAsJsonAsync("/api/incidents", Valid(title) with
            { Description = new string('x', 4000), OccurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(4) });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }
        finally { await CleanupAsync(title); }
    }

    [Fact]
    public async Task T06_UnknownIncident_ReturnsSafe404()
    {
        using var response = await _client.GetAsync("/api/incidents/99999999-9999-9999-9999-999999999999");
        await AssertProblemAsync(response, HttpStatusCode.NotFound);
    }
}
