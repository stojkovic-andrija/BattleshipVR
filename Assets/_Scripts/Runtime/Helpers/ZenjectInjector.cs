using Zenject;
using UnityEngine;

/// <summary>Tried creating something to fix the zenject dependencies mixing with network objects, didn't work out.</summary>
public sealed class ZenjectInjector : MonoBehaviour
{

    public static DiContainer Container;
    public static event System.Action<System.Type, Object> OnInstanceBound;

    [Inject]
    private void Construct(DiContainer container)
    {
        Container = container;
    }

    public static void Inject(object target)
    {
        if (Container == null || target == null) return;
        Container.Inject(target);
    }

    /// <summary>Bind or replace a singleton with the live instance.</summary>
    public static void BindSingletonInstance<T>(T instance) where T : class
    {
        if (Container == null || instance == null) return;

        // Replace existing binding if present.
        if (Container.HasBinding<T>())
            Container.Unbind<T>();

        Container.Bind<T>().FromInstance(instance).AsSingle();

        // Optional: notify listeners (for late-wiring)
        OnInstanceBound?.Invoke(typeof(T), instance as Object);
    }

    public static bool TryResolve<T>(out T value) where T : class
    {
        value = (Container != null) ? Container.TryResolve<T>() : null;
        return value != null;
    }
}
