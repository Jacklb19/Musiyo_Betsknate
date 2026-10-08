# Musiyo Bëtsknaté

Cliente Unity del museo virtual del Carnaval del Perdón. La escena contiene arquitectura neutra; el contenido se obtiene de Musiyo API mediante contratos v1 compartidos con Web.

## Desarrollo

- Unity **6000.3.25f1**, con soporte WebGL. Escena: `Assets/_Musiyo/Scenes/10_Museum_Blockout.unity`.
- La API debe servir `museum-main` en `/api/v1/tours/museum-main`. En el editor utiliza `http://127.0.0.1:8000`; en Web, el mismo origen de la página.
- WASD o flechas: caminar; IJKL o ratón tras clic: mirar; Tab/Mayús+Tab: puntos; Enter: activar; F: ficha; Retroceso: volver; Esc: pausa. Sensibilidad ajustable en pausa.
- Los GLB publicados se cargan al acercarse o seleccionar su elemento. X: examinar; flechas o arrastre: rotar; rueda o +/-: zoom; Inicio: restablecer. La ficha permanece disponible con F.
- La narración publicada acompaña al elemento con transcripción y WebVTT opcional. Espacio: pausa; R: repetir; M: silencio. Volumen y subtítulos se ajustan en pausa. Puedes caminar mientras escuchas.
- H: orientación opcional hacia una sala o por el orden sugerido. La línea del suelo sigue las colisiones del museo; puedes desactivarla y explorar libremente.
- Al llegar se muestra una tarjeta de bienvenida con la ruta sugerida (Enter la cierra). G: conversar con el guía cuando estás en su radio; mientras no exista el servicio de preguntas, el panel explica la limitación. En la última sala aparece «Volver al catálogo», que en Web envía `return_to_catalog` a la página.
- Los textos del HUD y las estaciones se editan en `Assets/_Musiyo/Resources/MuseumInterfaceText_es.json` (versión 1); no contienen material cultural. El menú completo y la persistencia de preferencias siguen pendientes.
- `Musiyo > Build Museum Web` genera `Build/MuseumWeb` y su manifiesto. Web copia el resultado con `npm.cmd run unity:sync`.
- Variantes offline: `npm.cmd --prefix tools/ModelVariants ci`, luego `node tools/ModelVariants/variants.mjs <source.glb> <new-external-directory> [web|quest]`. Conserva el original y genera candidatos con informe técnico; los modelos deben permanecer fuera del repositorio.

## Verificación

Pruebas EditMode: `MusiyoBetsknate.Tests`. Pruebas PlayMode: `MusiyoBetsknate.PlayMode.Tests`. `Musiyo > Validate Saved Blockout` comprueba anclas y colisiones.

Las funciones remotas se prueban con geometría y audio sintéticos. Quest, mezcla de ambiente/guía y contenido curatorial real conservan su validación pendiente. No se incluyen máscaras, grabaciones ni claves.
