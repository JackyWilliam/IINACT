using System;
using System.Collections.Generic;

namespace RainbowMage.OverlayPlugin.MemoryProcessors
{
    public interface IDalamudGameStateProvider
    {
        DalamudGameStateSnapshot Snapshot { get; }
    }

    public sealed class DalamudGameStateSnapshot
    {
        public static DalamudGameStateSnapshot Empty { get; } = new DalamudGameStateSnapshot(
            false,
            false,
            false,
            null,
            Array.Empty<DalamudPartyMember>());

        public DalamudGameStateSnapshot(
            bool gameExists,
            bool gameActive,
            bool inGameCombat,
            DalamudPartyMember player,
            IReadOnlyList<DalamudPartyMember> party)
        {
            GameExists = gameExists;
            GameActive = gameActive;
            InGameCombat = inGameCombat;
            Player = player;
            Party = party ?? Array.Empty<DalamudPartyMember>();
        }

        public bool GameExists { get; }
        public bool GameActive { get; }
        public bool InGameCombat { get; }
        public DalamudPartyMember Player { get; }
        public IReadOnlyList<DalamudPartyMember> Party { get; }
    }

    public sealed class DalamudPartyMember
    {
        public string Name { get; init; } = string.Empty;
        public uint EntityId { get; init; }
        public ulong ContentId { get; init; }
        public uint WorldId { get; init; }
        public byte JobId { get; init; }
        public byte Level { get; init; }
        public uint CurrentHp { get; init; }
        public uint MaxHp { get; init; }
        public ushort CurrentMp { get; init; }
        public ushort MaxMp { get; init; }
        public ushort TerritoryId { get; init; }
        public float PositionX { get; init; }
        public float PositionY { get; init; }
        public float PositionZ { get; init; }
        public float Rotation { get; init; }
    }
}
