using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using System.Collections.Generic;

public class TeleportRigUpMulti : MonoBehaviour
{
    [System.Serializable]
    public class ButtonTeleportPair
    {
        public string buttonName;
        public TeleportationAnchor anchor;
    }

    [Header("Teleport Anchors mapped by button name")]
    [SerializeField]
    private List<ButtonTeleportPair> teleportAnchors = new List<ButtonTeleportPair>();

    [Header("Teleportation Provider")]
    [SerializeField]
    private TeleportationProvider provider;

    public void TeleportTo(string buttonName)
    {
        foreach (var pair in teleportAnchors)
        {
            if (pair.buttonName == buttonName && pair.anchor != null && pair.anchor.teleportAnchorTransform != null)
            {
                TeleportRequest request = new TeleportRequest()
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