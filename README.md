# Musiyo Bëtsknaté

Prototipo del museo virtual interactivo del Carnaval del Perdón. Este repositorio contiene el cliente Unity; el contenido cultural público requiere aprobación y autorización vigentes.

## Abrir el proyecto

- Usar Unity `6000.0.82f1` con soporte WebGL.
- Abrir `Assets/Scenes/Recorrido_Prueba.unity` para explorar una sala geométrica con tres puntos de interés sin contenido cultural.
- En Play, caminar con `WASD`, mirar tras hacer clic y liberar el ratón con `Esc`. `Tab` y `Mayús+Tab` seleccionan puntos; `Intro` intenta examinarlos.

## Verificar

- Ejecutar las pruebas EditMode del ensamblado `MusiyoBetsknate.Tests` en Test Runner.
- Para generar el prototipo WebGL en `Build/RecorridoPruebaWebGL`, ejecutar el método de editor `MusiyoBetsknate.Editor.ConstruirWebGLPrueba.Construir` en modo batch.
- El editor actual no incluye el módulo Android; la versión Meta Quest aún requiere Android/OpenXR y una prueba con el visor físico.

La escena de prueba y su contrato JSON contienen solo identificadores y geometría sintéticos. La aplicación web, la API y el contenido cultural autorizado todavía deben integrarse antes de una entrega pública.
