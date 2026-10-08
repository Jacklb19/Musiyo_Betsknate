mergeInto(LibraryManager.library, {
  MusiyoIsPointerLocked: function () {
    // Unity's cursor state keeps its own request; the browser alone knows when Escape released the lock.
    return document.pointerLockElement === Module['canvas'] ? 1 : 0;
  },
  MusiyoKeepKeyboardInCanvas: function () {
    var canvas = Module['canvas'];
    if (!canvas || canvas.musiyoKeyboardGuard) return;
    canvas.musiyoKeyboardGuard = true;
    // While the visit has focus, its keys must not move page focus, scroll or navigate back.
    // Unity still receives the event; the host page keeps every key once focus leaves the canvas.
    var keys = ['Tab', 'ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight', ' ', 'Backspace'];
    canvas.addEventListener('keydown', function (event) {
      if (keys.indexOf(event.key) >= 0) event.preventDefault();
    }, true);
  },
  MusiyoClearSelection: function (tourPtr) {
    window.dispatchEvent(new CustomEvent('musiyo:selection', { detail: {
      source: 'musiyo-unity', type: 'selection_cleared', version: 1,
      data: { tour_key: UTF8ToString(tourPtr) }
    } }));
  },
  MusiyoConfirmSelection: function (tourPtr, pointPtr, elementPtr) {
    window.dispatchEvent(new CustomEvent('musiyo:selection', { detail: {
      source: 'musiyo-unity', type: 'selection_confirmed', version: 1,
      data: {
        tour_key: UTF8ToString(tourPtr), point_key: UTF8ToString(pointPtr),
        element_slug: elementPtr ? UTF8ToString(elementPtr) : null
      }
    } }));
  },
  MusiyoReturnToCatalog: function (tourPtr) {
    window.dispatchEvent(new CustomEvent('musiyo:navigation', { detail: {
      source: 'musiyo-unity', type: 'return_to_catalog', version: 1,
      data: { tour_key: UTF8ToString(tourPtr) }
    } }));
  },
  MusiyoNotifyPoint: function (tourPtr, pointPtr) {
    if (window.parent === window) return;
    window.parent.postMessage({
      source: 'musiyo-unity',
      type: 'selection_confirmed',
      version: 1,
      data: {
        tour_key: UTF8ToString(tourPtr),
        point_key: UTF8ToString(pointPtr),
        element_slug: null
      }
    }, window.location.origin);
  }
});
