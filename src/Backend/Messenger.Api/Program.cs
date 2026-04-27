using Messenger.Api.Extensions;
using Messenger.Api.Hubs;
using Messenger.Api.Services;
using Messenger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddMessengerApi(builder.Configuration, builder.Environment);

// CORS for web client — origins loaded from Cors:AllowedOrigins config (WEB-02)
var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? [];

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(allowedOrigins)
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials();
    });
});

var app = builder.Build();

// CORS middleware - before authentication
app.UseCors();

// ── Database migration ──────────────────────────────────────────────────────
// Applies any pending EF Core migrations at startup so the DB stays in sync.
// In production, run migrations out-of-band (e.g. with `dotnet ef database update`).
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await dbContext.Database.MigrateAsync();
}

// ── Middleware ──────────────────────────────────────────────────────────────
app.UseMiddleware<ExceptionHandlingMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    // Built-in .NET 10 OpenAPI document: GET /openapi/v1.json
    app.MapOpenApi();
}

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<ChatHub>("/hubs/chat");

app.Run();
