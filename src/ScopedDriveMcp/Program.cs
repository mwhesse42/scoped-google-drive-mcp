using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ScopedDriveMcp;

var builder = Host.CreateApplicationBuilder(args);

// Keep the config (and the key it points to) outside the repo: set SCOPED_DRIVE_CONFIG to its path.
var configPath = Environment.GetEnvironmentVariable("SCOPED_DRIVE_CONFIG")
    ?? Path.Combine(AppContext.BaseDirectory, "appsettings.json");

builder.Configuration.AddJsonFile(configPath, optional: false);

// stdout carries the MCP protocol, so every log line must go to stderr.
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

var settings = DriveSettings.Load(builder.Configuration);

builder.Services.AddSingleton(settings);
builder.Services.AddSingleton<DriveWorkspace>();

var mcp = builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<DriveReadTools>();

// In read-only mode the write tool is never registered, so the agent doesn't even see it.
if (!settings.ReadOnly)
    mcp.WithTools<DriveWriteTools>();

await builder.Build().RunAsync();
