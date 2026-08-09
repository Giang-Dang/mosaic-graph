using Microsoft.EntityFrameworkCore;

namespace Mosaic.Catalog.Data;

/// <summary>
/// Creates Catalog's database if it is not there, and fills it with the same
/// twenty-five products every chapter of this book has printed.
/// </summary>
/// <remarks>
/// <para>
/// <c>EnsureCreatedAsync</c> creates the database as well as the schema, which
/// is the whole reason Catalog needed no change to
/// <c>docker-compose.yml</c>: the PostgreSQL container was already running, and
/// the new service asked it for a database of its own on first start.
/// </para>
/// <para>
/// The reset switch is spelled <c>MOSAIC_RESET_DATABASE</c> in both services on
/// purpose. A verification run has to be able to put the whole system back to a
/// known state with one variable, and two switches with different names is one
/// more thing to get wrong at three in the morning.
/// </para>
/// </remarks>
public sealed class CatalogDatabaseSeeder(
    IDbContextFactory<CatalogDbContext> contextFactory,
    IConfiguration configuration,
    CatalogSeedData catalog,
    ILogger<CatalogDatabaseSeeder> logger)
    : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        var reset = configuration["MOSAIC_RESET_DATABASE"];
        if (reset is "1" or "true" or "True" or "TRUE")
        {
            logger.LogWarning(
                "MOSAIC_RESET_DATABASE is set. Dropping the catalog database and everything in it.");
            await db.Database.EnsureDeletedAsync(cancellationToken);
        }

        var created = await db.Database.EnsureCreatedAsync(cancellationToken);

        if (await db.Products.AnyAsync(cancellationToken))
        {
            logger.LogInformation(
                "Catalog is already seeded ({Products} products).",
                await db.Products.CountAsync(cancellationToken));
            return;
        }

        db.Products.AddRange(catalog.Products);

        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Seeded {Products} products into a {State} database.",
            catalog.Products.Count,
            created ? "newly created" : "pre-existing");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
