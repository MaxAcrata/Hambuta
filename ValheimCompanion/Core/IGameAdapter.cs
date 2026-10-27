using UnityEngine;

namespace ValheimCompanion.Core
{
    public enum SpawnStatus
    {
        NotAttempted,
        Success,
        Failed
    }

    public enum MoveJobStatus
    {
        None,
        Pending,
        Running,
        Succeeded,
        TimedOut,
        Aborted
    }

    /// <summary>Game-agnostic surface the brain uses. Implementations live in Adapters/.</summary>
    public interface IGameAdapter
    {
        SpawnStatus SpawnCompanion(bool useFallback);
        bool HasCompanion { get; }
        bool TryGetState(out CompanionEntity state);
        MoveJobStatus RequestMove(Vector3 destination);
        MoveJobStatus MoveStatus { get; }
        float MoveDistanceToTarget { get; }
        void Tick(float dt);
        void DespawnCompanion();
    }
}
