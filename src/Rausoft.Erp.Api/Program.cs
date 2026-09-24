using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Web;
using Rausoft.Erp.Api.Dienste;
using Rausoft.Erp.Data;
using Rausoft.Erp.Domain.Shared;

var builder = WebApplication.CreateBuilder(args);

// Authentifizierung — drei Modi, es werden nie Passwörter/Tokens in der
// eigenen Datenbank gespeichert:
//
// 1. "Windows" (Interimsbetrieb im Haus): Anmeldung über die lokale AD.
//    Unter IIS übernimmt IIS die Windows-Authentifizierung (der Negotiate-
//    Handler deaktiviert sich dort selbst), unter Kestrel verhandelt der
//    Handler Kerberos/NTLM direkt. Wie beim Urlaubsplaner.
// 2. "EntraId" (Standard/Zukunft): Die API validiert von Entra ausgestellte
//    Tokens; CTI/Service-Portal über eigene App-Registrierungen.
// 3. Entwicklungs-Auth: nur im Development-Environment UND mit explizitem
//    Flag "Entwicklung:AuthDeaktiviert" läuft ein fester Pseudo-Benutzer
//    ohne Anmeldung. Produktion erreicht diesen Zweig nie.
var entwicklungsAuth = builder.Environment.IsDevelopment()
    && builder.Configuration.GetValue<bool>("Entwicklung:AuthDeaktiviert");
var windowsAuth = !entwicklungsAuth
    && string.Equals(builder.Configuration["Authentifizierung:Modus"], "Windows", StringComparison.OrdinalIgnoreCase);

if (entwicklungsAuth)
{
    builder.Services
        .AddAuthentication(EntwicklungsAuthHandler.Schema)
        .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, EntwicklungsAuthHandler>(
            EntwicklungsAuthHandler.Schema, null);
}
else if (windowsAuth)
{
    builder.Services
        .AddAuthentication(NegotiateDefaults.AuthenticationScheme)
        .AddNegotiate();
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
    // "Admin" prüft die Mitarbeiter-Rolle aus der DB (AdminAnforderungHandler)
    options.AddPolicy("Admin", policy => policy.Requirements.Add(new AdminAnforderung()));
});
builder.Services.AddScoped<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, AdminAnforderungHandler>();

// Blazor WASM (eigener Dev-Port) darf die API in der Entwicklung aufrufen
builder.Services.AddCors();

builder.Services.AddDbContext<ErpDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("Kundenzentrale")));

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IAktuellerBenutzer, AktuellerBenutzer>();
builder.Services.AddScoped<NummernkreisDienst>();

// Geheimnisse verschlüsselt at rest (Sicherheitsregel): Der Schlüsselbund liegt
// maschinenweit unter ProgramData und ist DPAPI-geschützt (Machine-Scope), damit
// Web-App UND Odoo-Import-Aufgabe (anderes Konto) dieselben Geheimnisse lesen.
// Zugriff auf den Ordner per NTFS-ACL einschränken (DEPLOYMENT.md).
var schluesselOrdner = new DirectoryInfo(GeheimnisAblage.SchluesselOrdner);
schluesselOrdner.Create();
#pragma warning disable CA1416 // DPAPI ist Windows-only — Betrieb ausschließlich auf Windows Server
builder.Services.AddDataProtection()
    .SetApplicationName(GeheimnisAblage.Anwendungsname)
    .PersistKeysToFileSystem(schluesselOrdner)
    .ProtectKeysWithDpapi(protectToLocalMachine: true);
#pragma warning restore CA1416
builder.Services.AddHttpClient();
builder.Services.AddScoped<GeheimnisDienst>();
builder.Services.AddScoped<ServerStatusDienst>();
builder.Services.AddSingleton<OdooImportDienst>();
builder.Services.AddHostedService<ServerStatusHintergrund>();

builder.Services.AddControllers().AddJsonOptions(options =>
    // Übergangslösung bis zur DTO-Schicht: Navigations-Zyklen (Kunde ↔ Ansprechpartner) kappen
    options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles);
builder.Services.AddOpenApi();

var app = builder.Build();

