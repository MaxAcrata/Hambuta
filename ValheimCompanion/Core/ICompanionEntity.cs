using UnityEngine;

namespace ValheimCompanion.Core
{
    /// <summary>Game-agnostic snapshot of a companion entity.</summary>
    public sealed class CompanionEntity
    {
        public string PrefabName;
        public string DisplayName;
        public string Zdoid;
        public Vector3 Position;
        public float Health;
        public float MaxHealth;
        public bool IsTamed;
        public bool IsDead;
        public bool IsValid;
        public bool IsOwner;
        public bool IsOnGround;
        public bool IsSwimming;
        public string Faction;
        public float DistanceToPlayer;
        public string FollowTarget;
    }
}
