// Al abrir un chat, se muestra el último mensaje.
(function () {
    var box = document.getElementById('msj-messages');
    if (box) box.scrollTop = box.scrollHeight;
})();
