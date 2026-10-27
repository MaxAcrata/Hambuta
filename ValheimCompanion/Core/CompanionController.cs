using UnityEngine;

namespace ValheimCompanion.Core
{
    /// <summary>
    /// Holds adapter + brain and exposes a single move-job state machine.
    /// Stage 4: spawn/state/status only. Move-job is wired in Stage 5.
    /// </summary>
    public sealed class CompanionController
    {
        private readonly IGameAdapter _adapter;
        private readonly ICompanionBrain _brain;

        public CompanionController(IGameAdapter adapter, ICompanionBrain brain)
        {
            _adapter = adapter;
            _brain = brain ?? new NullBrain();
        }

        public IGameAdapter Adapter => _adapter;

        public SpawnStatus Spawn(bool useFallback) => _adapter.SpawnCompanion(useFallback);

        public bool TryGetState(out CompanionEntity state) => _adapter.TryGetState(out state);

        public MoveJobStatus RequestMove(Vector3 destination) => _adapter.RequestMove(destination);

        public MoveJobStatus MoveStatus => _adapter.MoveStatus;

        public float MoveDistanceToTarget => _adapter.MoveDistanceToTarget;

        public void Tick(float dt) => _adapter.Tick(dt);

        public void Despawn() => _adapter.DespawnCompanion();
    }
}
