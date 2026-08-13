using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Web;
using Rausoft.Erp.Api.Dienste;
using Rausoft.Erp.Data;
using Rausoft.Erp.Domain.Shared;

var builder = WebApplication.CreateBuilder(args);

// Authentifizierung: Entra ID pur — die API validiert von Entra ausgestellte
// Tokens. Mitarbeiter melden sich per M365-Konto an (SSO), CTI-Software und
// Service-Portal über eigene App-Registrierungen (Client Credentials).
// Es werden keine Passwörter oder Tokens in der eigenen Datenbank gespeichert.
//
// Ausnahme für lokale Tests: nur im Development-Environment UND mit explizitem
// Flag "Entwicklung:AuthDeaktiviert" läuft ein fester Pseudo-Benutzer ohne
// Anmeldung. Produktion erreicht diesen Zweig nie.
var entwicklungsAuth = builder.Environment.IsDevelopment()
    && builder.Configuration.GetValue<bool>("Entwicklung:AuthDeaktiviert");

if (entwicklungsAuth)
{
    builder.Services
        .AddAuthentication(EntwicklungsAuthHandler.Schema)
        .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, EntwicklungsAuthHandler>(
            EntwicklungsAuthHandler.Schema, null);
}
else
{
    builder.Services
        .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("EntraId"));
}

// Ohne explizites [AllowAnonymous] ist jeder Endpunkt geschützt —
// Berechtigungen werden serverseitig durchgesetzt, nie nur im Frontend.
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = options.DefaultPolicy;
});

builder.Services.AddDbContext<ErpDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("Kundenzentrale")));

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IAktuellerBenutzer, AktuellerBenutzer>();
builder.Services.AddScoped<NummernkreisDienst>();

builder.Services.AddControllers().AddJsonOptions(options =>
    // Übergangslösung bis zur DTO-Schicht: Navigations-Zyklen (Kunde ↔ Ansprechpartner) kappen
    options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles);
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    // Migrationen automatisch anwenden und den Entwicklungs-Benutzer anlegen,
    // damit Audit-Log-Einträge lokal einem Mitarbeiter zugeordnet werden können.
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ErpDbContext>();
    db.Database.Migrate();

    if (entwicklungsAuth)
    {
        app.Logger.LogWarning(
            "Entwicklungs-Auth aktiv: Requests laufen OHNE Anmeldung als Pseudo-Benutzer. Nur für lokale Tests!");

        if (!db.Mitarbeiter.Any(m => m.EntraObjectId == EntwicklungsAuthHandler.EntraObjectId))
        {
            db.Mitarbeiter.Add(new Mitarbeiter
            {
                EntraObjectId = EntwicklungsAuthHandler.EntraObjectId,
                Anzeigename = "Entwicklung (lokal)",
                Email = "entwicklung@localhost",
                Rollen = MitarbeiterRolle.Admin,
            });
            db.SaveChanges();
        }
    }
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
