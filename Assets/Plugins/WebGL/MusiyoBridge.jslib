mergeInto(LibraryManager.library, {
  MusiyoNotificarPunto: function (recorridoPtr, puntoPtr) {
    if (window.parent === window) return;
    window.parent.postMessage({
      source: 'musiyo-unity',
      type: 'point-selected',
      schemaVersion: 1,
      recorridoId: UTF8ToString(recorridoPtr),
      puntoId: UTF8ToString(puntoPtr)
    }, window.location.origin);
  }
});
