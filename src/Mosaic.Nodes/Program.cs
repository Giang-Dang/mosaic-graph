using Mosaic.Nodes;
using Mosaic.ServiceDefaults;
using Mosaic.ServiceDefaults.Auth;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMosaicServiceDefaults();

// Chapter 15. Every service validates the same tokens the same way; the
// key arrives through configuration and lives in nobody's source file.
builder.Services.AddMosaicJwtAuthentication(builder.Configuration);

// No database, so no connection string, no DbContext factory and no seeder.
// This is the only one of the seven services whose Program.cs has nothing
// between the defaults and the schema.
builder.AddGraphQL()
    // The one true argument in the repository. Every other service passes the
    // default, false, and publishes no node field; this service publishes both
    // for the whole graph. See MosaicSubgraphDefaults for why exactly one may.
    .AddMosaicSubgraph(registerNodeInterface: true)
    // AddApolloFederation puts _service on a query type and does not create
    // one, and neither does AddGlobalObjectIdentification: node and nodes are
    // added to a query type that has to already exist. Leave this out and the
    // service fails to start with a message about a missing Query type that
    // mentions neither federation nor node.
    .AddQueryType()
    .AddNodes();

builder.Services.AddMosaicPipelineReport();

var app = builder.Build();

app.UseMosaicServiceDefaults();

app.MapGraphQL();

app.RunWithGraphQLCommands(args);
