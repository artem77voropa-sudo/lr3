using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;

namespace SecureLab.Api.Tests;

public sealed class IdentitySeedTests(SecureLabApiFactory factory) : IClassFixture<SecureLabApiFactory>
{
    [Fact]
    public async Task Seed_PreservesIdsRolesAndAllDomainRelations()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var password = Environment.GetEnvironmentVariable("SeedUsers__Password")
            ?? throw new InvalidOperationException("Set local SeedUsers__Password outside Git.");
        foreach (var (id, name, role) in new[]
        {
            (DbSeeder.AliceId, "alice", DbSeeder.ReporterRole),
            (DbSeeder.BobId, "bob", DbSeeder.ReporterRole),
            (DbSeeder.MorganId, "morgan", DbSeeder.AnalystRole),
            (DbSeeder.AdminId, "admin", DbSeeder.AdministratorRole)
        })
        {
            var user = await users.FindByIdAsync(id.ToString());
            Assert.NotNull(user);
            Assert.Equal(name, user.UserName);
            Assert.True(await users.IsInRoleAsync(user, role));
            Assert.True(await users.CheckPasswordAsync(user, password));
            // Only booleans: no password/hash values in assertion output.
            Assert.True(!string.IsNullOrEmpty(user.PasswordHash) && user.PasswordHash != password);
        }
        Assert.Equal(DbSeeder.AliceId, await db.Incidents.Where(i => i.Id == DbSeeder.AliceIncidentId)
            .Select(i => i.OwnerUserId).SingleAsync());
        Assert.Equal(DbSeeder.BobId, await db.Incidents.Where(i => i.Id == DbSeeder.BobIncidentId)
            .Select(i => i.OwnerUserId).SingleAsync());
        Assert.DoesNotContain(db.Model.GetEntityTypes(), t => t.GetTableName() == "study_users");
        foreach (var domainType in new[] { typeof(Incident), typeof(IncidentComment), typeof(IncidentStatusHistory) })
        {
            var entity = db.Model.FindEntityType(domainType)!;
            Assert.Contains(entity.GetForeignKeys(), f => f.PrincipalEntityType.ClrType == typeof(ApplicationUser));
        }
        var aliceHash = await db.Users.Where(u => u.Id == DbSeeder.AliceId).Select(u => u.PasswordHash).SingleAsync();
        var bobHash = await db.Users.Where(u => u.Id == DbSeeder.BobId).Select(u => u.PasswordHash).SingleAsync();
        Assert.True(aliceHash != bobHash);
    }
}
