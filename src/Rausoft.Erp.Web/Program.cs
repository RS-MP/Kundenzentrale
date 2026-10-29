using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Rausoft.Erp.Web;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Die API-Adresse kommt aus wwwroot/appsettings.json (pro Umgebung austauschbar);
// ohne Eintrag wird derselbe Host wie das Frontend angenommen.
var apiUrl = builder.Configuration["ApiUrl"] ?? builder.HostEnvironment.BaseAddress;
builder.Services.AddScoped(sp =>
{
    var client = new HttpClient { BaseAddress = new Uri(apiUrl) };
    // CSRF-Schutz: schreibende API-Aufrufe brauchen diese Kopfzeile (SICHERHEIT.md V4);
    // ein fremdes Formular kann sie nicht setzen.
    client.DefaultRequestHeaders.Add("X-Requested-With", "Kundenzentrale");
    return client;
});

await builder.Build().RunAsync();
