// SlayIdleRepeat.Server — composition root and ASP.NET Core host (23 §7.1).
//
// M0 scope: boot and answer GET /health with 200 so the local compose stack
// (M0-03) and the CI smoke job have something to wait on. Game endpoints,
// the WebSocket hub and the store/ad webhooks arrive with their milestones;
// DI registration will live in Composition/, driving adapters in Endpoints/.

var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();