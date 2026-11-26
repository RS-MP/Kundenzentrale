// Textbaustein formatiert in die Zwischenablage legen: als HTML (für Outlook, Word) und
// als reiner Text (für Felder ohne Formatierung). Der Text entsteht aus einem losgelösten
// Dokument — das fremde HTML wird dabei nie in die Seite eingehängt und nichts daraus ausgeführt.
window.textbausteinKopieren = async (html) => {
    const dokument = new DOMParser().parseFromString(html, "text/html");
    const text = (dokument.body.innerText || dokument.body.textContent || "").trim();
    if (window.ClipboardItem && navigator.clipboard && navigator.clipboard.write) {
        await navigator.clipboard.write([new ClipboardItem({
            "text/html": new Blob([html], { type: "text/html" }),
            "text/plain": new Blob([text], { type: "text/plain" }),
        })]);
        return;
    }
    await navigator.clipboard.writeText(text);
};
