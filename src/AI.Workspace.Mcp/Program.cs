using AI.Workspace.Mcp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

// Keep the config (and the key it points to) outside the repo: set AI_WORKSPACE_CONFIG to its path.
var configPath = Environment.GetEnvironmentVariable("AI_WORKSPACE_CONFIG")
    ?? Path.Combine(AppContext.BaseDirectory, "appsettings.json");

builder.Configuration.AddJsonFile(configPath, optional: false);

// stdout carries the MCP protocol, so every log line must go to stderr.
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services.AddSingleton(DriveSettings.Load(builder.Configuration));
builder.Services.AddSingleton<DriveWorkspace>();

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();
