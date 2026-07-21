using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

public class SitOnChair : MonoBehaviour
{
    public TeleportationAnchor anchor;
    public TeleportationProvider provider;

    public void Sit()
    {
        if (anchor && provider)
        {
            TeleportRequest request = new TeleportRequest
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
