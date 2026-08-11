using Mosaic.Accounts;
using Mosaic.Accounts.Data;
using Mosaic.ServiceDefaults;
using Mosaic.ServiceDefaults.Auth;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMosaicServiceDefaults();

// Chapter 15. Every service validates the same tokens the same way; the
// key arrives through configuration and lives in nobody's source file.
builder.Services.AddMosaicJwtAuthentication(builder.Configuration);

builder.Services.AddAccountsDatabase(
    builder.Configuration.GetConnectionString("Accounts")
        ?? throw new InvalidOperationException(
            "No connection string named 'Accounts'. Start the database with "
            + "`docker compose up -d mosaic-db`, or set "
            + "ConnectionStrings__Accounts in the environment."));

builder.Services.AddAccountsDomain();

builder.AddGraphQL()
    .AddMosaicSubgraph()
    .AddAccounts()
    .RegisterDbContextFactory<AccountsDbContext>();

builder.Services.AddMosaicPipelineReport();

var app = builder.Build();

app.UseMosaicServiceDefaults();

app.MapGraphQL();

app.RunWithGraphQLCommands(args);
