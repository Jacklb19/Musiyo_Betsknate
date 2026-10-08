mergeInto(LibraryManager.library, {
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
