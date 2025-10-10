using Zenject;

public sealed class ProjectInstaller : MonoInstaller
{
    public override void InstallBindings()
    {
        Container.Bind<IHelloService>().To<HelloService>().AsSingle();
    }
}