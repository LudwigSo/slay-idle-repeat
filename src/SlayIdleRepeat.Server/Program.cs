// COMPOSITION PATTERN (binding for every M5 task): each functional area gets its
// own file(s) under Endpoints/ and Composition/, and exactly ONE extension-method
// call line here. This file stays a table of contents — no logic, no lambdas
// beyond /health's, no adapter type named directly.

using SlayIdleRepeat.Server.Composition;

var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapGameCommandEndpoints();

app.Run();
