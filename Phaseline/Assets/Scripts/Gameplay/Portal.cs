using UnityEngine;

public class Portal : MonoBehaviour
{
    public Transform exit;
    public bool keepForward = true;

    void OnTriggerEnter(Collider other)
    {
        if (!exit) return;
        
        // Find the NetworkBiker root
        var biker = other.GetComponentInParent<NetworkBike>();
        if (biker)
        {
            Quaternion targetRot = keepForward ? exit.rotation : biker.transform.rotation;
            biker.Teleport(exit.position, targetRot);
        }
    }
}