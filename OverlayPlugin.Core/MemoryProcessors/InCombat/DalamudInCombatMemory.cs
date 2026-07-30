using System;

namespace RainbowMage.OverlayPlugin.MemoryProcessors.InCombat
{
    internal sealed class DalamudInCombatMemory : IInCombatMemory
    {
        private readonly IDalamudGameStateProvider provider;

        public DalamudInCombatMemory(IDalamudGameStateProvider provider)
        {
            this.provider = provider;
        }

        public bool GetInCombat()
            => provider.Snapshot.InGameCombat;

        public bool IsValid()
            => provider.Snapshot.GameExists;

        public Version GetVersion()
            => new Version(99, 0);

        public void ScanPointers()
        {
        }
    }
}
