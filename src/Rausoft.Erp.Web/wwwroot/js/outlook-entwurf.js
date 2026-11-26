// Entwurf im eigenen Outlook-Postfach anlegen (Microsoft Graph).
//
// Die Kundenzentrale bleibt bei der Windows-Anmeldung. Für das Postfach meldet sich der Browser
// des Mitarbeiters selbst bei Microsoft an: öffentlicher Client, Autorisierungscode mit PKCE,
// Anmeldefenster. Das Zugriffstoken lebt nur im Arbeitsspeicher dieser Seite (nie im
// Browser-Speicher, nie auf dem Server) und gilt nur für das eigene Postfach (Mail.ReadWrite).
(function () {
    const BEREICH = "https://graph.microsoft.com/Mail.ReadWrite";
    const GRAPH = "https://graph.microsoft.com/v1.0";
    const KENNUNG = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
    let merker = null; // { token, bis, client }

    const b64 = (bytes) => btoa(String.fromCharCode(...new Uint8Array(bytes))).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
    const zufall = (n) => b64(crypto.getRandomValues(new Uint8Array(n)));

    function ruecksprung() {
        return new URL("outlook-anmeldung.html", document.baseURI).href;
    }

    function aufAntwortWarten(fenster) {
        return new Promise((fertig, fehler) => {
            const aufraeumen = () => { window.removeEventListener("message", hoeren); clearInterval(takt); clearTimeout(frist); };
            const hoeren = (e) => {
                if (e.origin !== window.location.origin || e.source !== fenster || !e.data || e.data.art !== "outlook-anmeldung") return;
                aufraeumen();
                fertig(new URLSearchParams(e.data.antwort));
            };
            const takt = setInterval(() => {
                if (fenster.closed) { aufraeumen(); fehler(new Error("Das Anmeldefenster wurde geschlossen, bevor die Anmeldung fertig war.")); }
            }, 500);
            const frist = setTimeout(() => { aufraeumen(); try { fenster.close(); } catch { } fehler(new Error("Die Anmeldung bei Microsoft hat zu lange gedauert.")); }, 180000);
            window.addEventListener("message", hoeren);
        });
    }

    async function tokenHolen(k) {
        if (!KENNUNG.test(k.tenantId || "") || !KENNUNG.test(k.clientId || ""))
            throw new Error("Die App-Registrierung ist nicht eingetragen (Verzeichnis-ID und Anwendungs-ID).");
        if (merker && merker.client === k.clientId && merker.bis > Date.now() + 60000)
            return merker.token;
        if (!window.crypto || !crypto.subtle)
            throw new Error("Die Anmeldung braucht eine https-Adresse.");

        // Das Fenster muss noch im Klick geöffnet werden, sonst blockiert der Browser es
        const fenster = window.open("about:blank", "kundenzentrale-outlook", "width=520,height=660");
        if (!fenster)
            throw new Error("Der Browser hat das Anmeldefenster blockiert — bitte Pop-ups für die Kundenzentrale zulassen.");

        const pruefwert = zufall(48);
        const herausforderung = b64(await crypto.subtle.digest("SHA-256", new TextEncoder().encode(pruefwert)));
        const zustand = zufall(16);
        const basis = `https://login.microsoftonline.com/${k.tenantId}/oauth2/v2.0`;
        const frage = new URLSearchParams({
            client_id: k.clientId, response_type: "code", redirect_uri: ruecksprung(), response_mode: "query",
            scope: BEREICH, state: zustand, code_challenge: herausforderung, code_challenge_method: "S256",
        });
        if (k.anmeldeHinweis) frage.set("login_hint", k.anmeldeHinweis);
        fenster.location.href = `${basis}/authorize?${frage}`;

        const antwort = await aufAntwortWarten(fenster);
        if (antwort.get("state") !== zustand)
            throw new Error("Die Antwort der Anmeldung passt nicht zur Anfrage — bitte noch einmal versuchen.");
        if (antwort.get("error"))
            throw new Error(`Microsoft hat die Anmeldung abgelehnt: ${antwort.get("error_description") || antwort.get("error")}`);

        const tausch = await fetch(`${basis}/token`, {
            method: "POST",
            headers: { "Content-Type": "application/x-www-form-urlencoded" },
            body: new URLSearchParams({
                client_id: k.clientId, grant_type: "authorization_code", code: antwort.get("code") || "",
                redirect_uri: ruecksprung(), code_verifier: pruefwert, scope: BEREICH,
            }),
        });
        const daten = await tausch.json().catch(() => ({}));
        if (!tausch.ok || !daten.access_token)
            throw new Error(`Microsoft hat kein Zugriffstoken ausgestellt: ${daten.error_description || daten.error || tausch.status}`);
        merker = { token: daten.access_token, bis: Date.now() + (daten.expires_in || 3000) * 1000, client: k.clientId };
        return merker.token;
    }

    async function graph(k, methode, pfad, inhalt) {
        const token = await tokenHolen(k);
        const antwort = await fetch(GRAPH + pfad, {
            method: methode,
            headers: {
                Authorization: `Bearer ${token}`,
                "Content-Type": "application/json",
                // Unveränderliche Kennung: Der Entwurf behält sie, wenn er nach dem Senden in „Gesendete Elemente“ wandert
                Prefer: 'IdType="ImmutableId"',
            },
            body: inhalt ? JSON.stringify(inhalt) : undefined,
        });
        const daten = await antwort.json().catch(() => ({}));
        if (!antwort.ok) {
            const fehler = new Error((daten.error && daten.error.message) || `Graph antwortet mit ${antwort.status}`);
            fehler.status = antwort.status;
            throw fehler;
        }
        return daten;
    }

    // Legt den Entwurf an und liefert Kennung und Verweis; gesendet wird nichts.
    window.outlookEntwurfAnlegen = async (k, mail) => {
        const empfaenger = (mail.empfaenger || []).filter((a) => a).map((a) => ({ emailAddress: { address: a } }));
        const m = await graph(k, "POST", "/me/messages", {
            subject: mail.betreff || "",
            body: { contentType: "HTML", content: mail.html || "" },
            toRecipients: empfaenger,
        });
        return { id: m.id, verweis: m.webLink || null, nachrichtenKennung: m.internetMessageId || null };
    };

    // Was ist aus dem Entwurf geworden? "entwurf" · "gesendet" · "geloescht"
    window.outlookEntwurfStand = async (k, id) => {
        try {
            const m = await graph(k, "GET", `/me/messages/${encodeURIComponent(id)}?$select=isDraft,sentDateTime,subject,internetMessageId`);
            return {
                stand: m.isDraft ? "entwurf" : "gesendet", gesendetAm: m.sentDateTime || null,
                betreff: m.subject || "", nachrichtenKennung: m.internetMessageId || null,
            };
        } catch (e) {
            if (e.status === 404) return { stand: "geloescht", gesendetAm: null, betreff: "", nachrichtenKennung: null };
            throw e;
        }
    };

    window.outlookRuecksprungAdresse = ruecksprung;
    window.outlookOeffnen = (verweis) => { window.open(verweis, "_blank", "noopener"); };
})();
