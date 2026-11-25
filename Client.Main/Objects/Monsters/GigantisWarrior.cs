namespace Client.Main.Objects.Monsters
{
    // Same skeleton and animations as Gigantis; only the skin differs.
    [NpcInfo(555, "Gigantis Warrior")]
    public class GigantisWarrior : Gigantis
    {
        protected override string ModelPath => "Monster/Monster200.bmd";
    }
}
