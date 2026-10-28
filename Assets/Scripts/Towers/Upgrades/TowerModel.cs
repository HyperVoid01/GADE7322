using UnityEngine;

// Sits on the root of a tower's visual model. When a tower changes level, the old model is destroyed,
// the new one is spawned, and the tower re-reads its moving parts from here.
public class TowerModel : MonoBehaviour
{
    [Tooltip("Rotates left/right to face the enemy")]
    public Transform turretYaw;

    [Tooltip("Tilts up/down. Leave empty if this model has no barrel")]
    public bool useBarrel = true;
    public Transform barrel;

    [Tooltip("Where projectiles spawn / the harpoon rests / the lazer beam starts")]
    public Transform projectileSpawnPoint;

    [Tooltip("Lazer only: the beam for this level. Leave empty to keep using the beam on the tower root")]
    public LineRenderer beam;
}
