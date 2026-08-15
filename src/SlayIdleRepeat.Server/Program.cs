// M0 scope: just enough host to answer GET /health. Game endpoints, the
// WebSocket hub, and DI composition arrive with later milestones.

var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();