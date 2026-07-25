namespace Advanced_Combat_Tracker;

public delegate void CombatToggleEventDelegate(bool isImport, CombatToggleEventArgs encounterInfo);

public sealed class CombatToggleEventArgs
{
    public CombatToggleEventArgs(bool isImport, EncounterData encounter)
    {
        this.isImport = isImport;
        this.encounter = encounter;
    }

    public bool isImport;
    public EncounterData encounter;
}
