namespace Client.Main.Objects.Monsters
{
    // Same skeleton and animations as Genocider; only the skin differs.
    [NpcInfo(556, "Genocider Warrior")]
    public class GenociderWarrior : Genocider
    {
        protected override string ModelPath => "Monster/Monster201.bmd";
    }
}
