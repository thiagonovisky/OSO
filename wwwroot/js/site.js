// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Write your JavaScript code.

// Menú lateral: se abre con la hamburguesa y se cierra con la X, el fondo o Escape.
// Al abrir, el foco pasa al menú; al cerrar, vuelve a la hamburguesa.
(function () {
    var btn = document.querySelector('.oso-menu');
    var menu = document.getElementById('osoSidemenu');
    var backdrop = document.getElementById('osoSidemenuBackdrop');
    if (!btn || !menu) return;
    var cerrar = menu.querySelector('.oso-sidemenu__cerrar');

    function estaAbierto() { return menu.classList.contains('is-open'); }

    function setOpen(open) {
        if (open === estaAbierto()) return;
        menu.classList.toggle('is-open', open);
        menu.setAttribute('aria-hidden', open ? 'false' : 'true');
        btn.setAttribute('aria-expanded', open ? 'true' : 'false');
        backdrop.hidden = !open;
        if (open) {
            var primero = menu.querySelector('.oso-sidemenu__item.active') || menu.querySelector('.oso-sidemenu__item');
            if (primero) primero.focus();
        } else {
            btn.focus();
        }
    }

    btn.addEventListener('click', function () { setOpen(!estaAbierto()); });
    if (cerrar) cerrar.addEventListener('click', function () { setOpen(false); });
    backdrop.addEventListener('click', function () { setOpen(false); });
    document.addEventListener('keydown', function (e) { if (e.key === 'Escape') setOpen(false); });
})();

// Ojito para mostrar/ocultar contraseña en login y registro
(function () {
    var eyeOpen = '<svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8S1 12 1 12z"/><circle cx="12" cy="12" r="3"/></svg>';
    var eyeOff = '<svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M17.94 17.94A10.07 10.07 0 0 1 12 20c-7 0-11-8-11-8a18.45 18.45 0 0 1 5.06-5.94M9.9 4.24A9.12 9.12 0 0 1 12 4c7 0 11 8 11 8a18.5 18.5 0 0 1-2.16 3.19m-6.72-1.07a3 3 0 1 1-4.24-4.24"/><line x1="1" y1="1" x2="23" y2="23"/></svg>';

    document.querySelectorAll('.auth-page input[type="password"]').forEach(function (input) {
        var wrap = document.createElement('div');
        wrap.className = 'password-field';
        input.parentNode.insertBefore(wrap, input);
        wrap.appendChild(input);

        var btn = document.createElement('button');
        btn.type = 'button';
        btn.className = 'password-toggle';
        btn.setAttribute('aria-label', 'Mostrar contraseña');
        btn.innerHTML = eyeOpen;
        wrap.appendChild(btn);

        btn.addEventListener('click', function () {
            var show = input.type === 'password';
            input.type = show ? 'text' : 'password';
            btn.innerHTML = show ? eyeOff : eyeOpen;
            btn.setAttribute('aria-label', show ? 'Ocultar contraseña' : 'Mostrar contraseña');
        });
    });
})();

// Requisitos de contraseña en el registro
(function () {
    var input = document.getElementById('contrasena');
    var list = document.getElementById('passwordRules');
    if (!input || !list) return;
    var items = {
        len: list.querySelector('[data-rule="len"]'),
        special: list.querySelector('[data-rule="special"]')
    };
    function update() {
        var v = input.value;
        items.len.classList.toggle('is-ok', v.length >= 8);
        items.special.classList.toggle('is-ok', /[^A-Za-z0-9]/.test(v));
    }
    input.addEventListener('input', update);
    update();

    var form = input.form;
    if (form) form.addEventListener('submit', function (e) {
        if (v_invalid(input.value)) {
            e.preventDefault();
            input.focus();
        }
    });
    function v_invalid(v) { return v.length < 8 || !/[^A-Za-z0-9]/.test(v); }
})();

