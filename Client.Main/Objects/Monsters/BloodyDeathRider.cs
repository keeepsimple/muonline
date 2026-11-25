namespace Client.Main.Objects.Monsters
{
    // Same skeleton and animations as DeathRider; only the skin differs.
    [NpcInfo(550, "Bloody Death Rider")]
    public class BloodyDeathRider : DeathRider
    {
        protected override string ModelPath => "Monster/Monster195.bmd";
    }
}
