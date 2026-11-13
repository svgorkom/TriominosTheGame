using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Triominos.Web;
using Triominos.Web.Storage;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped<IPreferenceStore, LocalStoragePreferenceStore>();
builder.Services.AddScoped<Preferences>();

await builder.Build().RunAsync();
