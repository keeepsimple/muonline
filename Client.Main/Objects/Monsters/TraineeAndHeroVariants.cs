namespace Client.Main.Objects.Monsters
{
    // Devil Square 7, Balgass Barracks/Refuge and Crywolf variants reuse the models, sounds and
    // animations of their base monsters (ids per OpenMU DevilSquare7, BarracksOfBalgass,
    // BalgassRefuge and CrywolfFortress map definitions).
    [NpcInfo(434, "Gigantis")] internal class GigantisDS7 : Gigantis { }
    [NpcInfo(436, "Balram (Trainee)")] internal class BalramTrainee : Balram { }
    [NpcInfo(437, "Soram (Trainee)")] internal class SoramTrainee : Soram { }
    [NpcInfo(438, "Persona")] internal class PersonaDS7 : Persona { }
    [NpcInfo(439, "Dreadfear")] internal class DreadfearDS7 : Dreadfear { }
    [NpcInfo(440, "Dark Elf")] internal class DarkElfDS7 : DarkElf { }

    [NpcInfo(409, "Balram (Trainee Soldier)")] internal class BalramTraineeSoldier : Balram { }
    [NpcInfo(410, "Death Spirit (Trainee Soldier)")] internal class DeathSpiritTraineeSoldier : DeathSpirit { }
    [NpcInfo(411, "Soram (Trainee Soldier)")] internal class SoramTraineeSoldier : Soram { }
    [NpcInfo(412, "Dark Elf (Trainee Soldier)")] internal class DarkElfTraineeSoldier : DarkElf { }

    // ponytail: Scout(Hero) model is a guess (Lance Scout, closest attack range 2 vs 3); swap base if the data says otherwise.
    [NpcInfo(314, "Scout (Hero)")] internal class ScoutHero : LanceScout { }
    [NpcInfo(315, "Werewolf (Hero)")] internal class WerewolfHero : Werewolf { }
    [NpcInfo(316, "Balram")] internal class BalramCrywolf : Balram { }
    [NpcInfo(317, "Soram")] internal class SoramCrywolf : Soram { }
}
