// The source generator collects every type marked with [QueryType],
// [ObjectType<T>] and friends across this assembly and emits a single
// AddMosaicTypes() extension method from this attribute's name. It is
// assembly-scoped, not folder-scoped, which is why one call in Program.cs
// registers all six domains.
[assembly: Module("Mosaic")]
