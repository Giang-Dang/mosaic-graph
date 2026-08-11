using Mosaic.Accounts;
using Mosaic.Accounts.Data;
using Mosaic.ServiceDefaults;
using Mosaic.ServiceDefaults.Security;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMosaicServiceDefaults();

// Chapter 15. This service holds the one field in the graph that is nobody
// else's business, so it validates tokens itself rather than trusting that
// every caller came through the router. Port 5104 is open on this machine.
builder.Services.AddMosaicSecurity(builder.Configuration);

builder.Services.AddAccountsDatabase(
    builder.Configuration.GetConnectionString("Accounts")
        ?? throw new InvalidOperationException(
            "No connection string named 'Accounts'. Start the database with "
            + "`docker compose up -d mosaic-db`, or set "
            + "ConnectionStrings__Accounts in the environment."));

builder.Services.AddAccountsDomain();

builder.AddGraphQL()
    .AddMosaicSubgraph()
    .AddMosaicAuthorization()
    .AddAccounts()
    .RegisterDbContextFactory<AccountsDbContext>();

builder.Services.AddMosaicPipelineReport();

var app = builder.Build();

app.UseMosaicServiceDefaults();

// Before MapGraphQL, and in this order. Authentication establishes who the
// caller is and authorization decides what they may do with that; ASP.NET Core
// will not reorder them for you and a resolver asking about a caller who was
// never established just sees nobody.
app.UseMosaicSecurity();

app.MapGraphQL();

app.RunWithGraphQLCommands(args);
