using Client.Main.Content;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(367, "Gateway Machine")]
    public class GatewayMachine : NPCObject
    {
        public GatewayMachine()
        {
            Scale = 4.76f;
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"NPC/to3gate.bmd");
            await base.Load();
            Position = new Microsoft.Xna.Framework.Vector3(Position.X - 20, Position.Y - 200, Position.Z);
        }

        protected override void HandleClick()
        {
            // The server answers with KanturuStateInfo (0xD1/0x00), which opens the entry dialog.
            var svc = MuGame.Network?.GetCharacterService();
            if (svc != null)
            {
                _ = svc.SendTalkToNpcRequestAsync(NetworkId);
            }
        }
    }
}