// Teléfono: selector de país + guiones automáticos (área-xxxx-xxxx)
(function () {
    var input = document.getElementById('telefono');
    var select = document.getElementById('codigoPais');
    if (!input || !select) return;

    function paisActual() {
        var opt = select.options[select.selectedIndex];
        return { digitos: parseInt(opt.dataset.digitos, 10), ejemplo: opt.dataset.ejemplo };
    }

    function formatear() {
        var pais = paisActual();
        var d = input.value.replace(/\D/g, '').slice(0, pais.digitos);
        var area = Math.max(pais.digitos - 8, 0);
        var partes = [];
        if (area > 0) partes.push(d.slice(0, area));
        partes.push(d.slice(area, area + 4));
        partes.push(d.slice(area + 4));
        input.value = partes.filter(function (p) { return p.length; }).join('-');
        input.setCustomValidity(d.length === pais.digitos || d.length === 0
            ? '' : 'El número debe tener ' + pais.digitos + ' dígitos, incluido el código de área.');
    }

    select.addEventListener('change', function () {
        input.placeholder = paisActual().ejemplo;
        formatear();
    });
    input.addEventListener('input', formatear);
    input.placeholder = paisActual().ejemplo;
    if (input.value) { formatear(); } // al editar un teléfono ya cargado, se muestra con guiones
})();

// Botones con data-despliega="id": muestran u ocultan ese elemento (ej. el formulario de pago debajo de un gasto).
document.addEventListener('click', function (e) {
    var boton = e.target.closest('[data-despliega]');
    if (!boton) { return; }

    var destino = document.getElementById(boton.dataset.despliega);
    if (!destino) { return; }

    var abrir = destino.hidden;
    destino.hidden = !abrir;
    boton.setAttribute('aria-expanded', abrir ? 'true' : 'false');
    boton.textContent = abrir ? 'Cancelar' : 'Pagar';
    boton.classList.toggle('btn-success', !abrir);
    boton.classList.toggle('btn-secondary', abrir);

    if (abrir) {
        var primero = destino.querySelector('input:not([type="hidden"])');
        if (primero) { primero.focus(); }
    }
});

// En pantallas chicas el menú principal se desliza de costado: se acomoda para que se vea la sección activa.
(function () {
    var activo = document.querySelector('.oso-nav__item.active');
    var nav = activo && activo.parentElement;
    if (nav && nav.scrollWidth > nav.clientWidth) {
        nav.scrollLeft = activo.offsetLeft - (nav.clientWidth - activo.offsetWidth) / 2;
    }
})();

// Perfil: el lápiz (data-editar="id") muestra el formulario de ese dato en lugar del valor; Cancelar lo vuelve a ocultar.
(function () {
    function alternar(formulario, abrir) {
        var vista = formulario.previousElementSibling;
        var lapiz = vista && vista.querySelector('[data-editar]');
        formulario.hidden = !abrir;
        if (vista) { vista.hidden = abrir; }
        if (lapiz) { lapiz.setAttribute('aria-expanded', abrir ? 'true' : 'false'); }
        if (abrir) {
            var primero = formulario.querySelector('input:not([type="hidden"])') || formulario.querySelector('select');
            if (primero) { primero.focus(); }
        } else if (lapiz) {
            formulario.reset();
            lapiz.focus();
        }
    }

    document.addEventListener('click', function (e) {
        var lapiz = e.target.closest('[data-editar]');
        if (lapiz) {
            var formulario = document.getElementById(lapiz.dataset.editar);
            if (formulario) { alternar(formulario, true); }
            return;
        }

        var cancelar = e.target.closest('[data-cancelar-edicion]');
        if (cancelar) {
            alternar(cancelar.closest('form'), false);
        }
    });

    // Un archivo elegido en un campo con data-envio-automatico se guarda enseguida (ej. la foto de perfil).
    document.addEventListener('change', function (e) {
        if (e.target.matches('[data-envio-automatico]') && e.target.files.length) {
            e.target.form.submit();
        }
    });
})();
