using UnityEngine;

[CreateAssetMenu(menuName = "Scriptable Objects/Tower Data", fileName = "NewTowerData")]
public class TowerData : ScriptableObject
{
    public string displayName;
    public Sprite icon;
    public GameObject prefab;
    public ResourceManager.ResourceCost[] costs;
}