// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Write your JavaScript code.

(function () {
    var btn = document.querySelector('.oso-menu');
    var menu = document.getElementById('osoSidemenu');
    var backdrop = document.getElementById('osoSidemenuBackdrop');
    if (!btn || !menu) return;
    function setOpen(open) {
        menu.classList.toggle('is-open', open);
        menu.setAttribute('aria-hidden', open ? 'false' : 'true');
        btn.setAttribute('aria-expanded', open ? 'true' : 'false');
        backdrop.hidden = !open;
    }
    btn.addEventListener('click', function () { setOpen(!menu.classList.contains('is-open')); });
    backdrop.addEventListener('click', function () { setOpen(false); });
    document.addEventListener('keydown', function (e) { if (e.key === 'Escape') setOpen(false); });
})();
