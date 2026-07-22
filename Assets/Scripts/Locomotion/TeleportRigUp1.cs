using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

namespace VRSIM.Locomotion
{
    /// <summary>
    /// Moves the player to a fixed anchor when <see cref="Teleport"/> is called,
    /// normally from a rig button's UnityEvent.
    ///
    /// The destination is not in this script -- it is whatever
    /// <see cref="anchor"/> is set to in the inspector. The class name records
    /// which button it happens to be wired to, nothing more.
    ///
    /// The original version failed silently: an unassigned anchor or provider
    /// made the whole call a no-op with nothing in the console, which is
    /// indistinguishable from a dead button, an unwired UnityEvent or a broken
    /// interactor. Every failure here now says which slot is empty, because a
    /// teleport that does nothing is the single hardest thing to diagnose in a
    /// headset -- you cannot inspect anything while wearing one.
    /// </summary>
    public class TeleportRigUp1 : MonoBehaviour
    {
        [Tooltip("The anchor the player is teleported to")]
        public TeleportationAnchor anchor = null;

        [Tooltip("The provider used to request the teleportation")]
        public TeleportationProvider provider = null;

        [Tooltip("Ignore repeat presses for this long. Guards against a held " +
                 "button firing every frame and against both hands hitting it at once.")]
        public float cooldownSeconds = 0.5f;

        float _lastTeleportTime = float.NegativeInfinity;

        public void Teleport()
        {
            if (anchor == null || provider == null)
            {
                Debug.LogWarning(
                    $"[{nameof(TeleportRigUp1)}] Teleport ignored on '{name}': " +
                    $"anchor={(anchor == null ? "MISSING" : anchor.name)}, " +
                    $"provider={(provider == null ? "MISSING" : provider.name)}. " +
                    "Assign both in the inspector.", this);
                return;
            }

            if (Time.time - _lastTeleportTime < cooldownSeconds)
                return;

            // teleportAnchorTransform is optional on the anchor; when it is left
            // empty the anchor's own transform is the destination. Reading it
            // blindly used to send the player to the world origin.
            Transform destination = anchor.teleportAnchorTransform != null
                ? anchor.teleportAnchorTransform
                : anchor.transform;

            var request = new TeleportRequest
            {
                requestTime = Time.time,
                matchOrientation = anchor.matchOrientation,
                destinationPosition = destination.position,
                destinationRotation = destination.rotation,
            };

            if (!provider.QueueTeleportRequest(request))
            {
                Debug.LogWarning(
                    $"[{nameof(TeleportRigUp1)}] Provider '{provider.name}' rejected the " +
                    "request. Check that it has a Locomotion Mediator and that no other " +
                    "locomotion provider currently holds the lock.", this);
                return;
            }

            _lastTeleportTime = Time.time;
        }
    }
}
