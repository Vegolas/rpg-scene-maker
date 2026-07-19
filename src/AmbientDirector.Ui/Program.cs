using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using AmbientDirector.Ui;
using AmbientDirector.Ui.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// The API serves this app, so its base address is the API address.
builder.Services.AddScoped(sp => new HttpClient
{
    BaseAddress = new Uri(builder.HostEnvironment.BaseAddress),
    // Setup endpoints (Tuya scan, Hue pairing) block server-side for up to ~10s;
    // stay well above that so the UI shows the server's error, not its own timeout.
    Timeout = TimeSpan.FromSeconds(30),
});
builder.Services.AddSingleton<UiState>();
builder.Services.AddScoped<ApiClient>();
builder.Services.AddScoped<ViewTransition>();
// Runtime translator: loads UI strings from the server's /i18n endpoints (see Localizer / App.razor boot gate).
builder.Services.AddScoped<Localizer>();

await builder.Build().RunAsync();
