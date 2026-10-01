mergeInto(LibraryManager.library, {
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
