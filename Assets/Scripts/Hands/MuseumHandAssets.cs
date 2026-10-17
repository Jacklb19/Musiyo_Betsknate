using UnityEngine;

namespace MusiyoBetsknate.Museum
{
    /// <summary>Hand models the headset shows, chosen at build time; no published content is stored here.</summary>
    public sealed class MuseumHandAssets : ScriptableObject
    {
        public const string ResourceName = "MuseumHandAssets";
        /// <summary>
        /// Hand driven joint by joint by hand tracking when the visitor puts the controllers down; a copy of it is held at
        /// each controller while the visitor holds one.
        /// </summary>
        public GameObject LeftTracked, RightTracked;
    }
}
