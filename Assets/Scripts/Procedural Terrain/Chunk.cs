using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
public class Chunk : MonoBehaviour
{
    public Vector3Int chunkCoord;
    [System.NonSerialized] public float[,,] densities;
}