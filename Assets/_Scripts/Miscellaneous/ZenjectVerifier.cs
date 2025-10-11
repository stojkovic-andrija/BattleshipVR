using UnityEngine;
using Zenject;

public interface IHelloService
{
    void SayHello();
}

public sealed class HelloService : IHelloService
{
    public void SayHello() => Debug.Log("Zenject Hello world");
}


public sealed class ZenjectVerifier : MonoBehaviour
{
    [Inject] private IHelloService _service;
    private void Start()
    {
        _service.SayHello();
    }
}

