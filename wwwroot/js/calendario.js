// Selector de mes y año del calendario.
(function () {
    var picker = document.getElementById('calPicker');
    var toggle = picker.querySelector('.cal-picker__toggle');
    var panel = document.getElementById('calPickerPanel');
    var selectAnio = picker.querySelector('.cal-picker__year');
    var meses = picker.querySelectorAll('.cal-picker__month');
    var url = picker.dataset.url;
    var anioActual = +picker.dataset.anio, mesActual = +picker.dataset.mes;
    var hoy = new Date();

    // Cada mes apunta al año elegido; se marcan el mes que se está viendo y el de hoy.
    function pintar() {
        var anio = +selectAnio.value;
        meses.forEach(function (a) {
            var mes = +a.dataset.mes;
            a.href = url + '?anio=' + anio + '&mes=' + mes;
            a.classList.toggle('is-selected', anio === anioActual && mes === mesActual);
            a.classList.toggle('is-today', anio === hoy.getFullYear() && mes === hoy.getMonth() + 1);
        });
        picker.querySelectorAll('.cal-picker__year-btn').forEach(function (b) {
            var nuevo = anio + +b.dataset.delta;
            b.disabled = nuevo < 2000 || nuevo > 2100;
        });
    }
    function abrir(si) {
        panel.hidden = !si;
        toggle.setAttribute('aria-expanded', si);
        if (si) { selectAnio.value = anioActual; pintar(); selectAnio.focus(); }
    }

    toggle.addEventListener('click', function () { abrir(panel.hidden); });
    selectAnio.addEventListener('change', pintar);
    picker.querySelectorAll('.cal-picker__year-btn').forEach(function (b) {
        b.addEventListener('click', function () {
            selectAnio.value = +selectAnio.value + +b.dataset.delta;
            pintar();
        });
    });
    document.addEventListener('click', function (e) {
        if (!panel.hidden && !picker.contains(e.target)) { abrir(false); }
    });
    document.addEventListener('keydown', function (e) {
        if (e.key === 'Escape' && !panel.hidden) { abrir(false); toggle.focus(); }
    });
    pintar();
})();
