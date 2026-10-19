using UnityEngine;

[CreateAssetMenu(fileName = "NewTerrainSettings", menuName = "Scriptable Objects/Terrain Settings")]
public class TerrainSettings : ScriptableObject
{
    [Header("Surface")]
    [Range(0f, 1f)] public float isoLevel = 0.5f; // air to land ratio

    [Header("3D Perlin Noise")]
    public float noiseScale = 0.08f;
    [Range(1, 8)] public int octaves = 4;
    [Range(0f, 1f)] public float persistence = 0.5f; // amplitude falloff per octave
    public float lacunarity = 2f; // frequency growth per octave
    [Range(0f, 2f)] public float noiseContrast = 1.4f; // pushes values away from 0.5

    [Header("Terrain Shaping")]
    [Range(0f, 1f)] public float groundBlend = 0.5f; // 0 = pure caves, 1 = pure land
}
