using UnityEngine;

using System.Collections.Generic;

public class TeleportRigUpMulti : MonoBehaviour
{
    [System.Serializable]
    public class ButtonTeleportPair
    {
        public string buttonName;
        public UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation.TeleportationAnchor anchor;
    }

    [Header("Teleport Anchors mapped by button name")]
    [SerializeField]
    private List<ButtonTeleportPair> teleportAnchors = new List<ButtonTeleportPair>();

    [Header("Teleportation Provider")]
    [SerializeField]
    private UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation.TeleportationProvider provider;

    public void TeleportTo(string buttonName)
    {
        foreach (var pair in teleportAnchors)
        {
            if (pair.buttonName == buttonName && pair.anchor != null && pair.anchor.teleportAnchorTransform != null)
            {
                UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation.TeleportRequest request = new UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation.TeleportRequest()
                {
                    requestTime = Time.time,
                    matchOrientation = pair.anchor.matchOrientation,
                    destinationPosition = pair.anchor.teleportAnchorTransform.position,
                    destinationRotation = pair.anchor.teleportAnchorTransform.rotation
                };

                provider.QueueTeleportRequest(request);
                Debug.Log($"Teleporting to {buttonName} at {pair.anchor.teleportAnchorTransform.position}");
                return;
            }
        }

        Debug.LogWarning($"No teleport anchor found for button: {buttonName}");
    }
}