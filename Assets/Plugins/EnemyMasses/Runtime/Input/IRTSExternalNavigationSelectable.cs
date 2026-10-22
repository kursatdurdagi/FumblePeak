using Arawn.EnemyMasses.Runtime.Navigation.Abstraction;

namespace Arawn.EnemyMasses.Runtime
{
    /// <summary>
    /// Optional extension for IRTSExternalSelectable implementations that can expose
    /// the unified navigation abstraction instead of only a NavMeshAgent.
    /// </summary>
    public interface IRTSExternalNavigationSelectable
    {
        INavigationAgent NavigationAgent { get; }
    }
}
