using System.Text;
using HanyingAgent.Host;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

Console.InputEncoding = new UTF8Encoding(false);
Console.OutputEncoding = new UTF8Encoding(false);

using var host = ApplicationHost.CreateBuilder(args).Build();
await host.StartAsync();
try
{
    var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
    await host.Services.GetRequiredService<ConsoleApplication>().RunAsync(lifetime.ApplicationStopping);
}
finally
{
    using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(10));
    await host.StopAsync(shutdown.Token);
}
