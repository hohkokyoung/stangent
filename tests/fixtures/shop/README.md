# Graph fixture: a tiny shop

Three projects that exercise every Phase 1a detector without NuGet packages (restores offline):
`Shop.Core` (services, events, entities, settings), `Shop.Web` (controllers, minimal API, DI,
config, a typed HTTP client to the stock service) and `Shop.Stock` (the service it calls).
`Shared/Clock.cs` is compiled into both web projects, as eShop shares `src/Shared`.
`Shop.Tests` uses a local `[Fact]` attribute so it needs no test framework.
Used by `tests/Stangent.Core.Tests/GraphTests.cs`; line numbers matter to the diff test.
