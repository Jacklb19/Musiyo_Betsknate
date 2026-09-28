using System;
using System.Collections.Generic;
using MusiyoBetsknate.Museo;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MusiyoBetsknate.Editor
{
    public static class CrearRecorridoPrueba
    {
        private const string RutaEscena = "Assets/Scenes/Recorrido_Prueba.unity";

        [MenuItem("Musiyo/Crear recorrido de prueba")]
        public static void Crear()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(RutaEscena) != null)
            {
                Debug.Log("La escena de prueba ya existe; no se sobrescribió.");
                return;
            }

            var escena = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.ambientLight = new Color(0.54f, 0.56f, 0.59f);
            RenderSettings.fog = false;

            var suelo = Material("PruebaSuelo", new Color(0.42f, 0.46f, 0.48f));
            var muro = Material("PruebaMuro", new Color(0.75f, 0.77f, 0.73f));
            var pedestal = Material("PruebaPedestal", new Color(0.26f, 0.47f, 0.50f));

            var visitante = new GameObject("VisitanteEscritorio");
            visitante.transform.position = new Vector3(0, 1, -7);
            var cuerpo = visitante.AddComponent<CharacterController>();
            cuerpo.height = 1.8f;
            cuerpo.radius = 0.3f;
            cuerpo.stepOffset = 0.25f;
            var camaraGo = new GameObject("CamaraVisitante");
            camaraGo.tag = "MainCamera";
            camaraGo.transform.SetParent(visitante.transform, false);
            camaraGo.transform.localPosition = new Vector3(0, 0.55f, 0);
            var camara = camaraGo.AddComponent<Camera>();
            camara.clearFlags = CameraClearFlags.Skybox;
            camaraGo.AddComponent<AudioListener>();
            var control = visitante.AddComponent<ControladorEscritorio>();
            Asignar(control, "camara", camara);

            var raiz = new GameObject("RecorridoPrueba");
            var salaGo = new GameObject("SalaPrueba");
            salaGo.transform.SetParent(raiz.transform);

            var entrada = new GameObject("PuntoDeEntrada");
            entrada.transform.SetParent(salaGo.transform);
            entrada.transform.position = visitante.transform.position;

            Cubo("Suelo", salaGo.transform, new Vector3(0, -0.15f, 0), new Vector3(14, 0.3f, 20), suelo);
            Cubo("MuroIzquierdo", salaGo.transform, new Vector3(-7, 1.6f, 0), new Vector3(0.3f, 3.2f, 20), muro);
            Cubo("MuroDerecho", salaGo.transform, new Vector3(7, 1.6f, 0), new Vector3(0.3f, 3.2f, 20), muro);
            Cubo("MuroFondo", salaGo.transform, new Vector3(0, 1.6f, 10), new Vector3(14, 3.2f, 0.3f), muro);
            Cubo("MuroEntrada", salaGo.transform, new Vector3(0, 1.6f, -10), new Vector3(14, 3.2f, 0.3f), muro);

            var posiciones = new[] { new Vector3(-4, 1, 2), new Vector3(0, 1, 4), new Vector3(4, 1, 2) };
            for (int i = 0; i < posiciones.Length; i++)
            {
                var anclaje = new GameObject("Punto " + (i + 1));
                anclaje.transform.SetParent(salaGo.transform);
                anclaje.transform.position = posiciones[i];
                var baseVisual = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                baseVisual.name = "Pedestal";
                baseVisual.transform.SetParent(anclaje.transform, false);
                baseVisual.transform.localPosition = new Vector3(0, -0.5f, 0);
                baseVisual.transform.localScale = new Vector3(1.1f, 0.5f, 1.1f);
                baseVisual.GetComponent<Renderer>().sharedMaterial = pedestal;
                var punto = anclaje.AddComponent<PuntoInteres>();
                Asignar(punto, "idComponente", "punto-0" + (i + 1));
                Asignar(punto, "nombreComponente", "Punto de prueba " + (i + 1));
            }

            var sala = salaGo.AddComponent<Escena>();
            Asignar(sala, "idComponente", "sala-prueba");
            Asignar(sala, "nombreComponente", "Sala de prueba sin contenido cultural");
            Asignar(sala, "puntoDeEntrada", entrada.transform);
            Asignar(sala, "descripcionAccesible", "Sala geométrica de prueba con tres puntos sin contenido publicado.");

            var recorrido = raiz.AddComponent<RecorridoVirtual>();
            Asignar(recorrido, "idComponente", "recorrido-prueba");
            Asignar(recorrido, "visitante", visitante.transform);

            var interaccion = new GameObject("InteraccionEscritorio");
            var teclado = interaccion.AddComponent<NavegacionTecladoPuntosInteres>();
            Asignar(teclado, "recorridoVirtual", recorrido);
            var hud = interaccion.AddComponent<HudRecorridoPrueba>();
            Asignar(hud, "navegacion", teclado);

            var contrato = raiz.AddComponent<ValidadorContratoEscena>();
            Asignar(contrato, "recorrido", recorrido);
            Asignar(contrato, "contratoJson", AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Contratos/recorrido-prueba-v1.json"));

            var luzGo = new GameObject("Luz");
            luzGo.transform.rotation = Quaternion.Euler(55, -30, 0);
            var luz = luzGo.AddComponent<Light>();
            luz.type = LightType.Directional;
            luz.intensity = 1.2f;

            EditorSceneManager.SaveScene(escena, RutaEscena);
            var escenas = new List<EditorBuildSettingsScene> { new EditorBuildSettingsScene(RutaEscena, true) };
            foreach (var item in EditorBuildSettings.scenes)
                if (!string.Equals(item.path, RutaEscena, StringComparison.Ordinal)) escenas.Add(item);
            EditorBuildSettings.scenes = escenas.ToArray();
            Debug.Log("Recorrido de prueba creado sin referencias a contenido cultural.");
        }

        private static Material Material(string nombre, Color color)
        {
            var ruta = "Assets/Materials/" + nombre + ".mat";
            var existente = AssetDatabase.LoadAssetAtPath<Material>(ruta);
            if (existente != null) return existente;
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var material = new Material(shader) { color = color };
            AssetDatabase.CreateAsset(material, ruta);
            return material;
        }

        private static void Cubo(string nombre, Transform padre, Vector3 posicion, Vector3 escala, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = nombre;
            go.transform.SetParent(padre);
            go.transform.position = posicion;
            go.transform.localScale = escala;
            go.GetComponent<Renderer>().sharedMaterial = material;
        }

        private static void Asignar(UnityEngine.Object objeto, string propiedad, object valor)
        {
            var serializado = new SerializedObject(objeto);
            var campo = serializado.FindProperty(propiedad);
            if (campo == null) throw new InvalidOperationException("Campo no encontrado: " + propiedad);
            switch (valor)
            {
                case string texto: campo.stringValue = texto; break;
                case UnityEngine.Object referencia: campo.objectReferenceValue = referencia; break;
                default: throw new InvalidOperationException("Tipo no soportado: " + propiedad);
            }
            serializado.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
