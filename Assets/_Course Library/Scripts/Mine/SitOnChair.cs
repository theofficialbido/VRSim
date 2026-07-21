using UnityEngine;


public class SitOnChair : MonoBehaviour
{
    public UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation.TeleportationAnchor anchor;
    public UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation.TeleportationProvider provider;

    public void Sit()
    {
        if (anchor && provider)
        {
            UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation.TeleportRequest request = new UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation.TeleportRequest
            {
                requestTime = Time.time,
                matchOrientation = anchor.matchOrientation,
                destinationPosition = anchor.teleportAnchorTransform.position,
                destinationRotation = anchor.teleportAnchorTransform.rotation
            };

            provider.QueueTeleportRequest(request);
        }
    }
}
