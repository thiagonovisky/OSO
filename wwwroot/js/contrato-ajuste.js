// Ajuste con monto a mano: avisa si se aleja más de un 5% del calculado por el índice.
(function () {
    var input = document.getElementById('montoManual');
    if (!input) { return; }
    var calculado = parseFloat(input.dataset.calculado);
    var aviso = document.getElementById('avisoAjuste');
    input.addEventListener('input', function () {
        var propuesto = parseFloat(input.value);
        aviso.hidden = !(calculado > 0 && propuesto > 0 && Math.abs(propuesto - calculado) / calculado > 0.05);
    });
})();
