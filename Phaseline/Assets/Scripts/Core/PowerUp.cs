using FishNet.Object;
using System.Collections;
using UnityEngine;

public class PowerUp : NetworkBehaviour
{
    public enum Type { Boost, Shield }
    public Type powerUpType;

    [Header("Settings")]
    public float duration = 4f;
    public float boostAmount = 1.5f; 
    public float respawnTime = 10f;
    
    [Header("Visuals")]
    public GameObject visualModel;
    public Collider pickupCollider;

    // Ensure tag is correct
    public override void OnStartServer()
    {
        base.OnStartServer();
        if (!gameObject.CompareTag("PowerUp")) gameObject.tag = "PowerUp";
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsServerInitialized) return;

        // 1. Try to find a Networked Player Bike
        var netBike = other.GetComponentInParent<NetworkBike>();
        if (netBike != null)
        {
            ApplyToPlayer(netBike);
            Consume();
            return;
        }

        // 2. Try to find a Server-Side Bot
        var bot = other.GetComponentInParent<BotController>();
        if (bot != null)
        {
            ApplyToBot(bot);
            Consume();
            return;
        }
    }

    void ApplyToPlayer(NetworkBike bike)
    {
        if (powerUpType == Type.Boost)
            bike.ApplyBoost(duration, boostAmount);
        else if (powerUpType == Type.Shield)
            bike.ApplyShield(duration);
    }

    void ApplyToBot(BotController bot)
    {
        if (powerUpType == Type.Boost)
            bot.ApplyBoost(duration, boostAmount);
        else if (powerUpType == Type.Shield)
            bot.ApplyShield(duration);
    }

    void Consume()
    {
        // Hide visual/collider on all clients
        ObserversCollected();
        
        // Start respawn timer on server
        StopAllCoroutines();
        StartCoroutine(CoRespawn());
    }

    [ObserversRpc]
    void ObserversCollected()
    {
        if (visualModel) visualModel.SetActive(false);
        if (pickupCollider) pickupCollider.enabled = false;
    }

    [ObserversRpc]
    void ObserversRespawned()
    {
        if (visualModel) visualModel.SetActive(true);
        if (pickupCollider) pickupCollider.enabled = true;
    }

    IEnumerator CoRespawn()
    {
        // Ensure locally hidden on server too
        if (visualModel) visualModel.SetActive(false);
        if (pickupCollider) pickupCollider.enabled = false;

        yield return new WaitForSeconds(respawnTime);

        if (visualModel) visualModel.SetActive(true);
        if (pickupCollider) pickupCollider.enabled = true;
        
        ObserversRespawned();
    }
}