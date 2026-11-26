// Aufgabenbereich des Outlook-Add-ins „In Kundenakte“ (Durchstich).
// Liest die geöffnete Mail über Office.js — kein Zugriff auf das übrige Postfach — und fragt die
// Kundenzentrale, zu welchem Kunden sie gehört. An die Kundenzentrale gehen nur Absender,
// Empfänger, Betreff und die Kennung der Mail; der Text bleibt im Durchstich in Outlook.
(function () {
    const el = (id) => document.getElementById(id);
    const zeige = (id, text, klasse) => { const e = el(id); e.textContent = text; if (klasse !== undefined) e.className = klasse; };
    const adresse = (a) => (a && a.emailAddress) || "";

    Office.onReady((info) => {
        if (info.host !== Office.HostType.Outlook) {
            zeige("stand", "Diese Seite ist der Aufgabenbereich des Outlook-Add-ins. Außerhalb von Outlook gibt es hier nichts zu sehen.", "warn");
            return;
        }
        lesen();
        // Angehefteter Aufgabenbereich: beim Wechsel der Mail neu lesen
        try { Office.context.mailbox.addHandlerAsync(Office.EventType.ItemChanged, lesen); } catch { /* ältere Outlook-Fassungen */ }
    });

    function lesen() {
        const mail = Office.context.mailbox.item;
        el("ergebnis").hidden = true;
        if (!mail) {
            el("mail").hidden = true;
            zeige("stand", "Keine E-Mail ausgewählt.", "neben");
            return;
        }
        const empfaenger = [].concat(mail.to || [], mail.cc || []).map(adresse).filter((a) => a);
        const anhaenge = (mail.attachments || []).filter((a) => !a.isInline).map((a) => a.name);
        el("mail").hidden = false;
        zeige("von", mail.from ? `${mail.from.displayName || ""} <${adresse(mail.from)}>` : "—");
        zeige("an", empfaenger.join(", ") || "—");
        zeige("betreff", mail.subject || "(ohne Betreff)");
        zeige("anhaenge", anhaenge.length ? `${anhaenge.length}: ${anhaenge.join(", ")}` : "keine");
        zeige("kennung", mail.internetMessageId || "—");
        zeige("text", "wird gelesen …");
        mail.body.getAsync(Office.CoercionType.Text, (r) => {
            zeige("text", r.status === Office.AsyncResultStatus.Succeeded
                ? `${r.value.trim().length} Zeichen lesbar`
                : `nicht lesbar (${r.error && r.error.message})`);
        });
        zeige("stand", "Outlook hat die E-Mail gelesen. Frage die Kundenzentrale …", "neben");
        zuordnen({ absender: adresse(mail.from), empfaenger: empfaenger.slice(0, 50), betreff: mail.subject || "", messageId: mail.internetMessageId || "" });
    }

    async function zuordnen(daten) {
        try {
            // Gleiche Adresse wie die Kundenzentrale: Die Windows-Anmeldung des Mitarbeiters geht mit
            const antwort = await fetch("../api/outlook/zuordnung", {
                method: "POST",
                credentials: "include",
                headers: { "Content-Type": "application/json", "X-Requested-With": "Kundenzentrale" },
                body: JSON.stringify(daten),
            });
            if (antwort.status === 401 || antwort.status === 403) {
                zeige("stand", `Die Kundenzentrale hat die Anmeldung nicht angenommen (${antwort.status}). In diesem Outlook geht die Windows-Anmeldung nicht mit.`, "schlecht");
                return;
            }
            if (!antwort.ok) {
                zeige("stand", `Die Kundenzentrale antwortet mit Fehler ${antwort.status}.`, "schlecht");
                return;
            }
            const z = await antwort.json();
            el("ergebnis").hidden = false;
            const klar = z.art === "adresse" || z.art === "domaene";
            zeige("art", klar ? "Kunde gefunden" : z.art === "mehrdeutig" ? "Mehrere Kunden möglich" : "Kein Kunde gefunden", klar ? "gut" : "warn");
            zeige("hinweis", z.hinweis);
            const liste = el("kunden");
            liste.textContent = "";
            (z.kunden || []).forEach((k) => {
                const punkt = document.createElement("li");
                const verweis = document.createElement("a");
                verweis.href = `../kunden/${encodeURIComponent(k.id)}`;
                verweis.target = "_blank";
                verweis.rel = "noopener";
                verweis.textContent = `${k.firmenname} (${k.kundennummer})`;
                punkt.appendChild(verweis);
                if (k.ansprechpartner) punkt.appendChild(document.createTextNode(` · ${k.ansprechpartner}`));
                // Achtung-Hinweise am Kunden — nur als Text, nie als HTML
                (k.hinweise || []).forEach((h) => {
                    const band = document.createElement("div");
                    band.className = "achtung";
                    band.textContent = `⚠ ${h}`;
                    punkt.appendChild(band);
                });
                liste.appendChild(punkt);
            });
            zeige("wer", `Angemeldet an der Kundenzentrale als ${z.angemeldet}.`);
            zeige("stand", "Durchstich erfolgreich: Outlook liest die Mail, die Kundenzentrale antwortet.", "gut");
        } catch (e) {
            zeige("stand", `Die Kundenzentrale ist aus diesem Outlook nicht erreichbar (${e && e.message}). Im Firmennetz oder VPN?`, "schlecht");
        }
    }
})();
