# Fix-loop fixture: a bug with a failing test

A tiny .NET repo for Phase 4's fix loop (`tests/Stangent.Core.Tests/FixLoopTests.cs`, D350). `Shop.Core/OrderService.cs`
has the bug: an order over 100 should wait for approval, but `Place` compares with ten times the limit.
`proof/OrderLimitTests.cs` is the bug proof's test as the reproducer would write it; the tests copy it into
`Shop.Tests/` uncommitted, as `ticket prove` leaves it. It fails on the unchanged code and passes once the comparison
uses `ApprovalLimit`. `proof/` isn't part of the build. Restores from the local NuGet cache (xunit 2.9.3).
