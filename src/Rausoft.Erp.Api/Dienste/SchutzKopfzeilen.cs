namespace Rausoft.Erp.Api.Dienste;

/// <summary>
/// Zwei kleine Schutzmaßnahmen am HTTP-Rand (SICHERHEIT.md V4/V5):
///
/// 1. CSRF: Die Windows-Anmeldung schickt der Browser automatisch mit — ein
///    fremdes Intranet-Formular könnte damit z. B. den Preislisten-Upload im
///    Namen des Angemeldeten abschicken. Darum verlangt jeder schreibende
///    API-Aufruf die Kopfzeile <c>X-Requested-With: Kundenzentrale</c>. Ein
///    HTML-Formular kann sie nicht setzen; ein fremdes Skript scheitert am
///    CORS-Preflight. Das Blazor-Frontend setzt sie an seinem HttpClient.
///
/// 2. Antwort-Kopfzeilen: <c>nosniff</c> (kein Erraten des Inhaltstyps, z. B. bei
///    einer als Dokument abgelegten HTML-Datei), kein Einbetten in fremde Seiten,
///    sparsamer Referrer. Eine vollständige CSP ist für Blazor WebAssembly nicht
///    ohne 'unsafe-eval' möglich und bringt hier wenig — bewusst weggelassen.
/// </summary>
public static class SchutzKopfzeilen
{
    public const string Kopfzeile = "X-Requested-With";
    public const string Wert = "Kundenzentrale";

    public static IApplicationBuilder UseSchutzKopfzeilen(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var kopf = context.Response.Headers;
            kopf["X-Content-Type-Options"] = "nosniff";
            kopf["X-Frame-Options"] = "SAMEORIGIN";
            kopf["Content-Security-Policy"] = "frame-ancestors 'self'";
            kopf["Referrer-Policy"] = "strict-origin-when-cross-origin";

            if (IstSchreibenderApiAufruf(context.Request)
                && !string.Equals(context.Request.Headers[Kopfzeile], Wert, StringComparison.Ordinal))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.ContentType = "text/plain; charset=utf-8";
                await context.Response.WriteAsync(
                    "Die Anfrage kam nicht aus der Kundenzentrale (fehlende Kopfzeile X-Requested-With).", context.RequestAborted);
                return;
            }
            await next();
        });

    private static bool IstSchreibenderApiAufruf(HttpRequest request) =>
        request.Path.StartsWithSegments("/api")
        && !HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method) && !HttpMethods.IsOptions(request.Method);
}
