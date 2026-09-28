using System;
using UnityEditor;
using UnityEditor.Build.Reporting;

namespace MusiyoBetsknate.Editor
{
    public static class ConstruirWebGLPrueba
    {
        public static void Construir()
        {
            const string escena = "Assets/Scenes/Recorrido_Prueba.unity";
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(escena) == null)
                throw new InvalidOperationException("Falta la escena de prueba.");

            var resultado = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { escena },
                locationPathName = "Build/RecorridoPruebaWebGL",
                target = BuildTarget.WebGL,
                options = BuildOptions.None
            });
            if (resultado.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException("El build WebGL falló: " + resultado.summary.result);

            UnityEngine.Debug.Log("Build WebGL de prueba completado en Build/RecorridoPruebaWebGL.");
        }
    }
}
