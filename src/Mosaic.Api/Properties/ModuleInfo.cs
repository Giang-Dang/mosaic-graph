// The source generator collects every type marked with [QueryType],
// [ObjectType<T>] and friends across this assembly and emits a single
// extension method named from this attribute: Module("Mosaic") gives
// AddMosaic(), which is what Program.cs calls. It is assembly-scoped, not
// folder-scoped, which is why one call registers all six domains.
[assembly: Module("Mosaic")]
