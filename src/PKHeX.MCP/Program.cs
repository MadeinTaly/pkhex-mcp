using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PKHeXMCP;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddSingleton<SaveContext>();
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();
