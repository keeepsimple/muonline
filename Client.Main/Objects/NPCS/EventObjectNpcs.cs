using Client.Main.Content;
using Client.Main.Controls;
using Client.Main.Objects.Player;
using Microsoft.Xna.Framework;
using System;
using System.Threading.Tasks;

namespace Client.Main.Objects.NPCS
{
    /// <summary>
    /// Event objects the server operates by position, not by talk: Castle Siege crown and switches count the
    /// player within one tile, a Crywolf altar is contracted by standing on its tile. Clicking walks the hero there.
    /// A null <see cref="ModelPath"/> means the map already draws the object (Crywolf statue and altars are
    /// EncTerrain35 objects at the NPC tiles); the NPC is then only the clickable, named marker.
    /// </summary>
    public abstract class PositionEventNpc : NPCObject
    {
        private DateTime _arrivalDeadline;

        public virtual string ModelPath => null;

        /// <summary>The tile the hero walks to: the object's own tile unless overridden.</summary>
        protected virtual Vector2 GetStandTile(Vector2 heroTile) => Tile(Location);

        /// <summary>Called once the hero stands on <see cref="GetStandTile"/> after a click.</summary>
        protected virtual void OnArrived() { }

        public override async Task Load()
        {
            if (ModelPath != null)
                Model = await BMDLoader.Instance.Prepare(ModelPath);
            await base.Load();
        }

        protected override void HandleClick()
        {
            if (World is not WalkableWorldControl { Walker: PlayerObject hero })
                return;

            var target = GetStandTile(Tile(hero.Location));
            if (Tile(hero.Location) == target)
            {
                OnArrived();
                return;
            }

            hero.MoveTo(target);
            _arrivalDeadline = DateTime.UtcNow.AddSeconds(15);
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);
            if (_arrivalDeadline == default || World is not WalkableWorldControl { Walker: PlayerObject hero })
                return;

            if (DateTime.UtcNow > _arrivalDeadline)
            {
                _arrivalDeadline = default;
                return;
            }

            if (!hero.IsMoving && Tile(hero.Location) == GetStandTile(Tile(hero.Location)))
            {
                _arrivalDeadline = default;
                OnArrived();
            }
        }

        protected static Vector2 Tile(Vector2 location) => new((int)location.X, (int)location.Y);
    }

    // Valley of Loren (Castle Siege)
    [NpcInfo(216, "Crown")]
    public class Crown : PositionEventNpc
    {
        public override string ModelPath => "NPC/NpcCrown.bmd";

        // The crown sits on its pedestal: stand on the neighbouring tile facing the hero.
        protected override Vector2 GetStandTile(Vector2 heroTile)
        {
            var own = Tile(Location);
            return own + new Vector2(Math.Sign(heroTile.X - own.X), Math.Sign(heroTile.Y - own.Y));
        }
    }

    [NpcInfo(217, "Crown Switch")] public class CrownSwitch1 : PositionEventNpc { public override string ModelPath => "NPC/NpcCheckFloor.bmd"; }
    [NpcInfo(218, "Crown Switch")] public class CrownSwitch2 : CrownSwitch1 { }

    // Crywolf Fortress
    [NpcInfo(204, "Wolf Statue")] public class WolfStatue : PositionEventNpc { protected override void HandleClick() { } }

    /// <summary>Standing on the altar and clicking it sends the contract request (0xBD/0x03) with the altar's id.</summary>
    public abstract class WolfAltar : PositionEventNpc
    {
        protected override void OnArrived() => _ = MuGame.Network?.GetCharacterService()?.SendCrywolfContractRequestAsync(NetworkId);
    }

    [NpcInfo(205, "Wolf Altar 1")] public class WolfAltar1 : WolfAltar { }
    [NpcInfo(206, "Wolf Altar 2")] public class WolfAltar2 : WolfAltar { }
    [NpcInfo(207, "Wolf Altar 3")] public class WolfAltar3 : WolfAltar { }
    [NpcInfo(208, "Wolf Altar 4")] public class WolfAltar4 : WolfAltar { }
    [NpcInfo(209, "Wolf Altar 5")] public class WolfAltar5 : WolfAltar { }

    /// <summary>
    /// Arena soccer ball: OpenMU moves it in the direction it is hit, so it is an attack target, not a talk NPC.
    /// ponytail: no model, 1.20.61 ships no ball model (no Ball/soccer file in Monster, NPC, Item or Object7);
    /// it is a named, attackable marker until a model is found.
    /// </summary>
    [NpcInfo(200, "Soccer Ball")]
    public class SoccerBall : MonsterObject
    {
        public SoccerBall()
        {
            BoundingBoxLocal = new BoundingBox(new Vector3(-30, -30, 0), new Vector3(30, 30, 60));
        }
    }
}
