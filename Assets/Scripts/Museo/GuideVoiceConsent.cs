using System;
using System.Linq;
using MusiyoBetsknate.Museo;
using UnityEngine;

namespace MusiyoBetsknate.Museum
{
    /// <summary>
    /// The visitor's acceptance of how spoken questions are processed, shared by the two guides: accepting the notice once
    /// covers the hearth guide and the companion. It lasts for this run of the museum only, is never stored, and no longer
    /// covers anything when the service reports another processing or other providers.
    /// </summary>
    public static class GuideVoiceConsent
    {
        private static string accepted;

        /// <summary>The processing the visitor accepted is the one the service reports now.</summary>
        public static bool Covers(VoiceCapabilitiesContractV1 capabilities)
        {
            var signature = Signature(capabilities);
            return signature != null && signature == accepted;
        }

        public static void Accept(VoiceCapabilitiesContractV1 capabilities) => accepted = Signature(capabilities);

        /// <summary>Forgets the acceptance, as a new run of the museum does.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Forget() => accepted = null;

        private static string Signature(VoiceCapabilitiesContractV1 capabilities) => capabilities == null ? null
            : capabilities.processing + ":" + string.Join(",", (capabilities.providers ?? new string[0]).OrderBy(name => name, StringComparer.Ordinal));
    }
}
