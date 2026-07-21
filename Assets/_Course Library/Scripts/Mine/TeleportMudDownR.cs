using System.Collections;
using System.Collections.Generic;
using UnityEngine;


public class TeleportMudDownR : MonoBehaviour
{
    [Tooltip("The anchor the player is teleported to")]
    public UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation.TeleportationAnchor anchor = null;

    [Tooltip("The provider used to request the teleportation")]
    public UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation.TeleportationProvider provider = null;

    public void Teleport()
    {
        if(anchor && provider)
        {
            UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation.TeleportRequest request = CreateRequest();
            provider.QueueTeleportRequest(request);
        }
    }

    private UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation.TeleportRequest CreateRequest()
    {
        Transform anchorTransform = anchor.teleportAnchorTransform;

        UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation.TeleportRequest request = new UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation.TeleportRequest()
        {
            requestTime = Time.time,
            matchOrientation = anchor.matchOrientation,

            destinationPosition = anchorTransform.position,
            destinationRotation = anchorTransform.rotation
        };

        return request;
    }

}
