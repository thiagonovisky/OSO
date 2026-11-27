// Revisión del contrato: reparto de cada gasto.
// Al elegir Inquilino o Propietario el porcentaje se completa solo; Compartido lo deja editable.
document.querySelectorAll('.regla-responsable').forEach(function (select) {
    var porcentaje = select.closest('tr').querySelector('.regla-porcentaje');
    function sincronizar(cambio) {
        if (select.value === 'Inquilino') { porcentaje.value = 100; }
        else if (select.value === 'Propietario') { porcentaje.value = 0; }
        else if (cambio && (porcentaje.value === '0' || porcentaje.value === '100')) { porcentaje.value = 50; }
        porcentaje.readOnly = select.value !== 'Compartido';
    }
    select.addEventListener('change', function () { sincronizar(true); });
    sincronizar(false);
});
