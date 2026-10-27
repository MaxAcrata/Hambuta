using System.Threading.Tasks;

namespace ValheimCompanion.Core
{
    /// <summary>No-op brain so the controller compiles. Does nothing.</summary>
    public sealed class NullBrain : ICompanionBrain
    {
        public Task<BrainDecision> DecideAsync(CompanionEntity state)
        {
            return Task.FromResult<BrainDecision>(null);
        }
    }
}
