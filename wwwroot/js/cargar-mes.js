// "Cargar el mes": el propietario suelta varias facturas, se mandan de a una a LeerFacturaParaMes (la IA las lee y dice
// en qué fila va cada una) y se completan las filas. Nada se guarda hasta que aprieta "Guardar los completos".
(function () {
    var formulario = document.getElementById('cargar-mes');
    if (!formulario) { return; }

    var MaximoPorTanda = 10;
    var Meses = ['enero', 'febrero', 'marzo', 'abril', 'mayo', 'junio', 'julio', 'agosto', 'septiembre', 'octubre', 'noviembre', 'diciembre'];

    var entrada = document.getElementById('archivos-facturas');
    var zona = document.getElementById('zona-facturas');
    var estado = document.getElementById('estado-facturas');
    var paraUbicar = document.getElementById('para-ubicar');
    var listaParaUbicar = document.getElementById('lista-para-ubicar');
    var sinLeer = document.getElementById('sin-leer');
    var listaSinLeer = document.getElementById('lista-sin-leer');
    var botonReintentar = document.getElementById('reintentar-facturas');
    var pendientesDeReintentar = [];
    var leyendo = false;

    // "2026-11-01" -> "noviembre 2026"
    function nombreMes(fecha) {
        var partes = fecha.substring(0, 7).split('-');
        return Meses[parseInt(partes[1], 10) - 1] + ' ' + partes[0];
    }

    function mismoMes(a, b) { return a && b && a.substring(0, 7) === b.substring(0, 7); }

    function fila(clave) { return formulario.querySelector('tr[data-clave="' + clave + '"]'); }
    function campo(tr, clase) { return tr.querySelector('.' + clase); }

    function tieneDatos(tr) {
        return campo(tr, 'campo-monto').value !== '' || campo(tr, 'campo-incluido').value !== '';
    }

    function descripcion(tr) {
        return nombreMes(tr.dataset.periodo) + ' · ' + tr.dataset.direccion + ' · ' + tr.dataset.servicio;
    }

    function completarFila(tr, r, archivo, marca) {
        if (r.monto !== null && r.monto !== undefined) { campo(tr, 'campo-monto').value = r.monto; }
        if (r.vencimiento) { campo(tr, 'campo-vencimiento').value = r.vencimiento; }
        var origen = campo(tr, 'cargar-mes__origen');
        origen.textContent = (marca === 'Leida' ? '📄 leída: ' : '📄 revisar: ') + archivo;
        origen.classList.toggle('text-warning', marca !== 'Leida');
    }

    // El mes queda cubierto por la factura de otro mes: no pide monto y se guarda como "incluido".
    function marcarIncluida(tr, mesFactura) {
        var monto = campo(tr, 'campo-monto');
        monto.value = '';
        monto.disabled = true;
        campo(tr, 'campo-vencimiento').disabled = true;
        campo(tr, 'campo-incluido').value = mesFactura.substring(0, 7) + '-01';
        campo(tr, 'cargar-mes__marcar-incluido').hidden = true;

        var texto = campo(tr, 'cargar-mes__incluido');
        texto.hidden = false;
        texto.textContent = 'Incluido en la factura de ' + nombreMes(mesFactura) + ' ';
        var quitar = document.createElement('button');
        quitar.type = 'button';
        quitar.className = 'btn btn-link btn-sm';
        quitar.textContent = 'Quitar';
        quitar.addEventListener('click', function () { desmarcarIncluida(tr); });
        texto.appendChild(quitar);
    }

    function desmarcarIncluida(tr) {
        campo(tr, 'campo-monto').disabled = false;
        campo(tr, 'campo-vencimiento').disabled = false;
        campo(tr, 'campo-incluido').value = '';
        campo(tr, 'cargar-mes__incluido').hidden = true;
        campo(tr, 'cargar-mes__marcar-incluido').hidden = false;
    }

    // Marcar a mano: se elige de qué mes es la factura que lo incluye (los dos meses siguientes).
    formulario.addEventListener('click', function (e) {
        var boton = e.target.closest('.cargar-mes__marcar-incluido');
        if (!boton) { return; }

        var tr = boton.closest('tr');
        var periodo = tr.dataset.periodo;
        var anio = parseInt(periodo.substring(0, 4), 10);
        var mes = parseInt(periodo.substring(5, 7), 10);

        var selector = document.createElement('select');
        selector.className = 'form-select form-select-sm';
        selector.setAttribute('aria-label', 'Mes de la factura que lo incluye');
        selector.add(new Option('¿En qué factura?', ''));
        for (var i = 1; i <= 2; i++) {
            var d = new Date(anio, mes - 1 + i, 1);
            var valor = d.getFullYear() + '-' + String(d.getMonth() + 1).padStart(2, '0') + '-01';
            selector.add(new Option('Factura de ' + nombreMes(valor), valor));
        }

        boton.hidden = true;
        boton.after(selector);
        selector.focus();
        selector.addEventListener('change', function () {
            if (selector.value) { marcarIncluida(tr, selector.value); }
            else { boton.hidden = false; }
            selector.remove();
        });
    });

    function agregarSinLeer(archivo, mensaje, paraReintentar) {
        var li = document.createElement('li');
        li.textContent = archivo.name + ': ' + mensaje;
        if (paraReintentar) { li.dataset.reintentar = 'si'; }
        listaSinLeer.appendChild(li);
        sinLeer.hidden = false;
    }

    // La factura no tiene una fila segura: se lista con un selector para que el propietario elija dónde va.
    function agregarParaUbicar(archivo, r) {
        var li = document.createElement('li');
        var datos = [r.servicio, r.domicilio, r.periodo ? nombreMes(r.periodo) : null,
            r.monto !== null && r.monto !== undefined ? '$' + Number(r.monto).toLocaleString('es-AR') : null]
            .filter(function (x) { return x; }).join(' · ');
        li.appendChild(document.createTextNode(archivo.name + (datos ? ' (' + datos + ') ' : ' ')));

        var selector = document.createElement('select');
        selector.className = 'form-select form-select-sm';
        selector.setAttribute('aria-label', 'Fila para ' + archivo.name);
        selector.add(new Option('Elegí la fila', ''));
        formulario.querySelectorAll('tr[data-clave]').forEach(function (tr) {
            if (!tieneDatos(tr)) { selector.add(new Option(descripcion(tr), tr.dataset.clave)); }
        });
        li.appendChild(selector);

        selector.addEventListener('change', function () {
            var tr = fila(selector.value);
            if (!tr) { return; }
            if (r.periodo && !mismoMes(r.periodo, tr.dataset.periodo)
                && !confirm('La factura es de ' + nombreMes(r.periodo) + '. ¿La cargás igual en ' + nombreMes(tr.dataset.periodo) + '?')) {
                selector.value = '';
                return;
            }
            completarFila(tr, r, archivo.name, 'Revisar');
            li.remove();
            if (!listaParaUbicar.children.length) { paraUbicar.hidden = true; }
        });

        listaParaUbicar.appendChild(li);
        paraUbicar.hidden = false;
    }

    function ubicar(archivo, r) {
        var tr = r.clave ? fila(r.clave) : null;
        if (r.estado === 'ParaUbicar' || !tr || tieneDatos(tr)) {
            agregarParaUbicar(archivo, r);
            return;
        }

        completarFila(tr, r, archivo.name, r.estado);
        (r.clavesIncluidas || []).forEach(function (clave) {
            var anterior = fila(clave);
            if (anterior && !tieneDatos(anterior)) { marcarIncluida(anterior, r.periodo); }
        });
    }

    async function leerUna(archivo) {
        var datos = new FormData();
        datos.append('factura', archivo);
        datos.append('__RequestVerificationToken', formulario.querySelector('input[name="__RequestVerificationToken"]').value);
        try {
            var respuesta = await fetch(entrada.dataset.url, { method: 'POST', body: datos });
            return await respuesta.json();
        } catch (err) {
            return { ok: false, tipoError: 'NoDisponible', mensaje: 'No se pudo conectar.' };
        }
    }

    async function leerTanda(archivos) {
        if (leyendo) { return; }
        leyendo = true;
        botonReintentar.hidden = true;
        pendientesDeReintentar = [];

        for (var i = 0; i < archivos.length; i++) {
            estado.textContent = 'Leyendo ' + (i + 1) + ' de ' + archivos.length + '…';
            var r = await leerUna(archivos[i]);

            if (r.ok) {
                ubicar(archivos[i], r);
            } else if (r.tipoError === 'SinCupo' || r.tipoError === 'NoDisponible') {
                // Sin cupo o sin servicio: las que siguen fallarían igual. Quedan para reintentar.
                pendientesDeReintentar = archivos.slice(i);
                pendientesDeReintentar.forEach(function (a) { agregarSinLeer(a, 'sin leer todavía', true); });
                botonReintentar.hidden = false;
                estado.textContent = (r.mensaje ? r.mensaje + ' ' : '') + 'No se pudieron leer ' + pendientesDeReintentar.length
                    + ' factura' + (pendientesDeReintentar.length === 1 ? '' : 's') + ': cargalas a mano o probá más tarde.';
                leyendo = false;
                return;
            } else {
                agregarSinLeer(archivos[i], r.mensaje || 'No se pudo leer.');
            }
        }

        estado.textContent = 'Listo. Revisá las filas completadas y apretá "Guardar los completos".';
        leyendo = false;
    }

    function recibir(lista) {
        var archivos = Array.prototype.slice.call(lista);
        if (!archivos.length) { return; }
        if (archivos.length > MaximoPorTanda) {
            alert('Se pueden leer hasta ' + MaximoPorTanda + ' facturas por vez. Vamos con las primeras ' + MaximoPorTanda + '.');
            archivos = archivos.slice(0, MaximoPorTanda);
        }
        leerTanda(archivos);
    }

    // Si el guardado volvió con errores, las filas que ya estaban marcadas como incluidas se muestran así otra vez.
    formulario.querySelectorAll('tr[data-clave]').forEach(function (tr) {
        var incluido = campo(tr, 'campo-incluido').value;
        if (incluido) { marcarIncluida(tr, incluido); }
    });

    entrada.addEventListener('change', function () { recibir(entrada.files); entrada.value = ''; });

    zona.addEventListener('dragover', function (e) { e.preventDefault(); zona.classList.add('cargar-mes__zona--activa'); });
    zona.addEventListener('dragleave', function () { zona.classList.remove('cargar-mes__zona--activa'); });
    zona.addEventListener('drop', function (e) {
        e.preventDefault();
        zona.classList.remove('cargar-mes__zona--activa');
        recibir(e.dataTransfer.files);
    });

    botonReintentar.addEventListener('click', function () {
        var archivos = pendientesDeReintentar;
        listaSinLeer.querySelectorAll('li[data-reintentar]').forEach(function (li) { li.remove(); });
        sinLeer.hidden = !listaSinLeer.children.length;
        leerTanda(archivos);
    });
})();
