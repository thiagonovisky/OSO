// Formulario de "Agregar evento": fecha larga, duración, dirección de la propiedad e invitado.
(function () {
    var fecha = document.getElementById('Fecha');
    var fechaLarga = document.getElementById('evtFechaLarga');
    var inicio = document.getElementById('HoraInicio');
    var fin = document.getElementById('HoraFin');
    var duracion = document.getElementById('evtDuracion');
    var propiedad = document.getElementById('IdPropiedad');
    var ubicacion = document.getElementById('Ubicacion');
    var invitado = document.getElementById('Invitado');
    var invitadoAviso = document.getElementById('evtInvitadoAviso');
    var descripcion = document.getElementById('Descripcion');
    var contador = document.getElementById('evtDescContador');

    // Abre el selector nativo al hacer click en cualquier parte del campo de fecha u hora.
    [fecha, inicio, fin].forEach(function (input) {
        input.addEventListener('click', function () {
            if (typeof input.showPicker === 'function') {
                try { input.showPicker(); } catch (e) { }
            }
        });
    });

    // El subtítulo sigue a la fecha elegida.
    fecha.addEventListener('change', function () {
        if (!fecha.value) { return; }
        var p = fecha.value.split('-');
        var d = new Date(+p[0], +p[1] - 1, +p[2]);
        var texto = d.toLocaleDateString('es-AR', { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' }).replace(',', '');
        fechaLarga.textContent = texto.replace(/(^|\s)(\p{L})/gu, function (m, s, l) { return s + l.toUpperCase(); }).replace(/ De /g, ' de ');
    });

    // La hora de fin acompaña a la de inicio (la duración propuesta viene en data-minutos-propuestos) hasta que la cambies a mano.
    var minutosPropuestos = +fin.dataset.minutosPropuestos;
    var finTocado = false;
    function aMinutos(v) { var p = v.split(':'); return parseInt(p[0], 10) * 60 + parseInt(p[1], 10); }
    function aHora(m) { return String(Math.floor(m / 60)).padStart(2, '0') + ':' + String(m % 60).padStart(2, '0'); }
    function mostrarDuracion() {
        if (!inicio.value || !fin.value) { duracion.textContent = ''; return; }
        var dif = aMinutos(fin.value) - aMinutos(inicio.value);
        fin.setCustomValidity(dif <= 0 ? 'La hora de fin tiene que ser posterior a la de inicio.' : '');
        duracion.classList.toggle('evt-hint--error', dif <= 0);
        if (dif <= 0) { duracion.textContent = 'La hora de fin tiene que ser posterior a la de inicio.'; return; }
        var h = Math.floor(dif / 60), m = dif % 60;
        duracion.textContent = 'Duración: ' + (h ? h + ' h' : '') + (h && m ? ' ' : '') + (m ? m + ' min' : '');
    }
    fin.addEventListener('input', function () { finTocado = true; mostrarDuracion(); });
    inicio.addEventListener('input', function () {
        if (!finTocado && inicio.value) {
            fin.value = aHora(Math.min(aMinutos(inicio.value) + minutosPropuestos, 23 * 60 + 59));
        }
        mostrarDuracion();
    });
    mostrarDuracion();

    // Al elegir una propiedad, la dirección toma la suya si todavía está vacía o era la de otra propiedad.
    // Sin propiedad no hay inquilino a quien invitar.
    var anterior = ubicacion.value;
    function actualizarInvitado() {
        var sinPropiedad = propiedad.value === '';
        invitado.options[1].disabled = sinPropiedad;
        if (sinPropiedad) { invitado.value = 'No'; }
        invitadoAviso.hidden = !sinPropiedad;
    }
    propiedad.addEventListener('change', function () {
        var direccion = propiedad.selectedOptions[0].dataset.direccion || '';
        if (ubicacion.value === '' || ubicacion.value === anterior) {
            ubicacion.value = direccion;
            anterior = direccion;
        }
        actualizarInvitado();
    });
    actualizarInvitado();

    descripcion.addEventListener('input', function () { contador.textContent = descripcion.value.length; });
})();
