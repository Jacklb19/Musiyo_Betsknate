using System.Collections.Generic;
using System.Linq;
using MusiyoBetsknate.Dominio;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace MusiyoBetsknate.Editor
{
    /// <summary>Evita empaquetar los ScriptableObjects de prueba no publicables.</summary>
    public sealed class ValidarContenidoBuild : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            var entradas = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToList();
            entradas.AddRange(AssetDatabase.GetAllAssetPaths().Where(p => p.Contains("/Resources/")));
            var dependencias = new HashSet<string>(AssetDatabase.GetDependencies(entradas.ToArray(), true));
            foreach (var ruta in dependencias)
            {
                foreach (var objeto in AssetDatabase.LoadAllAssetsAtPath(ruta))
                {
                    if (objeto is ElementoCultural elemento && !elemento.EsDivulgablePublicamente)
                        throw new BuildFailedException("Contenido sin revisión publicable en el build: " + ruta
                            + ". Retire la referencia de las escenas/Resources y use datos autorizados del backend.");
                }
            }
        }
    }
}
