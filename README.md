# Musiyo Bëtsknaté

Cliente Unity del museo virtual del Carnaval del Perdón. La escena contiene arquitectura neutra; el contenido se obtiene de Musiyo API mediante contratos v1 compartidos con Web.

## Desarrollo

- Unity **6000.3.25f1**, con soporte WebGL. Escena: `Assets/_Musiyo/Scenes/10_Museum_Blockout.unity`.
- La API debe servir `museum-main` en `/api/v1/tours/museum-main`. En el editor utiliza `http://127.0.0.1:8000`; en Web, el mismo origen de la página.
- Para los datos sintéticos locales existentes: `powershell -File tools/Development/start-api.ps1`. El script configura juntos `TestResults/development.db` y `TestResults/private`; acepta `-DatabasePath`, `-StorageRoot`, `-WebOrigin` y `-Port` para otros entornos. No crea ni migra bases.
- WASD o flechas: caminar con aceleración y frenado; IJKL o ratón tras clic: mirar; Tab/Mayús+Tab: puntos; Enter: activar; F: ficha; Retroceso: volver; Esc: pausa. Los menús y la lectura detienen el movimiento al instante.
- Los GLB publicados se cargan al acercarse o seleccionar su elemento. X: examinar; flechas o arrastre: rotar; rueda o +/-: zoom; Inicio: restablecer. La ficha permanece disponible con F.
- La narración publicada acompaña al elemento con transcripción y WebVTT opcional. Espacio: pausa; R: repetir; M: silencio. Puedes caminar mientras escuchas; se pausa al alejarte del punto y continúa al volver. Esta regla aún requiere prueba audible con narraciones reales.
- H: orientación opcional hacia una sala o por el orden sugerido. La línea del suelo sigue las colisiones del museo; puedes desactivarla y explorar libremente.
- Al llegar se muestra una tarjeta de bienvenida con la ruta sugerida (Enter la cierra). G: conversar con el guía cuando estás en su radio; mientras no exista el servicio de preguntas, el panel explica la limitación. En la última sala aparece «Volver al catálogo», que en Web envía `return_to_catalog` a la página.
- Menú inicial: Explorar museo, Configuración y Controles; pausa: continuar o volver al menú. Los ajustes se guardan en el dispositivo. Un enlace directo válido enfoca la pieza y omite la entrada.
- Textos neutrales en `Assets/_Musiyo/Resources/MuseumInterfaceText_es.json`; presentación, movimiento y distancia de narración en `MuseumExperienceConfiguration.json`. La navegación de menú con flechas y Esc con el puntero capturado conservan su comprobación pendiente en navegador visible.
- `Musiyo > Build Museum Web` genera `Build/MuseumWeb` y su manifiesto. Web copia el resultado con `npm.cmd run unity:sync`.
- Variantes offline: `npm.cmd --prefix tools/ModelVariants ci`, luego `node tools/ModelVariants/variants.mjs <source.glb> <new-external-directory> [web|quest]`. Conserva el original y genera candidatos con informe técnico; los modelos deben permanecer fuera del repositorio.

## Verificación

Pruebas EditMode: `MusiyoBetsknate.Tests`. Pruebas PlayMode: `MusiyoBetsknate.PlayMode.Tests`. `Musiyo > Validate Saved Blockout` comprueba anclas y colisiones.

Las pruebas automáticas usan geometría y audio sintéticos. Quest, mezcla de ambiente/guía y narraciones culturales conservan su validación pendiente. No se incluyen máscaras, grabaciones ni claves. Consulta el relevo local `.local_docs/REPORT_2026-10-08_HANDOFF.md` para el preview externo y la coordinación con Web.
