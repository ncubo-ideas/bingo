// El guardado que la persona controla. La pagina mira donde corre y elige el nivel:
//   'carpeta'  -> Chromium con contexto seguro (https, localhost): showDirectoryPicker, y el comprobante
//                 se reescribe solo en la carpeta elegida despues de cada ciclo.
//   'descarga' -> http plano, Safari/iOS (WebKit no tiene File System Access) o cualquier otro caso:
//                 boton que baja el comprobante, y selector de archivo para volver a traerlo.
export function instalarGuardado(ctx) {
  const { $, log, YO, leerConfig, guardarConfig, guardarArbol, armarComprobanteActual } = ctx;
  const nivel = (window.isSecureContext && typeof window.showDirectoryPicker === 'function') ? 'carpeta' : 'descarga';
  const porque = nivel === 'carpeta' ? 'contexto seguro con File System Access' : !window.isSecureContext ? 'http plano, sin contexto seguro' : 'este navegador no tiene showDirectoryPicker';
  const g = window.__guardado = { nivel, porque, carpeta: null, nombreCarpeta: null, permiso: null, escrituras: 0, ultimo: null, error: null, importado: null, escribir: null };
  $('nivel').textContent = nivel + ' (' + porque + ')';
  log('guardado: nivel ' + nivel + ', porque ' + porque);

  async function escribir(motivo) {
    if (!g.carpeta) return;
    try {
      const c = armarComprobanteActual(); if (!c) return;
      const f = await g.carpeta.getFileHandle('velada-' + YO.funcion + '.html', { create: true });
      const w = await f.createWritable(); await w.write(c.html); await w.close();
      g.escrituras++; g.ultimo = new Date().toTimeString().slice(0, 8); g.error = null;
      $('carpetaEstado').textContent = 'velada-' + YO.funcion + '.html en ' + g.nombreCarpeta + ' · ' + g.escrituras + ' escritura(s), ultima ' + g.ultimo;
      if (g.escrituras === 1 || motivo !== 'ciclo') log('COMPROBANTE ESCRITO en la carpeta ' + g.nombreCarpeta + ' (' + motivo + '): ' + Math.round(c.html.length / 1024) + ' KB, ' + c.bolitas + ' bolitas');
    } catch (e) {
      g.error = e.name + ': ' + e.message; log('no pude escribir en la carpeta: ' + g.error);
      if (e.name === 'NotAllowedError') { g.carpeta = null; g.permiso = 'prompt'; $('reanudar').style.display = ''; }
    }
  }
  g.escribir = escribir;

  async function tomarCarpeta(dir, motivo) {
    g.carpeta = dir; g.nombreCarpeta = dir.name; g.permiso = 'granted';
    $('reanudar').style.display = 'none'; $('carpeta').textContent = 'Cambiar la carpeta';
    await escribir(motivo);
  }

  if (nivel === 'carpeta') {
    $('carpeta').style.display = '';
    $('carpeta').onclick = async () => {
      try {
        const dir = await window.showDirectoryPicker({ mode: 'readwrite', id: 'velada', startIn: 'documents' });
        await guardarConfig('carpeta', dir);
        log('carpeta elegida: ' + dir.name);
        await tomarCarpeta(dir, 'carpeta elegida');
      } catch (e) { log(e.name === 'AbortError' ? 'la persona cancelo el selector de carpeta' : 'no se pudo elegir carpeta: ' + e.name + ' ' + e.message); }
    };
    $('reanudar').onclick = async () => {
      const dir = await leerConfig('carpeta'); if (!dir) return;
      const p = await dir.requestPermission({ mode: 'readwrite' }); g.permiso = p;
      log('permiso de la carpeta ' + dir.name + ': ' + p);
      if (p === 'granted') await tomarCarpeta(dir, 'permiso renovado');
    };
    leerConfig('carpeta').then(async dir => {
      if (!dir) return;
      g.nombreCarpeta = dir.name;
      const p = await dir.queryPermission({ mode: 'readwrite' }); g.permiso = p;
      log('carpeta guardada de otra vez: ' + dir.name + ', permiso ' + p);
      if (p === 'granted') await tomarCarpeta(dir, 'sesion nueva');
      else { $('reanudar').textContent = 'Seguir guardando en ' + dir.name; $('reanudar').style.display = ''; }
    }).catch(e => log('no pude leer la carpeta guardada: ' + e.name));
  }

  // Traer un comprobante bajado (de este u otro telefono): el journal que viene adentro se deposita en
  // IndexedDB y el bingo se reconstruye al volver a abrir la pagina.
  $('importar').onchange = async ev => {
    const f = ev.target.files && ev.target.files[0]; if (!f) return;
    try {
      const texto = await f.text();
      const m = /<script type="application\/json" id="velada-datos">([\s\S]*?)<\/script>/.exec(texto);
      if (!m) throw new Error('ese archivo no es un comprobante con journal adentro');
      const datos = JSON.parse(m[1]);
      if (datos.festival !== YO.funcion) throw new Error('es de otro bingo (' + datos.festival + '), este es ' + YO.funcion);
      const r = await guardarArbol(datos.journal);
      g.importado = { archivo: f.name, festival: datos.festival, archivos: r.archivos, bytes: r.bytes, bolitas: (datos.resumen.bolitas || []).length };
      $('importado').textContent = 'importado ' + f.name + ': ' + r.archivos + ' archivo(s) del journal, ' + g.importado.bolitas + ' bolitas. Cierra esta pestana y abre el bingo de nuevo desde el icono para verlo.';
      log('COMPROBANTE IMPORTADO ' + f.name + ': ' + r.archivos + ' archivo(s), ' + g.importado.bolitas + ' bolitas -> IndexedDB');
    } catch (e) { g.importado = { error: e.message }; $('importado').textContent = 'no se pudo importar: ' + e.message; log('importar: ' + e.message); }
  };
}
