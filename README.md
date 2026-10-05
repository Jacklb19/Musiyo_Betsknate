# Musiyo Bëtsknaté

Cliente Unity del museo virtual del Carnaval del Perdón. La escena contiene arquitectura neutra; el contenido se obtiene de Musiyo API mediante contratos v1 compartidos con Web.

## Desarrollo

- Unity **6000.3.25f1**, con soporte WebGL. Escena: `Assets/_Musiyo/Scenes/10_Museum_Blockout.unity`.
- La API debe servir `museum-main` en `/api/v1/tours/museum-main`. En el editor utiliza `http://127.0.0.1:8000`; en Web, el mismo origen de la página.
- WASD o flechas: caminar; IJKL o ratón tras clic: mirar; Tab/Mayús+Tab: puntos; Enter: activar; F: ficha; Retroceso: volver; Esc: pausa. Sensibilidad ajustable en pausa.
- Los GLB publicados se cargan al acercarse o seleccionar su elemento. X: examinar; flechas o arrastre: rotar; rueda o +/-: zoom; Inicio: restablecer. La ficha permanece disponible con F.
- `Musiyo > Build Museum Web` genera `Build/MuseumWeb` y su manifiesto. Web copia el resultado con `npm.cmd run unity:sync`.

## Verificación

Pruebas EditMode: `MusiyoBetsknate.Tests`. Pruebas PlayMode: `MusiyoBetsknate.PlayMode.Tests`. `Musiyo > Validate Saved Blockout` comprueba anclas y colisiones.

El examen remoto está validado en Editor y WebGL con geometría sintética. Meta Quest y narraciones conservan sus comprobaciones pendientes. No se incluyen máscaras, grabaciones ni claves.
