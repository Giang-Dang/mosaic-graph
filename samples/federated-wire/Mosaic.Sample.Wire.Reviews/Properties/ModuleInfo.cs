// Module("WireReviews") is what makes builder.AddGraphQL().AddWireReviews()
// compile: the source generator names its registration method after this
// attribute and collects every [QueryType] and [ObjectType<T>] in the assembly
// into it.
[assembly: Module("WireReviews")]
