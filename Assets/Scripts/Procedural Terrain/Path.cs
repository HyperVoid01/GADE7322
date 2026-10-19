using UnityEngine;

[System.Serializable]
public class Path
{
    public Vector3 start;
    public Vector3 target;

    public Path(Vector3 start, Vector3 target)
    {
        this.start = start;
        this.target = target;
    }
}
