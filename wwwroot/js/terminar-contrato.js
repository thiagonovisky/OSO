// Terminar contrato: los motivos distintos de fin de plazo piden descripción y fecha real.
document.getElementById('motivo').addEventListener('change', function () {
    var otro = this.value !== '' && this.value !== 'FinDePlazo';
    document.getElementById('detalleOtroMotivo').hidden = !otro;
    document.getElementById('descripcion').required = otro;
    var fecha = document.getElementById('fechaFinReal');
    if (fecha) fecha.required = otro;
});
