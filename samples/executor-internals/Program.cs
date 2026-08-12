using Mosaic.Sample.ExecutorInternals;

// Five facts about the HotChocolate 16.6.0 executor, each measured by building
// a request executor in process and counting diagnostic events. Nothing here
// starts a web server: four of the five are about what happens before, or
// instead of, a response.
//
// Every case asserts. A case that stops being true fails the gate rather than
// quietly making chapter 16 wrong, which is the arrangement scripts/*-cases.mjs
// already use for facts about the composer.
//
//   dotnet run --project samples/executor-internals            (all five)
//   dotnet run --project samples/executor-internals -- <case>  (one)

return await Cases.RunAsync(args);
