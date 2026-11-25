namespace Client.Main.Objects.Monsters
{
    // Same skeleton and animations as ForestOrc; only the skin differs.
    [NpcInfo(549, "Bloody Orc")]
    public class BloodyOrc : ForestOrc
    {
        protected override string ModelPath => "Monster/Monster194.bmd";
    }
}
