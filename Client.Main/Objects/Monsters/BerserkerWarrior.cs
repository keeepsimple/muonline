namespace Client.Main.Objects.Monsters
{
    // Same skeleton and animations as Berserk; only the skin differs.
    [NpcInfo(553, "Berserker Warrior")]
    public class BerserkerWarrior : Berserk
    {
        protected override string ModelPath => "Monster/Monster198.bmd";

        public BerserkerWarrior()
        {
            Scale = 1.15f;
        }
    }
}
