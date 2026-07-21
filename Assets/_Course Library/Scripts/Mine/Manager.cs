using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
public class Manager : MonoBehaviour
{
    [SerializeField] private GameObject[] Anchorsup;
    [SerializeField] private GameObject[] Anchorsdown;
    [SerializeField] private GameObject[] Buttons;
    public TeleportationProvider provider;

    private int linker;


    public void Matcher()
    {
        
    }
    // Start is called before the first frame update
    public void Teleport()
    {
        
            TeleportRequest request = CreateRequest();
            provider.QueueTeleportRequest(request);
        
    }

    private TeleportRequest CreateRequest()
    {
        //Transform anchorTransform = anchor.teleportAnchorTransform;

        TeleportRequest request = new TeleportRequest()
        {
            requestTime = Time.time,
          //  matchOrientation = anchor.matchOrientation,

          //  destinationPosition = anchorTransform.position,
          //  destinationRotation = anchorTransform.rotation
        };

        return request;
    }
    
    
    
}
