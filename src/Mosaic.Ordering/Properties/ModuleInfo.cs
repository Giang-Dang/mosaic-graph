// One assembly, one module, one registration method: Module("Ordering") is what
// makes builder.AddGraphQL().AddOrdering() compile. Mosaic.Api's copy of this file
// said Module("Mosaic") and collected six domains; there are six of these now,
// each collecting one.
[assembly: Module("Ordering")]