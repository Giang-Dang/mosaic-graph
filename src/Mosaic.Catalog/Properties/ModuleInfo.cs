// One assembly, one module, one registration method: Module("Catalog") is what
// makes builder.AddGraphQL().AddCatalog() compile. Mosaic.Api's copy of this
// file says Module("Mosaic") and collects six domains; this one collects the
// domain that left.
[assembly: Module("Catalog")]
