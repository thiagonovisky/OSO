// Subida del contrato: evita el doble envío y muestra que se está leyendo.
let enviado = false;
document.getElementById('formSubirContrato').addEventListener('submit', function (e) {
    // Evita mandarlo dos veces (doble clic o Enter), que crearía el borrador y después daría error.
    if (enviado) {
        e.preventDefault();
        return;
    }
    enviado = true;
    document.getElementById('btnSubir').disabled = true;
    document.getElementById('leyendo').hidden = false;
});
