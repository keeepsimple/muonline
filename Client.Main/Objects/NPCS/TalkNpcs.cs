using Client.Main.Content;
using System.Threading.Tasks;

namespace Client.Main.Objects.NPCS
{
    /// <summary>
    /// NPCs whose only client logic is their model and the talk request: the server answers with the window
    /// (merchant, Lucky Coin, Loren Market warp, socket seeds, Illusion Temple storages, ...).
    /// Model paths are the 1.20.61 data names.
    /// </summary>
    public abstract class TalkNpc : NPCObject
    {
        public abstract string ModelPath { get; }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare(ModelPath);
            await base.Load();
        }

        protected override void HandleClick() => _ = MuGame.Network?.GetCharacterService()?.SendTalkToNpcRequestAsync(NetworkId);
    }

    // Elvenland
    [NpcInfo(415, "Silvia")] public class Silvia : TalkNpc { public override string ModelPath => "NPC/silvia.bmd"; }
    [NpcInfo(417, "Marce")] public class Marce : TalkNpc { public override string ModelPath => "NPC/marce.bmd"; }
    // Both socket NPCs are goblins: the bearded one (gbarbard texture) is the master, the seated one with the seed ball the researcher.
    [NpcInfo(452, "Seed Master")] public class SeedMaster : TalkNpc { public override string ModelPath => "NPC/goblinmaster.bmd"; }
    [NpcInfo(453, "Seed Researcher")] public class SeedResearcher : TalkNpc { public override string ModelPath => "NPC/seedgoblin.bmd"; }

    // Devias
    // Delgado's model is inferred (the only unassigned person-NPC model of that era in 1.20.61); swap it if it shows the wrong person.
    [NpcInfo(478, "Delgado")] public class Delgado : TalkNpc { public override string ModelPath => "NPC/monicaNPC.bmd"; }
    // Monster166 is Jerint the assistant (sub_jerent textures), shared with the Imperial Guardian boss.
    [NpcInfo(522, "Adviser Jerint")] public class AdviserJerint : TalkNpc { public override string ModelPath => "Monster/Monster166.bmd"; }

    // Loren Market
    [NpcInfo(545, "Christine the General Goods Merchant")] public class Christine : TalkNpc { public override string ModelPath => "NPC/UnitedMarketPlace_christine.bmd"; }
    [NpcInfo(546, "Jeweler Raul")] public class JewelerRaul : TalkNpc { public override string ModelPath => "NPC/UnitedMarkedPlace_raul.bmd"; }
    [NpcInfo(547, "Market Union Member Julia")] public class Julia : TalkNpc { public override string ModelPath => "NPC/UnitedMarkedPlace_julia.bmd"; }

    // Illusion Temple (NPC/CursedTemple models)
    [NpcInfo(380, "Stone Statue")] public class StoneStatue : TalkNpc { public override string ModelPath => "NPC/songsom.bmd"; }
    [NpcInfo(381, "MU Allies General")] public class MuAlliesGeneral : TalkNpc { public override string ModelPath => "NPC/allied.bmd"; }
    [NpcInfo(382, "Illusion Elder")] public class IllusionElder : TalkNpc { public override string ModelPath => "NPC/illusion.bmd"; }
    [NpcInfo(383, "Alliance Item Storage")] public class AllianceItemStorage : TalkNpc { public override string ModelPath => "NPC/songko.bmd"; }
    [NpcInfo(384, "Illusion Item Storage")] public class IllusionItemStorage : TalkNpc { public override string ModelPath => "NPC/songk2.bmd"; }

    // Valley of Loren: the guard in front of the Land of Trials (same model family as the guardsman knight/archer).
    [NpcInfo(220, "Guard")] public class CastleGuard : TalkNpc { public override string ModelPath => "Monster/Monster73.bmd"; }

    // Santa Village
    [NpcInfo(467, "Snowman")] public class Snowman : TalkNpc { public override string ModelPath => "NPC/snowman.bmd"; }

    /// <summary>The eight Little Santas share NPC/xmassanta.bmd and differ only by texture (xmassanta1..8).</summary>
    public abstract class LittleSanta : TalkNpc
    {
        protected abstract int Variant { get; }
        public override string ModelPath => "NPC/xmassanta.bmd";
        public string TexturePath => $"NPC/xmassanta{Variant}.jpg";

        public override async Task Load()
        {
            await base.Load();
            if (Model?.Meshes == null)
                return;

            // The model's own texture (xmassanta.jpg) is not shipped; every colour is an override.
            var texture = await TextureLoader.Instance.PrepareAndGetTexture(TexturePath);
            if (texture == null)
                return;
            for (int i = 0; i < Model.Meshes.Length; i++)
                SetMeshTextureOverride(i, texture);
        }
    }

    [NpcInfo(468, "Little Santa")] public class LittleSantaYellow : LittleSanta { protected override int Variant => 1; }
    [NpcInfo(469, "Little Santa")] public class LittleSantaGreen : LittleSanta { protected override int Variant => 2; }
    [NpcInfo(470, "Little Santa")] public class LittleSantaRed : LittleSanta { protected override int Variant => 3; }
    [NpcInfo(471, "Little Santa")] public class LittleSantaBlue : LittleSanta { protected override int Variant => 4; }
    [NpcInfo(472, "Little Santa")] public class LittleSantaWhite : LittleSanta { protected override int Variant => 5; }
    [NpcInfo(473, "Little Santa")] public class LittleSantaBlack : LittleSanta { protected override int Variant => 6; }
    [NpcInfo(474, "Little Santa")] public class LittleSantaOrange : LittleSanta { protected override int Variant => 7; }
    [NpcInfo(475, "Little Santa")] public class LittleSantaPink : LittleSanta { protected override int Variant => 8; }
}
