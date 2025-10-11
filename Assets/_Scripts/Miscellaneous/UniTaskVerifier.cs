using Cysharp.Threading.Tasks;
using UnityEngine;

public sealed class UniTaskVerifier : MonoBehaviour
{
    private async void Start()
    {
        while (true)
        {
            Debug.Log($"Tick {Time.frameCount}");
            await UniTask.Delay(1000); // 1 second delay, no GC
        }
    }
}
