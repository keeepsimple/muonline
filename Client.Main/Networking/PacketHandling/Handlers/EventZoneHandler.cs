using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.Threading.Tasks;
using Client.Main.Controls;
using Client.Main.Controls.UI;
using Client.Main.Controls.UI.Game;
using Client.Main.Core.Client;
using Client.Main.Core.Utilities;
using Client.Main.Models;
using Client.Main.Objects.Effects;
using Client.Main.Objects.Monsters;
using Client.Main.Scenes;
using Microsoft.Extensions.Logging;
using Microsoft.Xna.Framework;

namespace Client.Main.Networking.PacketHandling.Handlers
{
    /// <summary>
    /// Kanturu (0xD1) and Crywolf (0xBD) event packets: the Kanturu entry dialog and battle HUD data, the Crywolf
    /// statue/altar/Balgass HUD data and result. KanturuStateInfo, KanturuStateChange and CrywolfInfo are registered in
    /// <see cref="MiscGamePacketHandler"/> (music) and forward to the static <c>Apply*</c> methods here.
    /// The Raklion state packets (0xD1/0x10-0x13) drive the Raklion lines of the event-zone HUD.
    /// Also the event item NPCs: Lucky Coins at Delgado (0xBF/0x0B-0x0D) and the Golden Archer registration (0x94).
    /// </summary>
    public class EventZoneHandler : IGamePacketHandler
    {
        private static readonly ILogger Logger = (ILogger)MuGame.AppLoggerFactory?.CreateLogger<EventZoneHandler>()
            ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

        /// <summary>Event feedback in the chat log, for the events and NPCs without a window of their own.</summary>
        internal static void ShowSystemMessage(string text)
        {
            MuGame.ScheduleOnMainThread(() =>
                (MuGame.Instance?.ActiveScene as GameScene)?.ChatLog?.AddMessage("System", text, MessageType.System));
        }

        // ──────────────────────────── Shared packets (called from MiscGamePacketHandler) ────────────────────────────

        /// <summary>0xD1/0x00 KanturuStateInfo: opens the Gateway Machine dialog.</summary>
        public static void ApplyKanturuStateInfo(ReadOnlySpan<byte> span)
        {
            if (span.Length < 12) return;
            byte state = span[4];
            bool canEnter = span[6] != 0;
            int users = span[7];
            uint remain = BinaryPrimitives.ReadUInt32LittleEndian(span[8..]);
            MuGame.ScheduleOnMainThread(() => KanturuGatewayWindow.Instance.ShowInfo(state, canEnter, users, remain));
        }

        /// <summary>0xD1/0x03 KanturuStateChange: shows or hides the battle HUD.</summary>
        public static void ApplyKanturuStateChange(ReadOnlySpan<byte> span)
        {
            if (span.Length < 6) return;
            byte state = span[4];
            byte detail = span[5];
            MuGame.ScheduleOnMainThread(() => KanturuEventState.SetState(state, detail));
        }

        /// <summary>0xBD/0x00 CrywolfInfo: event phase; the result of the event also asks for the chaos rate benefit.</summary>
        public static void ApplyCrywolfInfo(ReadOnlySpan<byte> span)
        {
            if (span.Length < 6) return;
            byte occupation = span[4];
            byte state = span[5];
            MuGame.ScheduleOnMainThread(() =>
            {
                CrywolfEventState.SetState(occupation, state);
                // The benefit applies once the occupation is decided; the server only answers on request.
                if (state is 5 or 6)
                    _ = MuGame.Network?.GetCharacterService()?.SendCrywolfChaosRateBenefitRequestAsync();
            });
        }

        // ───────────────────────────────────────── Kanturu ─────────────────────────────────────────

        [PacketHandler(0xD1, 0x01)] // KanturuEnterResult (S2C)
        public Task HandleKanturuEnterResultAsync(Memory<byte> packet)
        {
            if (packet.Length < 5) return Task.CompletedTask;
            bool success = packet.Span[4] == 1;
            MuGame.ScheduleOnMainThread(() =>
            {
                if (success)
                    KanturuGatewayWindow.Instance.Hide();
                else
                    RequestDialog.ShowInfo("You cannot enter Kanturu right now.");
            });
            return Task.CompletedTask;
        }

        [PacketHandler(0xD1, 0x04)] // KanturuBattleResult (S2C)
        public Task HandleKanturuBattleResultAsync(Memory<byte> packet)
        {
            if (packet.Length < 5) return Task.CompletedTask;
            bool victory = packet.Span[4] == 1;
            MuGame.ScheduleOnMainThread(() => KanturuEventState.SetResult(victory));
            return Task.CompletedTask;
        }

