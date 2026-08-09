using Microsoft.EntityFrameworkCore;

namespace Mosaic.Accounts.Data;

/// <summary>
/// Creates Accounts's database if it is not there, and fills it with the same
/// customers every chapter of this book has printed.
/// </summary>
/// <remarks>
/// <para>
/// <c>EnsureCreatedAsync</c> creates the database as well as the schema, which
/// is why chapter 12 added five services to <c>docker-compose.yml</c> and no
/// storage: the PostgreSQL container was already running, and each new service
/// asked it for a database of its own on first start. Chapter 8 did this once
/// and called it a deployment detail. Doing it five more times is where the
/// detail turns into a policy, and the policy is that two services never share
/// a table.
/// </para>
/// <para>
/// A migration would be the right answer for a system whose schema evolves.
/// Mosaic's is generated from types the book is already showing you, and six
/// migrations folders would be several thousand lines of generated code that
/// never teach anything about federation.
/// </para>
/// <para>
/// The reset switch is spelled <c>MOSAIC_RESET_DATABASE</c> in all six services
/// on purpose. A verification run has to be able to put the whole system back
/// to a known state with one variable, and six switches with different names is
/// five more things to get wrong at three in the morning.
/// </para>
/// <para>
/// Read as a string and compared by hand, which looks like the long way round
/// and is not. <c>GetValue&lt;bool&gt;</c> accepts only "true" and "false", and
/// throws on "1" - not a fallback to false, an unhandled exception that takes
/// the host down at start-up.
/// </para>
/// </remarks>
public sealed class AccountsDatabaseSeeder(
    IDbContextFactory<AccountsDbContext> contextFactory,
    IConfiguration configuration,
    AccountsSeedData seed,
    ILogger<AccountsDatabaseSeeder> logger)
    : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        var reset = configuration["MOSAIC_RESET_DATABASE"];
        if (reset is "1" or "true" or "True" or "TRUE")
        {
            logger.LogWarning(
                "MOSAIC_RESET_DATABASE is set. Dropping the accounts database and everything in it.");
            await db.Database.EnsureDeletedAsync(cancellationToken);
        }

        var created = await db.Database.EnsureCreatedAsync(cancellationToken);

        if (await db.Customers.AnyAsync(cancellationToken))
        {
            logger.LogInformation(
                "Accounts is already seeded ({Count} customers).",
                await db.Customers.CountAsync(cancellationToken));
            return;
        }

        db.Customers.AddRange(seed.Customers);

        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Seeded {Count} customers into a {State} database.",
            seed.Customers.Count,
            created ? "newly created" : "pre-existing");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}