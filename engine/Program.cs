using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using SkynetZero.Web;
public static class Bootstrap
{
    public static async Task Main(string[] args) {
        var builder=WebAssemblyHostBuilder.CreateDefault(args);
        builder.RootComponents.Add<App>("#app");
        await builder.Build().RunAsync();
    }
}