        [PacketHandler(0xD1, 0x05)] // KanturuTimeLimit (S2C)
        public Task HandleKanturuTimeLimitAsync(Memory<byte> packet)
        {
            if (packet.Length < 8) return Task.CompletedTask;
            uint milliseconds = BinaryPrimitives.ReadUInt32LittleEndian(packet.Span[4..]);
            MuGame.ScheduleOnMainThread(() => KanturuEventState.SetTimeLimit(milliseconds));
            return Task.CompletedTask;
        }

        [PacketHandler(0xD1, 0x06)] // KanturuMayaWideAreaAttack (S2C): cosmetic, 0 = storm, 1 = stone rain
        public Task HandleKanturuMayaWideAreaAttackAsync(Memory<byte> packet)
        {
            if (packet.Length < 7) return Task.CompletedTask;
            bool storm = packet.Span[6] == 0;
            Logger.LogDebug("Maya wide-area attack: {Type}", storm ? "storm" : "stone rain");
            MuGame.ScheduleOnMainThread(() =>
            {
                try
                {
                    // ponytail: only the Maya body attack animation; the storm / stone rain particles are not ported.
                    int animated = 0;
                    if (MuGame.Instance?.ActiveScene?.World is WorldControl world)
                    {
                        double now = MuGame.Instance.GameTime?.TotalGameTime.TotalSeconds ?? 0;
                        var action = storm ? MonsterActionType.Attack1 : MonsterActionType.Attack2;
                        foreach (var maya in world.Objects.OfType<Maya>().ToList())
                            if (maya.ApplyMonsterSkill(action, 0, 0, now, null)) animated++;
                    }

                    if (animated == 0)
                        ShowSystemMessage(storm ? "Maya unleashes a storm!" : "Maya calls down a rain of stones!");
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Error showing the Maya wide-area attack.");
                }
            });
            return Task.CompletedTask;
        }

        [PacketHandler(0xD1, 0x07)] // KanturuMonsterUserCount (S2C)
        public Task HandleKanturuMonsterUserCountAsync(Memory<byte> packet)
        {
            if (packet.Length < 6) return Task.CompletedTask;
            int monsters = packet.Span[4];
            int users = packet.Span[5];
            MuGame.ScheduleOnMainThread(() => KanturuEventState.SetCounts(monsters, users));
            return Task.CompletedTask;
        }

        // ───────────────────────────────────────── Crywolf ─────────────────────────────────────────

        [PacketHandler(0xBD, 0x02)] // CrywolfStatueAndAltarInfo (S2C)
        public Task HandleCrywolfStatueAndAltarInfoAsync(Memory<byte> packet)
        {
            if (packet.Length < 13) return Task.CompletedTask;
            uint shield = BinaryPrimitives.ReadUInt32LittleEndian(packet.Span[4..]);
            byte[] altars = packet.Span.Slice(8, CrywolfEventState.AltarCount).ToArray();
            MuGame.ScheduleOnMainThread(() => CrywolfEventState.SetStatueAndAltars(shield, altars));
            return Task.CompletedTask;
        }

        [PacketHandler(0xBD, 0x03)] // CrywolfContractResult (S2C): altar key = 317 + altar index
        public Task HandleCrywolfContractResultAsync(Memory<byte> packet)
        {
            if (packet.Length < 8) return Task.CompletedTask;
            bool success = packet.Span[4] == 1;
            byte altarState = packet.Span[5];
            int altar = BinaryPrimitives.ReadUInt16BigEndian(packet.Span[6..]) - 317;
            MuGame.ScheduleOnMainThread(() => CrywolfEventState.SetAltarState(altar, altarState));
            ShowSystemMessage(success
                ? $"You are now the guardian of altar {altar + 1}. Pray to protect the Wolf Statue."
                : $"The contract with altar {altar + 1} failed.");
            return Task.CompletedTask;
        }

        [PacketHandler(0xBD, 0x04)] // CrywolfLeftTime (S2C)
        public Task HandleCrywolfLeftTimeAsync(Memory<byte> packet)
        {
            if (packet.Length < 6) return Task.CompletedTask;
            byte hours = packet.Span[4];
            byte minutes = packet.Span[5];
            MuGame.ScheduleOnMainThread(() => CrywolfEventState.SetLeftTime(hours, minutes));
            return Task.CompletedTask;
        }

        [PacketHandler(0xBD, 0x05)] // CrywolfBossMonsterInfo (S2C)
        public Task HandleCrywolfBossMonsterInfoAsync(Memory<byte> packet)
        {
            if (packet.Length < 9) return Task.CompletedTask;
            int balgass = BinaryPrimitives.ReadInt32LittleEndian(packet.Span[4..]);
            byte darkElves = packet.Span[8];
            MuGame.ScheduleOnMainThread(() => CrywolfEventState.SetBossInfo(balgass, darkElves));
            return Task.CompletedTask;
        }

