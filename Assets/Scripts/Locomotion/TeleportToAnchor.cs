using UnityEngine;
using Teleportation = UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

namespace VRSIM.Locomotion
{
    /// <summary>
    /// Sends the player to a teleport anchor. One script for every destination,
    /// replacing twelve near-identical classes that differed only in their name.
    ///
    /// Two ways to use it, both supported at once:
    ///
    /// <b>A component per button.</b> Drop this on the button, drag its anchor
    /// into <see cref="anchor"/>, and point the button's OnClick at
    /// <see cref="Teleport"/>. Selecting a button then shows where it goes.
    ///
    /// <b>One component for the whole panel.</b> Put a single instance anywhere
    /// and point every button's OnClick at <see cref="TeleportTo"/>, dragging
    /// that button's anchor into the argument slot on the OnClick row. Unity
    /// passes Object arguments to persistent listeners, so the destination lives
    /// on the button with no per-button component at all.
    ///
    /// The provider is found automatically, which removes the twelve identical
    /// drags the old scripts each needed.
    /// </summary>
    public class TeleportToAnchor : MonoBehaviour
    {
        [Header("Destination")]
        [Tooltip("Where this button sends the player. Leave empty if you are " +
                 "passing the anchor through the button's OnClick argument instead.")]
        public Teleportation.TeleportationAnchor anchor;

        [Header("Provider")]
        [Tooltip("Left empty, the provider in the scene is found automatically.")]
        public Teleportation.TeleportationProvider provider;

        [Header("Diagnostics")]
        [Tooltip("Log each teleport, for checking a button goes where you expect.")]
        public bool verbose;

        private void Awake()
        {
            if (provider == null) provider = FindAnyObjectByType<Teleportation.TeleportationProvider>();
        }

        /// <summary>Go to the anchor assigned on this component.</summary>
        public void Teleport() => TeleportTo(anchor);

        /// <summary>
        /// Go to a specific anchor. Wire a button's OnClick to this and drag the
        /// destination into the argument slot to avoid a component per button.
        /// </summary>
        public void TeleportTo(Teleportation.TeleportationAnchor target)
        {
            if (target == null)
            {
                // Naming the object matters: a dead teleport button is otherwise
                // indistinguishable from one whose destination is unreachable.
                Debug.LogWarning($"[TeleportToAnchor] '{name}' has no destination anchor.", this);
                return;
            }

            if (provider == null) provider = FindAnyObjectByType<Teleportation.TeleportationProvider>();
            if (provider == null)
            {
                Debug.LogWarning(
                    $"[TeleportToAnchor] '{name}': no TeleportationProvider in the scene, " +
                    "so nothing can be teleported.", this);
                return;
            }

            var destination = target.teleportAnchorTransform;
            if (destination == null)
            {
                Debug.LogWarning(
                    $"[TeleportToAnchor] '{name}': anchor '{target.name}' has no " +
                    "teleportAnchorTransform assigned.", this);
                return;
            }

            provider.QueueTeleportRequest(new Teleportation.TeleportRequest
            {
                requestTime = Time.time,
                // Taken from the anchor, so orientation is configured where the
                // destination is, not duplicated on every button.
                matchOrientation = target.matchOrientation,
                destinationPosition = destination.position,
                destinationRotation = destination.rotation,
            });

            if (verbose)
                Debug.Log($"[TeleportToAnchor] '{name}' -> '{target.name}' at {destination.position}", this);
        }
    }
}
