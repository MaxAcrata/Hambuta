using System.Threading.Tasks;

namespace ValheimCompanion.Core
{
    /// <summary>Decision DTO only. No HTTP, no prompts, no memory in this prototype.</summary>
    public sealed class BrainDecision
    {
        public string Action;
        public float X;
        public float Y;
        public float Z;
        public string Note;
    }

    /// <summary>Future MiMo provider implements this. Prototype uses <see cref="NullBrain"/>.</summary>
    public interface ICompanionBrain
    {
        Task<BrainDecision> DecideAsync(CompanionEntity state);
    }
}
