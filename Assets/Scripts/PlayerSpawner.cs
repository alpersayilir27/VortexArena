using UnityEngine;

/// <summary>
/// Places the camera rig at one of the assigned spawn points (picked at
/// random) when the game starts. Rotation is applied on the yaw axis only so
/// the floor-level tracking origin stays upright.
/// </summary>
public class PlayerSpawner : MonoBehaviour
{
    [SerializeField] private Transform[] spawnPoints;

    private void Start()
    {
        if (spawnPoints == null || spawnPoints.Length == 0)
        {
            Debug.LogWarning("PlayerSpawner: no spawn points assigned.", this);
            return;
        }

        Transform sp = spawnPoints[Random.Range(0, spawnPoints.Length)];
        transform.SetPositionAndRotation(sp.position, Quaternion.Euler(0f, sp.eulerAngles.y, 0f));
        Debug.Log($"PlayerSpawner: spawned at '{sp.name}'.");
    }
}
