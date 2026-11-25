namespace Client.Main.Objects.Monsters
{
    // Same skeleton and animations as Kentauros; only the skin differs.
    [NpcInfo(554, "Kentauros Warrior")]
    public class KentaurosWarrior : Kentauros
    {
        protected override string ModelPath => "Monster/Monster199.bmd";
    }
}
