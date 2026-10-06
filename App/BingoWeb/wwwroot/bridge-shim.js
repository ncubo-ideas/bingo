// En la app, MAUI pone window.HybridWebView. En la pagina servida lo pone este archivo: guarda lo que
// player.js manda antes de que arranque .NET, y bridge.js lo atiende apenas esta listo.
(function () {
  var cola = [];
  window.HybridWebView = {
    SendRawMessage: function (m) { if (window.__atender) window.__atender(m); else cola.push(m); },
    __cola: cola
  };
  window.__empujar = function (obj) {
    var texto = typeof obj === 'string' ? obj : JSON.stringify(obj);
    window.dispatchEvent(new CustomEvent('HybridWebViewMessageReceived', { detail: { message: texto } }));
  };
})();
