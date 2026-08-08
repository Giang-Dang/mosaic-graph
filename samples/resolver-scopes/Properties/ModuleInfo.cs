using HotChocolate;

// The generator names its registration method after this attribute, so
// Module("Scopes") is what makes builder.AddGraphQL().AddScopes() compile.
[assembly: Module("Scopes")]
