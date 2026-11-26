// Rücksprung von der Microsoft-Anmeldung: Diese Seite läuft im Anmeldefenster und reicht die
// Antwort (einmaliger Code, kein Token) nur an das Fenster der Kundenzentrale weiter, das sie
// geöffnet hat — und nur an die eigene Adresse.
(function () {
    var text = document.getElementById("text");
    if (window.opener && !window.opener.closed) {
        window.opener.postMessage({ art: "outlook-anmeldung", antwort: window.location.search }, window.location.origin);
        // Das Fenster für den Entwurf bleibt offen: Die Kundenzentrale zeigt darin gleich die Mail in Outlook
        if (window.name === "kundenzentrale-entwurf") {
            text.textContent = "Angemeldet — der Entwurf wird vorbereitet …";
        } else {
            text.textContent = "Angemeldet — dieses Fenster schließt sich.";
            window.close();
        }
    } else {
        text.textContent = "Dieses Fenster gehört zur Anmeldung der Kundenzentrale und kann geschlossen werden.";
    }
})();
