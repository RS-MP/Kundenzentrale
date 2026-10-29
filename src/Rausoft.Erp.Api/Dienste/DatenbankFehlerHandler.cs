using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Rausoft.Erp.Api.Dienste;

/// <summary>
/// Datenbankkonflikte als 409 mit lesbarem Text statt als nackter 500er
/// (SICHERHEIT.md Q5): gleichzeitige Änderung (RowVersion), doppelte Schlüssel
/// (Unique-Index, z. B. Vorlagenversion oder Dokumentversion bei gleichzeitiger
/// Anlage) und Fremdschlüssel-Verstöße. Der Text ist absichtlich ohne Tabellen-
/// oder Indexnamen; die Details stehen im Log. Das Frontend zeigt den Antworttext
/// an, so wie es ihn bei 400 schon tut. Alles andere bleibt ein 500 (Standard).
/// </summary>
public sealed class DatenbankFehlerHandler(ILogger<DatenbankFehlerHandler> log) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        var text = exception switch
        {
            DbUpdateConcurrencyException =>
                "Der Datensatz wurde zwischenzeitlich von jemand anderem geändert — bitte neu laden und erneut speichern.",
            DbUpdateException { InnerException: SqlException { Number: 2601 or 2627 } } =>
                "Ein gleicher Eintrag ist gerade parallel entstanden (z. B. dieselbe Nummer oder derselbe Name) — bitte neu laden.",
            DbUpdateException { InnerException: SqlException { Number: 547 } } =>
                "Der Datensatz wird noch von anderen Daten verwendet oder verweist auf etwas, das nicht mehr existiert.",
            DbUpdateException => "Die Änderung konnte nicht gespeichert werden — bitte neu laden und erneut versuchen.",
            _ => null,
        };
        if (text is null)
            return false;

        log.LogWarning(exception, "Datenbankkonflikt bei {Methode} {Pfad}", context.Request.Method, context.Request.Path);
        context.Response.StatusCode = StatusCodes.Status409Conflict;
        context.Response.ContentType = "text/plain; charset=utf-8";
        await context.Response.WriteAsync(text, ct);
        return true;
    }
}
