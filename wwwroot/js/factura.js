// "Leer monto de la factura": manda la factura elegida a la acción LeerFactura (la IA la lee, no se guarda)
// y completa los campos del formulario (monto, vencimiento y mes). La persona revisa antes de guardar.
(function () {
    function completar(formulario, nombre, valor) {
        var campo = formulario.querySelector('[name="' + nombre + '"]');
        if (campo && valor !== null && valor !== undefined && valor !== '') {
            campo.value = valor;
        }
    }

    document.addEventListener('click', async function (e) {
        var boton = e.target.closest('.btn-leer-factura');
        if (!boton) { return; }

        var formulario = boton.closest('form');
        var contenedor = boton.closest('.factura-lector');
        var archivo = contenedor.querySelector('input[type="file"]');
        var estado = contenedor.querySelector('.factura-estado');

        if (!archivo.files.length) {
            estado.textContent = 'Elegí primero el archivo de la factura.';
            return;
        }

        var datos = new FormData();
        datos.append('factura', archivo.files[0]);
        datos.append('tipo', boton.dataset.tipo || '');
        datos.append('__RequestVerificationToken', formulario.querySelector('input[name="__RequestVerificationToken"]').value);

        boton.disabled = true;
        estado.textContent = 'Leyendo la factura...';

        try {
            var respuesta = await fetch(boton.dataset.url, { method: 'POST', body: datos });
            var r = await respuesta.json();

            if (!r.ok) {
                estado.textContent = r.mensaje || 'No pudimos leer la factura. Cargá los datos a mano.';
                return;
            }

            completar(formulario, 'monto', r.monto);
            completar(formulario, 'fechaVencimiento', r.vencimiento);
            completar(formulario, 'mes', r.periodo);

            var texto = r.dudosa
                ? 'Leímos la factura con dudas: revisá bien los datos antes de guardar.'
                : 'Leímos la factura. Revisá los datos antes de guardar.';

            // En un pendiente del contrato el mes ya está fijado: si la factura es de otro mes, se avisa.
            var mesFijo = formulario.querySelector('input[type="hidden"][name="periodo"]');
            if (mesFijo && r.periodo && mesFijo.value.substring(0, 7) !== r.periodo.substring(0, 7)) {
                texto += ' Ojo: la factura parece ser de otro mes (' + r.periodo.substring(0, 7) + ').';
            }

            estado.textContent = texto + (r.proveedor ? ' (' + r.proveedor + ')' : '');
        } catch (err) {
            estado.textContent = 'No se pudo leer la factura. Cargá los datos a mano.';
        } finally {
            boton.disabled = false;
        }
    });
})();