// Migrationen automatisch anwenden — Einzelinstanz-Betrieb, das Deployment
// braucht so keinen separaten Migrationsschritt.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ErpDbContext>();
    db.Database.Migrate();

    if (entwicklungsAuth)
    {
        // Entwicklungs-Benutzer anlegen, damit Audit-Log-Einträge lokal
        // einem Mitarbeiter zugeordnet werden können.
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

    // Erst-Admin für den Windows-Auth-Betrieb: das konfigurierte Konto bekommt
    // beim Start die Admin-Rolle (alle weiteren Rollen pflegt dann ein Admin).
    if (windowsAuth && builder.Configuration["Authentifizierung:ErstAdminKonto"] is { Length: > 0 } erstAdmin)
    {
        var mitarbeiter = db.Mitarbeiter.FirstOrDefault(m => m.WindowsKonto == erstAdmin);
        if (mitarbeiter is null)
            db.Mitarbeiter.Add(NeuerWindowsMitarbeiter(erstAdmin, MitarbeiterRolle.Admin));
        else if (!mitarbeiter.Rollen.HasFlag(MitarbeiterRolle.Admin))
            mitarbeiter.Rollen |= MitarbeiterRolle.Admin;
        db.SaveChanges();
    }
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Unterpfad-Betrieb ohne IIS (nur für Kestrel-Tests): unter IIS als
// Unteranwendung setzt IIS den PathBase pro Request selbst — dort NICHT setzen.
// UseRouting danach ist Pflicht, sonst matcht das automatisch vorgezogene
// Routing die Routen VOR dem Abschneiden des Basispfads.
if (builder.Configuration["Hosting:PfadBasis"] is { Length: > 0 } pfadBasis)
{
    app.UsePathBase(pfadBasis);
    app.UseRouting();
}

app.UseHttpsRedirection();

// Die API liefert auch das Blazor-Frontend aus (ein IIS-Site, kein CORS nötig);
// in der Entwicklung läuft das Frontend weiterhin auf dem eigenen Dev-Port.
app.UseBlazorFrameworkFiles();
app.UseStaticFiles();

if (app.Environment.IsDevelopment())
{
    // Nur lokale Origins (Blazor-Dev-Server); im Produktionsbetrieb kommt das
    // Frontend vom selben Host — CORS bleibt dort aus.
    app.UseCors(policy => policy
        .SetIsOriginAllowed(origin => new Uri(origin).IsLoopback)
        .AllowAnyHeader()
        .AllowAnyMethod());
}

app.UseAuthentication();
app.UseAuthorization();

if (windowsAuth)
{
    // Windows-Betrieb: unbekannte Domänen-Konten beim ersten Zugriff als
    // Mitarbeiter (ohne Rollen) anlegen, damit Timeline und Audit-Log sofort
    // zugeordnet werden. Rollen vergibt ein Admin.
    var bekannteKonten = new System.Collections.Concurrent.ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
    app.Use(async (context, next) =>
    {
        if (context.User.Identity is { IsAuthenticated: true, Name: { } konto }
            && konto.Contains('\\') && !bekannteKonten.ContainsKey(konto))
        {
            var db = context.RequestServices.GetRequiredService<ErpDbContext>();
            if (!await db.Mitarbeiter.AnyAsync(m => m.WindowsKonto == konto))
            {
                db.Mitarbeiter.Add(NeuerWindowsMitarbeiter(konto));
                await db.SaveChangesAsync();
            }
            bekannteKonten.TryAdd(konto, 0);
        }
        await next();
    });
}

app.MapControllers();

// Fallback fürs Blazor-Frontend: index.html mit passender <base href> ausliefern.
// So läuft die App an der Site-Wurzel UND als IIS-Unteranwendung (z. B.
// /Kundenzentrale wie beim Urlaubsplaner) — der Basispfad kommt pro Request
// aus PathBase, an den Dateien muss nichts angepasst werden.
app.MapFallback(async context =>
{
    var datei = Path.Combine(app.Environment.WebRootPath ?? "", "index.html");
    if (!File.Exists(datei))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }
    var basis = context.Request.PathBase.HasValue ? context.Request.PathBase.Value + "/" : "/";
    var html = (await File.ReadAllTextAsync(datei, context.RequestAborted))
        .Replace("<base href=\"/\" />", $"<base href=\"{basis}\" />");
    context.Response.ContentType = "text/html; charset=utf-8";
    await context.Response.WriteAsync(html, context.RequestAborted);
});

app.Run();

// E-Mail ist nur ein eindeutiger Platzhalter (aus der AD kommt keine Adresse) —
// pflegt ein Admin später nach; Phase 1 verschickt keine Mails an Mitarbeiter.
static Mitarbeiter NeuerWindowsMitarbeiter(string konto, MitarbeiterRolle rollen = MitarbeiterRolle.Keine) => new()
{
    WindowsKonto = konto,
    Anzeigename = konto[(konto.IndexOf('\\') + 1)..],
    Email = konto.Replace('\\', '.').ToLowerInvariant() + "@ad.lokal",
    Rollen = rollen,
};