        [PacketHandler(0xBD, 0x07)] // CrywolfPersonalRank (S2C)
        public Task HandleCrywolfPersonalRankAsync(Memory<byte> packet)
        {
            if (packet.Length < 12) return Task.CompletedTask;
            byte rank = packet.Span[4];
            uint experience = BinaryPrimitives.ReadUInt32LittleEndian(packet.Span[8..]);
            MuGame.ScheduleOnMainThread(() => CrywolfEventState.SetPersonalRank(rank, experience));
            return Task.CompletedTask;
        }

        [PacketHandler(0xBD, 0x08)] // CrywolfHeroList (S2C)
        public Task HandleCrywolfHeroListAsync(Memory<byte> packet)
        {
            const int HeroLength = 20;
            if (packet.Length < 5) return Task.CompletedTask;
            var span = packet.Span;
            int count = Math.Min((int)span[4], 5);
            var heroes = new List<CrywolfEventState.Hero>(count);
            for (int i = 0; i < count && 5 + (i + 1) * HeroLength <= span.Length; i++)
            {
                var item = span.Slice(5 + i * HeroLength, HeroLength);
                int end = item.Slice(1, 10).IndexOf((byte)0);
                string name = Encoding.UTF8.GetString(item.Slice(1, end < 0 ? 10 : end));
                heroes.Add(new CrywolfEventState.Hero(name, BinaryPrimitives.ReadUInt32LittleEndian(item[12..]), item[16]));
            }

            MuGame.ScheduleOnMainThread(() => CrywolfEventState.SetHeroes(heroes));
            return Task.CompletedTask;
        }

        [PacketHandler(0xBD, 0x0C)] // CrywolfRegionMonsterAttack (S2C): a ballista shoots at a point
        public Task HandleCrywolfRegionMonsterAttackAsync(Memory<byte> packet)
        {
            if (packet.Length < 10) return Task.CompletedTask;
            var span = packet.Span;
            byte sourceX = span[6], targetX = span[8], targetY = span[9];
            MuGame.ScheduleOnMainThread(() =>
            {
                try
                {
                    if (MuGame.Instance?.ActiveScene?.World is not WalkableWorldControl world || world.Terrain == null) return;
                    // Like the reference client only the ballista x is used: the arrow comes in from its side.
                    float endX = (targetX + 0.5f) * Constants.TERRAIN_SCALE, endY = (targetY + 0.5f) * Constants.TERRAIN_SCALE;
                    var end = new Vector3(endX, endY, world.Terrain.RequestTerrainHeight(endX, endY));
                    var start = new Vector3((sourceX + 0.5f) * Constants.TERRAIN_SCALE, endY, end.Z + 400f);
                    var arrow = new MonsterArrowProjectileEffect(start, end, "Skill/Arrow01.bmd", 2f);
                    world.Objects.Add(arrow);
                    _ = arrow.Load();
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Error showing the Crywolf ballista attack.");
                }
            });
            return Task.CompletedTask;
        }

        [PacketHandler(0xBD, 0x09)] // CrywolfBenefitPlusChaosRate (S2C)
        public Task HandleCrywolfChaosRateBenefitAsync(Memory<byte> packet)
        {
            if (packet.Length < 5) return Task.CompletedTask;
            byte percent = packet.Span[4];
            MuGame.ScheduleOnMainThread(() => CrywolfEventState.SetChaosRateBenefit(percent));
            return Task.CompletedTask;
        }

        // ───────────────────────────────────────── Raklion ─────────────────────────────────────────

        [PacketHandler(0xD1, 0x10)] // RaklionStateInfo (S2C): answer to the state request
        public Task HandleRaklionStateInfoAsync(Memory<byte> packet)
        {
            if (packet.Length < 6) return Task.CompletedTask;
            byte state = packet.Span[4];
            byte selupan = packet.Span[5];
            MuGame.ScheduleOnMainThread(() =>
            {
                RaklionEventState.SetState(state, selupan);
                RaklionEventState.SetState(RaklionEventState.DetailStateMarker, selupan);
            });
            return Task.CompletedTask;
        }

        [PacketHandler(0xD1, 0x11)] // RaklionCurrentState (S2C): sent when entering a Raklion map
        public Task HandleRaklionCurrentStateAsync(Memory<byte> packet)
        {
            if (packet.Length < 6) return Task.CompletedTask;
            byte state = packet.Span[4];
            byte detail = packet.Span[5];
            MuGame.ScheduleOnMainThread(() => RaklionEventState.SetState(state, detail));
            return Task.CompletedTask;
        }

