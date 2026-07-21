using UnityEngine;

namespace VRSIM.Presentation
{
    /// <summary>
    /// Drives a green/red indicator pair from a component state string.
    ///
    /// This replaces a 160-line method that applied five different mechanisms
    /// at once (SetActive, Button colours, Light components, renderer emission,
    /// UI Image colours) and decided which colour to use by checking whether
    /// the GameObject's *name* contained "green" or "red". A light named
    /// "Annular_Indicator_L" silently got no colour at all.
    ///
    /// Here the pairing is explicit, and colour comes from the role the object
    /// was assigned to, never from its name.
    /// </summary>
    [System.Serializable]
    public class StatusIndicator
    {
        public GameObject greenLight;
        public GameObject redLight;

        private static readonly Color GreenOn = new Color(0.25f, 0.95f, 0.35f);
        private static readonly Color RedOn = new Color(0.95f, 0.25f, 0.20f);

        /// <summary>
        /// Applies a protocol component state: open | closed | moving | fault.
        /// Unknown values leave both lights off rather than throwing -- the
        /// protocol requires receivers to tolerate an unrecognised enum.
        /// </summary>
        public void Apply(string state)
        {
            bool green = false, red = false;
            switch (state)
            {
                case "open": green = true; break;
                case "closed": red = true; break;
                // A ram in transit lights both, which reads as amber on a real
                // panel and matches how the operator's own console behaves.
                case "moving": green = true; red = true; break;
                case "fault": red = true; break;
            }
            Set(greenLight, green, GreenOn);
            Set(redLight, red, RedOn);
        }

        public void SetBoolean(bool on)
        {
            Set(greenLight, on, GreenOn);
            Set(redLight, !on, RedOn);
        }

        /// <summary>
        /// Turns one indicator on or off.
        ///
        /// Uses a MaterialPropertyBlock rather than touching renderer.material,
        /// which instantiates a fresh material per renderer per call and leaked
        /// one clone per light per update in the previous implementation.
        /// </summary>
        private static void Set(GameObject light, bool on, Color colour)
        {
            if (light == null) return;

            if (light.activeSelf != on) light.SetActive(on);
            if (!on) return; // nothing below is visible on an inactive object

            var renderer = light.GetComponent<Renderer>();
            if (renderer != null)
            {
                var block = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(block);
                block.SetColor("_BaseColor", colour);
                block.SetColor("_Color", colour);
                block.SetColor("_EmissionColor", colour * 2f);
                renderer.SetPropertyBlock(block);
            }

            var unityLight = light.GetComponent<Light>();
            if (unityLight != null)
            {
                unityLight.color = colour;
                unityLight.enabled = true;
            }
        }
    }
}
