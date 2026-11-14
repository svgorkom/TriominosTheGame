using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Triominos.Web;
using Triominos.Web.Online;
using Triominos.Web.Storage;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped<IPreferenceStore, LocalStoragePreferenceStore>();
builder.Services.AddScoped<Preferences>();

// Online play: the Worker that serves the site also holds the rooms, on the same address.
builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddScoped<IRoomTransport, BrowserRoomTransport>();
builder.Services.AddScoped<OnlineClient>();

await builder.Build().RunAsync();
