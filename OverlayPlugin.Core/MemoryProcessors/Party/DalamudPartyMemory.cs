using System;
using System.Linq;

namespace RainbowMage.OverlayPlugin.MemoryProcessors.Party
{
    internal sealed class DalamudPartyMemory : IPartyMemory
    {
        private const int PartyCapacity = 8;
        private readonly IDalamudGameStateProvider provider;

        public DalamudPartyMemory(IDalamudGameStateProvider provider)
        {
            this.provider = provider;
        }

        public PartyListsStruct GetPartyLists()
        {
            var members = provider.Snapshot.Party
                .Take(PartyCapacity)
                .Select(member => new PartyListEntry
                {
                    x = member.PositionX,
                    y = member.PositionY,
                    z = member.PositionZ,
                    contentId = unchecked((long)member.ContentId),
                    objectId = member.EntityId,
                    currentHP = member.CurrentHp,
                    maxHP = member.MaxHp,
                    currentMP = member.CurrentMp,
                    maxMP = member.MaxMp,
                    territoryType = member.TerritoryId,
                    homeWorld = unchecked((ushort)member.WorldId),
                    name = member.Name,
                    classJob = member.JobId,
                    level = member.Level,
                    flags = 0x13,
                })
                .ToArray();

            return new PartyListsStruct
            {
                memberCount = unchecked((byte)members.Length),
                partyLeaderIndex = 0,
                partyMembers = Pad(members),
                alliance1Members = EmptyParty(),
                alliance2Members = EmptyParty(),
                alliance3Members = EmptyParty(),
                alliance4Members = EmptyParty(),
                alliance5Members = EmptyParty(),
            };
        }

        public bool IsValid()
            => provider.Snapshot.GameExists;

        public Version GetVersion()
            => new Version(99, 0);

        public void ScanPointers()
        {
        }

        private static PartyListEntry[] Pad(PartyListEntry[] members)
        {
            var result = EmptyParty();
            Array.Copy(members, result, members.Length);
            return result;
        }

        private static PartyListEntry[] EmptyParty()
            => new PartyListEntry[PartyCapacity];
    }
}
