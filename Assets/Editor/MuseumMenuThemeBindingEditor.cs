using MusiyoBetsknate.Museum;
using UnityEditor;

namespace MusiyoBetsknate.Editor
{
    [CustomEditor(typeof(MuseumMenuThemeBinding))]
    public sealed class MuseumMenuThemeBindingEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox("Enabled bindings override the named style properties from MuseumExperienceConfiguration.json at runtime. Select None or disable the binding to edit that property here. Layout, hierarchy, fonts and unbound properties always belong to the prefab.", MessageType.Info);
            DrawDefaultInspector();
        }
    }
}
