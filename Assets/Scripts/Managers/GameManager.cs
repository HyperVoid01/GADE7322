using System;
using UnityEngine;

public class GameManager : MonoBehaviour
{
    [Header("Player Spawn")]
    [SerializeField] private GameObject playerPrefab;
    [SerializeField] private float spawnHeightOffset;
    
    [Header("Main Tower")]
    [SerializeField] private GameObject mainTowerPrefab;
    [SerializeField] private float mainTowerSpawnHeightOffset;

    [Header("Waves")] 
    public int waveNumber = 0;
    
    public static GameManager Instance;

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        
        MarchingCubes.Instance.OnTerrainGenerated += InitializeMainTower;
        MarchingCubes.Instance.OnTerrainGenerated += SpawnPlayer;
    }
    
    private void OnDestroy()
    {
        if (MarchingCubes.Instance != null)
        {
            MarchingCubes.Instance.OnTerrainGenerated -= SpawnPlayer;
            MarchingCubes.Instance.OnTerrainGenerated += InitializeMainTower;
        }
    }

    private void SpawnPlayer()
    {
        Vector3 spawnPosition = MarchingCubes.Instance.MapCenter + Vector3.up * spawnHeightOffset;
        Instantiate(playerPrefab, spawnPosition, Quaternion.identity);
        
        MarchingCubes.Instance.OnTerrainGenerated -= SpawnPlayer;
    }

    private void InitializeMainTower()
    {
        Vector3 spawnPosition = MarchingCubes.Instance.MapCenter + Vector3.up * mainTowerSpawnHeightOffset;
        Instantiate(mainTowerPrefab, spawnPosition, Quaternion.identity);
        
        MarchingCubes.Instance.OnTerrainGenerated -= InitializeMainTower;
    }
    
    public void IncrementWaveNumber()
    {
        waveNumber++;
        HUDManager.Instance.UpdateWaveNumberText(waveNumber);
    }

    public void EndGame()
    {
        Time.timeScale = 0;
        
        HUDManager.Instance.ShowGameOverUI();
    }
}