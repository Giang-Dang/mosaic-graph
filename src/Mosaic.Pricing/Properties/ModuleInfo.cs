// One assembly, one module, one registration method: Module("Pricing") is what
// makes builder.AddGraphQL().AddPricing() compile. Mosaic.Api's copy of this file
// said Module("Mosaic") and collected six domains; there are six of these now,
// each collecting one.
[assembly: Module("Pricing")]