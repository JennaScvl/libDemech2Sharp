namespace Demech2
{
    // The running install's edition and game, and the gameplay options, as the sim's edition branches test them.
    public interface IVariant
    {
        // Titanium (GameEditions.IsTitanium of the install), BattlePack (Titanium 8-bit's battle.dll), Mercenaries
        // (either edition), Ghost Bear's Legacy.
        bool IsTitanium { get; }
        bool IsBattlePack { get; }
        bool IsMercenaries { get; }
        bool IsGhostBearsLegacy { get; }

        // Titanium Edition's cockpit (its 34 panels and MFDs): Titanium, BattlePack, and (with Mercenaries built in)
        // Mercenaries.
        bool CockpitTitanium { get; }

        // Not the original's: MoonBunny Edition (its fixes on), and active this match; the weapons grouped by family.
        bool MoonBunnyEdition { get; }
        bool MoonBunnyActive { get; }
        bool AutoWeaponGroups { get; }

        // Not the original's: the running edition's weapon type nearest a MekNet match's (Titanium's) type p_type.
        int LocalWeaponType(int p_type);

        // Not the original's: the weapon types below this are Clan (every game's table puts them first); a chassis'
        // tech base.
        int ClanWeaponCount { get; }
        bool IsInnerSphere(string p_chassis);
    }
}
