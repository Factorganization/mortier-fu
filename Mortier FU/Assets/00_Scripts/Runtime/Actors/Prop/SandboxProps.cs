using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

public class SandboxProps : MonoBehaviour
{
    private Vector3 _originPos;
    private readonly float _yOffset =0.1f ;
    [SerializeField] private Rigidbody rb;
    private CancellationTokenSource _cts;
    
    
    private void Awake()
    {
        _originPos = transform.position;
        
    }

    public async UniTask Respawn()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        
        gameObject.SetActive(false);
        await UniTask.WaitForFixedUpdate(_cts.Token);
        transform.position = (_originPos += new Vector3(0, _yOffset, 0));
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        await UniTask.WaitForFixedUpdate(_cts.Token);
        gameObject.SetActive(true);
        
        
    }

    private void OnDestroy()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }
}
