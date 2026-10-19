using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;

// Creates one path particle effect per enemy path
public class PathParticleSpawner : MonoBehaviour
{
    [SerializeField] private PathParticleFollower particlePrefab;

    private bool _spawned;

    private void Start()
    {
        // Listen for the terrain finishing, and also try right away in case it already finished
        // before this script's Start ran
        MarchingCubes.Instance.OnTerrainGenerated += SpawnParticles;
        SpawnParticles();
    }

    private void OnDestroy()
    {
        if (MarchingCubes.Instance != null)
            MarchingCubes.Instance.OnTerrainGenerated -= SpawnParticles;
    }

    private void SpawnParticles()
    {
        if (_spawned || particlePrefab == null) return;

        List<SplineContainer> containers = MarchingCubes.Instance.GetPathSplineContainers();
        if (containers == null || containers.Count == 0) return; // Paths aren't generated yet

        _spawned = true;

        for (int i = 0; i < containers.Count; i++)
        {
            if (containers[i] == null) continue;

            PathParticleFollower follower = Instantiate(particlePrefab, transform);
            follower.name = $"PathParticles_{i}";
            follower.Initialize(containers[i]);
        }
    }
}