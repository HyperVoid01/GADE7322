using UnityEngine;

[CreateAssetMenu(fileName = "BoidData", menuName = "ScriptableObjects/BoidData")]
public class BoidData : ScriptableObject
{
    [SerializeField] private float cohesionWeight;
    [SerializeField] private float separationWeight;
    [SerializeField] private float alignmentWeight;
    [SerializeField] private float perceptionRadius;
    [SerializeField] private float perceptionAngle;
    [SerializeField] private float obstacleAvoidanceWeight;
    [SerializeField] private float obstacleAvoidanceRadius;
    [SerializeField] private float moveSpeed;
    
    public float CohesionWeight => cohesionWeight;
    public float SeparationWeight => separationWeight;
    public float AlignmentWeight => alignmentWeight;
    public float PerceptionRadius => perceptionRadius;
    public float PerceptionAngle => perceptionAngle;
    public float ObstacleAvoidanceWeight => obstacleAvoidanceWeight;
    public float ObstacleAvoidanceRadius => obstacleAvoidanceRadius;
    public float MoveSpeed => moveSpeed;
}
