# Musiyo Bëtsknaté

Cliente Unity del museo virtual del Carnaval del Perdón. La escena contiene arquitectura neutra; el contenido se obtiene de Musiyo API mediante contratos v1 compartidos con Web.

## Desarrollo

- Unity **6000.3.25f1**, con soporte WebGL. Escena: `Assets/_Musiyo/Scenes/10_Museum_Blockout.unity`.
- La API debe servir `museum-main` en `/api/v1/tours/museum-main`. En el editor utiliza `http://127.0.0.1:8000`; en Web, el mismo origen de la página.
- Para los datos sintéticos locales existentes: `powershell -File tools/Development/start-api.ps1`. El script configura juntos `TestResults/development.db` y `TestResults/private`; acepta `-DatabasePath`, `-StorageRoot`, `-WebOrigin` y `-Port` para otros entornos. No crea ni migra bases.
- WASD o flechas: caminar con aceleración y frenado; IJKL o ratón tras clic: mirar; Tab/Mayús+Tab: puntos; Enter: activar; F: ficha; Retroceso: volver; Re Pág/Av Pág: desplazar el panel del punto; Esc: pausa. En Web, si el navegador libera el puntero, la visita se pausa. Los menús y la lectura detienen el movimiento al instante.
- En la Sala de Personajes cada pedestal tiene un atril. Al acercarte al pedestal o al atril, la ficha del punto se despliega como una pantalla que flota frente al atril, dentro del museo, y gira para quedar siempre de cara a ti; puedes seguir caminando mientras eliges elemento o lees la ficha completa. Con el ratón capturado, el centro de la vista es el puntero: la opción a la que miras se resalta y un clic la elige (también las teclas 1 a 9); con el cursor libre se hace clic sobre ella. Se recoge al alejarte, vuelve a desplegarse al regresar y, si te acercas a otro punto, se abre el de ese punto. Los puntos sin atril conservan el panel en pantalla. Un enlace directo sitúa al visitante donde se ven la pieza y el atril.
- Los GLB publicados se cargan al acercarse o seleccionar su elemento. X: examinar; flechas o arrastre: rotar; rueda o +/-: zoom; Inicio: restablecer. La ficha permanece disponible con F.
- La narración publicada acompaña al elemento con transcripción y WebVTT opcional. Espacio: pausa; R: repetir; M: silencio. Puedes caminar mientras escuchas; se pausa al alejarte del punto y continúa al volver. Esta regla aún requiere prueba audible con narraciones reales.
- H: orientación opcional hacia una sala o por el orden sugerido. La línea del suelo sigue las colisiones del museo; puedes desactivarla y explorar libremente.
- Al llegar se muestra una tarjeta de bienvenida con la ruta sugerida (Enter la cierra). G: conversar con el guía cuando estás en su radio; mientras no exista el servicio de preguntas, el panel explica la limitación. En la última sala aparece «Volver al catálogo», que en Web envía `return_to_catalog` a la página.
- Menú inicial: Explorar museo, Configuración y Controles; pausa: continuar o volver al menú. Flechas, W/S o Tab eligen; izquierda/derecha ajustan los deslizadores; Retroceso vuelve. Los ajustes se guardan en el dispositivo. Un enlace directo válido enfoca la pieza y omite la entrada.
- Textos neutrales en `Assets/_Musiyo/Resources/MuseumInterfaceText_es.json`; presentación, movimiento, panel, pantalla del atril y distancia de permanencia en el punto en `MuseumExperienceConfiguration.json`. Las fichas sin narración publicada lo indican en el panel.
- Los atriles provienen del modelo conceptual del museo, que permanece fuera del repositorio: `blender -b --python tools/export_lecterns.py -- <concepto.glb> Assets/_Musiyo/Arte/Modelos/Boceto/Museum_Lecterns.fbx` y después `Musiyo > Configure Museum Lecterns`, que los coloca y los vincula a su punto más cercano.
- `Musiyo > Build Museum Web` genera `Build/MuseumWeb` y su manifiesto. Web copia el resultado con `npm.cmd run unity:sync`.
- Variantes offline: `npm.cmd --prefix tools/ModelVariants ci`, luego `node tools/ModelVariants/variants.mjs <source.glb> <new-external-directory> [web|quest]`. Conserva el original y genera candidatos con informe técnico; los modelos deben permanecer fuera del repositorio.

## Verificación

Pruebas EditMode: `MusiyoBetsknate.Tests`. Pruebas PlayMode: `MusiyoBetsknate.PlayMode.Tests`. `Musiyo > Validate Saved Blockout` comprueba anclas y colisiones.

Comprobación visible del build Web servido (requiere Python Playwright y Chrome): `python tools/Development/verify_web_visit.py --url <página> --out <directorio> --scenario menu|pointer|content`. Guarda capturas por paso para revisarlas; no las evalúa por sí sola.

Las pruebas automáticas usan geometría y audio sintéticos. Quest, mezcla de ambiente/guía y narraciones culturales conservan su validación pendiente. No se incluyen máscaras, grabaciones ni claves. Consulta el relevo local `.local_docs/REPORT_2026-10-08_HANDOFF.md` para el preview externo y la coordinación con Web.
