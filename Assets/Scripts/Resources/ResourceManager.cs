using System;
using UnityEngine;

public class ResourceManager : MonoBehaviour
{
    [Header("Ores")]
    public int ironOre = 0;
    public int copperOre = 0;

    [Header("Coral")] 
    public int pearls = 0;

    [Header("Shipwrecks")] 
    public int wood = 0;
    public int gold = 0;
    
    public static ResourceManager Instance;
    
    [Serializable]
    public struct ResourceCost
    {
        public ResourceType resourceType;
        public int amount;
    }

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    public void AddResource(ResourceType resourceType, int amount)
    {
        switch (resourceType)
        {
            case ResourceType.IronOre:
                ironOre += amount;
                break;
            
            case ResourceType.CopperOre:
                copperOre += amount;
                break;
            
            case ResourceType.Pearl:
                pearls += amount;
                break;
            
            case ResourceType.Wood:
                wood += amount;
                break;
            
            case ResourceType.Gold:
                gold += amount;
                break;
        }
    }

    public int GetResourceAmount(ResourceType resourceType)
    {
        switch (resourceType)
        {
            case ResourceType.IronOre:
                return ironOre;
            
            case ResourceType.CopperOre:
                return copperOre;
            
            case ResourceType.Pearl:
                return pearls;
            
            case ResourceType.Wood:
                return wood;
            
            case ResourceType.Gold:
                return gold;
        }
        
        return 0;
    }
    
    public static string GetResourceName(ResourceType resourceType)
    {
        switch (resourceType)
        {
            case ResourceType.IronOre:   return "Iron";
            case ResourceType.CopperOre: return "Copper";
            case ResourceType.Pearl:     return "Pearls";
            case ResourceType.Wood:      return "Wood";
            case ResourceType.Gold:      return "Gold";
            default:                     return resourceType.ToString();
        }
    }
    
    public bool HasEnoughResources(ResourceCost[] costs)
    {
        foreach (ResourceCost cost in costs)
        {
            if (GetResourceAmount(cost.resourceType) < cost.amount)
                return false;
        }

        return true;
    }

    public void SpendResources(ResourceCost[] costs)
    {
        foreach (ResourceCost cost in costs)
        {
            SpendResource(cost.resourceType, cost.amount);
        }
    }

    private void SpendResource(ResourceType resourceType, int amount)
    {
        switch (resourceType)
        {
            case ResourceType.IronOre:
                ironOre -= amount;
                break;

            case ResourceType.CopperOre:
                copperOre -= amount;
                break;

            case ResourceType.Pearl:
                pearls -= amount;
                break;

            case ResourceType.Wood:
                wood -= amount;
                break;

            case ResourceType.Gold:
                gold -= amount;
                break;
        }
    }
}
