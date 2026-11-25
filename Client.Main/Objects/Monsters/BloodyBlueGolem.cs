namespace Client.Main.Objects.Monsters
{
    // Same skeleton and animations as BlueGolem; only the skin differs.
    [NpcInfo(551, "Bloody Golem")]
    public class BloodyBlueGolem : BlueGolem
    {
        protected override string ModelPath => "Monster/Monster196.bmd";
    }
}
