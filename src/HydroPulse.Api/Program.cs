using HydroPulse.Infrastructure.Persistence;
using HydroPulse.Application.Interfaces;
using HydroPulse.Api.Workers;
using HydroPulse.Api.Hubs;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSignalR();

builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "HydroPulse API",
        Version = "v1"
    });
});

// SignalR browser clients need explicit origins + credentials. Configure "Cors:Origins" in appsettings.
var corsOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>()
                  ?? new[] { "http://localhost:3000", "http://localhost:5173" };
builder.Services.AddCors(options =>
    options.AddPolicy("Dashboard", policy => policy
        .WithOrigins(corsOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials()));

builder.Services.AddDbContext<HydroPulseDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddScoped<ITelemetryRepository, TelemetryRepository>();
builder.Services.AddHostedService<H2TelemetrySimulationWorker>();

var app = builder.Build();

// Apply pending EF Core migrations before the simulation worker starts writing.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<HydroPulseDbContext>();
    db.Database.Migrate();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "HydroPulse API v1");
    });
}
else
{
    // The container only exposes HTTP (8080); redirecting would just log warnings in Development.
    app.UseHttpsRedirection();
}

app.UseCors("Dashboard");
app.UseAuthorization();
app.MapControllers();
app.MapHub<TelemetryHub>("/hubs/telemetry");

app.Run();