        [PacketHandler(0xD1, 0x12)] // RaklionStateChange (S2C); the server announces the changes in the chat itself
        public Task HandleRaklionStateChangeAsync(Memory<byte> packet)
        {
            if (packet.Length < 6) return Task.CompletedTask;
            byte state = packet.Span[4];
            byte detail = packet.Span[5];
            Logger.LogDebug("Raklion state change: {State}/{Detail}", state, detail);
            MuGame.ScheduleOnMainThread(() => RaklionEventState.SetState(state, detail));
            return Task.CompletedTask;
        }

        [PacketHandler(0xD1, 0x13)] // RaklionBattleResult (S2C)
        public Task HandleRaklionBattleResultAsync(Memory<byte> packet)
        {
            if (packet.Length < 5) return Task.CompletedTask;
            bool success = packet.Span[4] == 1;
            MuGame.ScheduleOnMainThread(() => RaklionEventState.SetBattleResult(success));
            ShowSystemMessage(success ? "Selupan has been defeated!" : "The battle against Selupan has failed.");
            return Task.CompletedTask;
        }

        // ─────────────────────────────────────── Event items ───────────────────────────────────────

        [PacketHandler(0xBF, 0x0B)] // LuckyCoinCountResponse (S2C): opening the Delgado registration dialog
        public Task HandleLuckyCoinCountAsync(Memory<byte> packet)
        {
            if (packet.Length < 8) return Task.CompletedTask;
            uint count = BinaryPrimitives.ReadUInt32LittleEndian(packet.Span[4..]);
            MuGame.ScheduleOnMainThread(() => EventItemState.LuckyCoinsRegistered = count);
            return Task.CompletedTask;
        }

        public static string DescribeLuckyCoinRegistration(byte result, uint count) => result switch
        {
            1 => $"The Lucky Coin was registered. Registered Lucky Coins: {count}.",
            0 => "You have no Lucky Coin in your inventory.",
            100 => "This account has already applied.",
            _ => $"The Lucky Coin registration failed ({result}).",
        };

        public static string DescribeLuckyCoinExchange(byte result) => result switch
        {
            1 => "The Lucky Coins were exchanged.",
            0 => "You don't have enough Lucky Coins.",
            2 => "Your inventory needs a free space of 2 x 4.",
            _ => $"The Lucky Coin exchange failed ({result}).",
        };

        // Season 6 gets LuckyCoinRegistrationResponse (enum result) from LuckyCoinViewPlugIn; the older
        // LuckyCoinRegistrationResult shares the code and layout, so one parser covers both.
        [PacketHandler(0xBF, 0x0C)]
        public Task HandleLuckyCoinRegistrationAsync(Memory<byte> packet)
        {
            if (packet.Length < 12) return Task.CompletedTask;
            byte result = packet.Span[4];
            uint count = BinaryPrimitives.ReadUInt32LittleEndian(packet.Span[8..]);
            string text = DescribeLuckyCoinRegistration(result, count);
            MuGame.ScheduleOnMainThread(() =>
            {
                EventItemState.LuckyCoinsRegistered = count;
                DelgadoLuckyCoinWindow.Instance.StatusText = text;
            });
            return Task.CompletedTask;
        }

        [PacketHandler(0xBF, 0x0D)] // LuckyCoinExchangeResponse (S2C); same layout as LuckyCoinExchangeResult
        public Task HandleLuckyCoinExchangeAsync(Memory<byte> packet)
        {
            if (packet.Length < 5) return Task.CompletedTask;
            string text = DescribeLuckyCoinExchange(packet.Span[4]);
            MuGame.ScheduleOnMainThread(() => DelgadoLuckyCoinWindow.Instance.StatusText = text);
            return Task.CompletedTask;
        }

        /// <summary>
        /// 0x94 EventChipRegistrationResult (Golden Archer). OpenMU sends 1 only for a missing item and 0 both for
        /// opening the dialog and for a registration, so 1 is not read as the Lena lottery numbers.
        /// </summary>
        [PacketHandler(0x94, PacketRouter.NoSubCode)]
        public Task HandleEventChipRegistrationResultAsync(Memory<byte> packet)
        {
            if (packet.Length < 10) return Task.CompletedTask;
            var span = packet.Span;
            byte result = span[3];
            uint registered = BinaryPrimitives.ReadUInt32LittleEndian(span[4..]);
            int inInventory = BinaryPrimitives.ReadUInt16LittleEndian(span[8..]);
            MuGame.ScheduleOnMainThread(() =>
            {
                EventItemState.EventChipsRegistered = registered;
                EventItemState.EventChipsInInventory = inInventory;
                GoldenArcherWindow.Instance.ShowResult(result);
            });
            return Task.CompletedTask;
        }
    }
}
