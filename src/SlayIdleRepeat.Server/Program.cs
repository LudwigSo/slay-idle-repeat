// COMPOSITION PATTERN (binding for every M5 task): each functional area gets its
// own file(s) under Endpoints/ and Composition/, and exactly ONE extension-method
// call line here — plus, for an area that must configure services or middleware
// before the host is built (observability, auth), exactly one builder.AddX() line
// above Build(), with any middleware ordering declared inside the area's own file.
// Shared state (the content set, the world store, the command ledger, clock, ids)
// is obtained from Composition/GameBackbone.Shared — never newed per area, or two
// areas end up in two disjoint worlds. This file stays a table of contents: no
// logic, no lambdas beyond /health's, no adapter type named directly.

using SlayIdleRepeat.Server.Composition;

var builder = WebApplication.CreateBuilder(args);

// Observability first: it installs the logger every later area logs through.
builder.AddObservability();
builder.AddPersistence();
builder.AddAntiCheat();

var app = builder.Build();

app.UseObservability();
app.UseAntiCheat();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapAuthEndpoints();
app.MapGameCommandEndpoints();
app.MapQuerySurfaceEndpoints();
app.MapRemoteConfigEndpoint();
app.MapContentDistributionEndpoints();

app.Run();
