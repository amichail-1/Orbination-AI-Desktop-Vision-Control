using DesktopControlMcp.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Server;

var builder = Host.CreateApplicationBuilder(args);

builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = new() { Name = "desktop-control", Version = "2.0.0" };
    })
    .WithStdioServerTransport()
    .WithTools<MouseTools>()
    .WithTools<KeyboardTools>()
    .WithTools<ScreenTools>()
    .WithTools<VisionTools>()
    .WithTools<CompositeTools>();

var app = builder.Build();
await app.RunAsync();
