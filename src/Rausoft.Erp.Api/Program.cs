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
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("EntraId"));

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

builder.Services.AddControllers();
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
