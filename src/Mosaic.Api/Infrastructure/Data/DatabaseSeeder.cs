using Microsoft.EntityFrameworkCore;
using Mosaic.Api.Accounts.Data;
using Mosaic.Api.Catalog.Data;
using Mosaic.Api.Inventory.Data;
using Mosaic.Api.Ordering.Data;
using Mosaic.Api.Pricing.Data;
using Mosaic.Api.Reviews.Data;

namespace Mosaic.Api.Infrastructure.Data;

/// <summary>
/// Creates Mosaic's schema if it is not there, and fills it with the seed data
/// the earlier chapters used, once.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="DbContext.Database"/>'s <c>EnsureCreatedAsync</c> rather than a
/// migration, and that is a deliberate limit on this book's scope. Migrations
/// are the right answer for a system whose schema evolves; Mosaic's schema is
/// generated from types the book is already showing you, and a migrations
/// folder would be several hundred lines of generated code that never teaches
/// anything about federation. A production service would use
/// <c>dotnet ef migrations add</c>; this one does not pretend to.
/// </para>
/// <para>
/// Running as a hosted service matters. Anything done before
/// <c>RunWithGraphQLCommands</c> would also run for <c>schema export</c>,
/// which has no database and should not need one. Hosted services start only
/// when the web host does.
/// </para>
/// </remarks>
public sealed class DatabaseSeeder(
    IDbContextFactory<MosaicDbContext> contextFactory,
    CatalogSeedData catalog,
    PricingSeedData pricing,
    InventorySeedData inventory,
    ReviewsSeedData reviews,
    AccountsSeedData accounts,
    OrderingSeedData ordering,
    ILogger<DatabaseSeeder> logger)
    : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        var created = await db.Database.EnsureCreatedAsync(cancellationToken);

        if (await db.Products.AnyAsync(cancellationToken))
        {
            logger.LogInformation(
                "Database is already seeded ({Products} products).",
                await db.Products.CountAsync(cancellationToken));
            return;
        }

        db.Products.AddRange(catalog.Products);
        db.Prices.AddRange(pricing.Prices);
        db.StockLevels.AddRange(inventory.StockLevels);
        db.Reviews.AddRange(reviews.Reviews);
        db.Customers.AddRange(accounts.Customers);
        db.Orders.AddRange(ordering.Orders);

        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Seeded {Products} products, {Reviews} reviews, {Customers} customers "
            + "and {Orders} orders into a {State} schema.",
            catalog.Products.Count,
            reviews.Reviews.Count,
            accounts.Customers.Count,
            ordering.Orders.Count,
            created ? "newly created" : "pre-existing");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
