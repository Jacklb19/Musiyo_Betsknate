# Musiyo Bëtsknaté

Prototipo del museo virtual interactivo del Carnaval del Perdón. Este repositorio contiene el cliente Unity; el contenido cultural público requiere aprobación y autorización vigentes.

## Abrir el proyecto

- Instalar la versión de Unity indicada en `ProjectSettings/ProjectVersion.txt`, con soporte WebGL.
- Abrir `Assets/_Musiyo/Scenes/10_Museum_Blockout.unity` para revisar la arquitectura neutra, los puntos y las colisiones. La escena anterior de prueba está en `Assets/Scenes/Recorrido_Prueba.unity`.
- En la escena de prueba: caminar con `WASD`, mirar tras hacer clic y liberar el ratón con `Esc`.

## Verificar

- Ejecutar las pruebas EditMode del ensamblado `MusiyoBetsknate.Tests` en Test Runner.
- Ejecutar `Musiyo > Validate Saved Blockout` para verificar la escena técnica.
- API genera los contratos v1 en inglés, sus copias SHA-256 en `Assets/Contracts/` y los DTO de Unity. El puente Web consulta `/api/v1/tours`; las pruebas verifican ejemplos, estructura y selección.
- La versión Meta Quest requiere Android/OpenXR y una prueba con el visor físico.

La escena técnica no incluye contenido cultural listo para publicación.
