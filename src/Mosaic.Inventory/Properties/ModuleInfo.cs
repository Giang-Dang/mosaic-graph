// One assembly, one module, one registration method: Module("Inventory") is what
// makes builder.AddGraphQL().AddInventory() compile. Mosaic.Api's copy of this file
// said Module("Mosaic") and collected six domains; there are six of these now,
// each collecting one.
[assembly: Module("Inventory")]