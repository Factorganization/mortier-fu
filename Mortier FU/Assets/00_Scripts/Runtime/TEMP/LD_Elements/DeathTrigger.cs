using Cysharp.Threading.Tasks;
using UnityEngine;
using MortierFu;

public enum E_GhostSpawnPolicy
{
    Default,
    LastSafeGround,
    ExplicitAnchor
}

public class DeathTrigger : MonoBehaviour
{
    [Header("Ghost Spawn")]
    [SerializeField] private E_GhostSpawnPolicy _ghostSpawnPolicy = E_GhostSpawnPolicy.LastSafeGround;
    [SerializeField] private Transform _ghostSpawnAnchor;

    public E_GhostSpawnPolicy GhostSpawnPolicy => _ghostSpawnPolicy;
    public Transform GhostSpawnAnchor => _ghostSpawnAnchor;

    private void OnTriggerEnter(Collider other)
    {
        Rigidbody rb = other.attachedRigidbody;

        if (rb != null && rb.TryGetComponent(out PlayerCharacter character))
            character.Health.TakeLethalDamage(this);

        if (rb != null && rb.TryGetComponent(out SandboxProps props))
        {
            props.Respawn().Forget();
        }
    }
}