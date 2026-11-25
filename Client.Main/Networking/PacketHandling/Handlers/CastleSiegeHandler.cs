#nullable enable
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Threading.Tasks;
using Client.Main.Controls.UI;
using Client.Main.Controls.UI.Game.CastleSiege;
using Client.Main.Core.Client;
using Client.Main.Core.Utilities;
using Client.Main.Models;
using Client.Main.Scenes;
using Client.Main.Worlds;
using Microsoft.Extensions.Logging;
using static Client.Main.Core.Client.CastleSiegeManager;

namespace Client.Main.Networking.PacketHandling.Handlers
{
    /// <summary>
    /// Castle Siege packets of OpenMU (ServerToClientPackets.xml 0xB2-0xBB). The OpenMU packet NuGet has no
    /// server-to-client siege structs, so the fields are read at the offsets of the server specification.
    /// </summary>
    public class CastleSiegeHandler : IGamePacketHandler
    {
        private readonly ILogger<CastleSiegeHandler> _logger;
        private readonly CastleSiegeManager _siege;

        public CastleSiegeHandler(ILoggerFactory loggerFactory, CastleSiegeManager siege)
        {
            _logger = loggerFactory.CreateLogger<CastleSiegeHandler>();
            _siege = siege;
        }

        // ─────────────────────────────── Status / registration ───────────────────────────────

        [PacketHandler(0xB2, 0x00)] // CastleSiegeStatusResponse
        public Task HandleStatusAsync(Memory<byte> packet)
        {
            if (!HasLength(packet, 46, "status")) return Task.CompletedTask;
            var s = packet.Span;
            bool ok = s[4] == 1;
            var state = (SiegeState)s[5];
            var start = ReadDate(s[6..]);
            var end = ReadDate(s[12..]);
            var siegeStart = ReadDate(s[18..]);
            string guild = ReadString(s.Slice(24, 8));
            string master = ReadString(s.Slice(32, 10));
            uint remaining = BinaryPrimitives.ReadUInt32BigEndian(s[42..]);
            Run(() =>
            {
                bool wasRunning = _siege.IsBattleRunning;
                if (ok) _siege.SetStatus(state, start, end, siegeStart, guild, master, remaining);
                else _siege.SetStatusUnavailable();
                if (wasRunning != _siege.IsBattleRunning)
                    ValleyOfLorenWorld.SetCastleSiegeBattleActive(_siege.IsBattleRunning);
            });
            return Task.CompletedTask;
        }

        [PacketHandler(0xB2, 0x01)] // CastleSiegeRegistrationResponse
        public Task HandleRegistrationAsync(Memory<byte> packet)
        {
            if (!HasLength(packet, 5, "registration")) return Task.CompletedTask;
            byte result = packet.Span[4];
            string text = result switch
            {
                1 => "Your guild has been registered for the Castle Siege.",
                2 => "Your guild is already registered.",
                3 => "The castle owner's guild defends the castle and cannot register.",
                4 => "Only the guild master can register the guild.",
                5 => "The guild master's level is too low to register.",
                6 => "You are not a member of a guild.",
                7 => "Guild registration is closed.",
                8 => "Your guild does not have enough members.",
                _ => "The guild could not be registered.",
            };
            Run(() =>
            {
                RequestDialog.ShowInfo(text);
                if (result == 1) CastleSiegeWindow.Instance.RequestData();
            });
            return Task.CompletedTask;
        }

        [PacketHandler(0xB2, 0x02)] // CastleSiegeUnregisterResponse
        public Task HandleUnregisterAsync(Memory<byte> packet)
        {
            if (!HasLength(packet, 5, "unregister")) return Task.CompletedTask;
            byte result = packet.Span[4];
            string text = result switch
            {
                1 => "Your guild gave up the Castle Siege registration.",
                2 => "Your guild is not registered.",
                3 => "The registration can only be withdrawn during the guild registration period.",
                _ => "The registration could not be withdrawn.",
            };
            Run(() =>
            {
                RequestDialog.ShowInfo(text);
                if (result == 1) CastleSiegeWindow.Instance.RequestData();
            });
            return Task.CompletedTask;
        }

        [PacketHandler(0xB2, 0x03)] // CastleSiegeRegistrationStateResponse
        public Task HandleRegistrationStateAsync(Memory<byte> packet)
        {
            if (!HasLength(packet, 19, "registration state")) return Task.CompletedTask;
            var s = packet.Span;
            bool registered = s[4] == 1;
            string guild = ReadString(s.Slice(5, 8));
            uint marks = BinaryPrimitives.ReadUInt32BigEndian(s[13..]);
            bool givingUp = s[17] != 0;
            byte rank = s[18];
            Run(() => _siege.SetRegistration(registered, guild, marks, givingUp, rank));
            return Task.CompletedTask;
        }

        [PacketHandler(0xB2, 0x04)] // CastleSiegeMarkRegistrationResponse
        public Task HandleMarkRegistrationAsync(Memory<byte> packet)
        {
            if (!HasLength(packet, 17, "mark registration")) return Task.CompletedTask;
            var s = packet.Span;
            byte result = s[4];
            uint marks = BinaryPrimitives.ReadUInt32BigEndian(s[13..]);
            Run(() =>
            {
                if (result == 1)
                {
                    _siege.SetMarks(marks);
                    Chat($"Sign of Lord registered. Your guild has {marks} mark(s).");
                    return;
                }

                RequestDialog.ShowInfo(result switch
                {
                    2 => "Your guild is not registered for the Castle Siege.",
                    3 => "This item is not a Sign of Lord.",
                    _ => "Signs of Lord can only be registered during the mark registration period.",
                });
            });
            return Task.CompletedTask;
        }

        [PacketHandler(0xB9, 0x02)] // CastleSiegeOwnerLogo: all zeros without owner
        public Task HandleOwnerLogoAsync(Memory<byte> packet)
        {
            if (!HasLength(packet, 36, "owner logo")) return Task.CompletedTask;
            var mark = GuildInfo.UnpackMark(packet.Span.Slice(4, 32));
            Run(() => _siege.SetOwnerGuildMark(mark));
            return Task.CompletedTask;
        }

        [PacketHandler(0xB4, PacketRouter.NoSubCode)] // CastleSiegeRegisteredGuildList (C2)
        public Task HandleRegisteredGuildListAsync(Memory<byte> packet)
        {
            if (!TryGetEntries(packet, 14, out int count, "registered guild list")) return Task.CompletedTask;
            var s = packet.Span;
            var guilds = new List<RegisteredGuild>(count);
            for (int i = 0; i < count; i++)
            {
                var e = s.Slice(12 + i * 14, 14);
                guilds.Add(new RegisteredGuild(ReadString(e[..8]), BinaryPrimitives.ReadUInt32BigEndian(e[8..]), e[12] != 0, e[13]));
            }
            Run(() => _siege.SetRegisteredGuilds(guilds));
            return Task.CompletedTask;
        }

        [PacketHandler(0xB5, PacketRouter.NoSubCode)] // CastleSiegeGuildList (C2): selected guilds and sides
        public Task HandleParticipantListAsync(Memory<byte> packet)
        {
            if (!TryGetEntries(packet, 16, out int count, "participant list")) return Task.CompletedTask;
            var s = packet.Span;
            var guilds = new List<Participant>(count);
            if (s[4] == 1)
            {
                for (int i = 0; i < count; i++)
                {
                    var e = s.Slice(12 + i * 16, 16);
                    guilds.Add(new Participant((JoinSide)e[0], e[1] != 0, ReadString(e.Slice(2, 8)), BinaryPrimitives.ReadUInt32LittleEndian(e[12..])));
                }
            }
            Run(() => _siege.SetParticipants(guilds));
            return Task.CompletedTask;
        }

        // ─────────────────────────────── Senior: taxes and defenses ───────────────────────────────

        [PacketHandler(0xB2, 0x05)] // CastleSiegeDefenseBuyResponse: a destroyed gate/statue was rebuilt
        public Task HandleDefenseBuyAsync(Memory<byte> packet)
        {
            if (!HasLength(packet, 16, "defense buy")) return Task.CompletedTask;
            var s = packet.Span;
            byte result = s[4];
            uint number = BinaryPrimitives.ReadUInt32LittleEndian(s[8..]);
            uint index = BinaryPrimitives.ReadUInt32LittleEndian(s[12..]);
            Run(() =>
            {
                if (result == 1)
                {
                    _siege.SetDefenseNpcBuilt(number, index);
                    RefreshDefenseList(number);
                }
                RequestDialog.ShowInfo(result switch
                {
                    1 => $"{DefenseName(number)} #{index} has been built.",
                    2 => "Only the castle owner's guild master or assistant can build structures.",
                    3 => "You do not have enough Zen.",
                    4 => "This structure already exists.",
                    7 => "The structure was not found.",
                    _ => "The structure could not be built.",
                });
            });
            return Task.CompletedTask;
        }

        [PacketHandler(0xB2, 0x06)] // CastleSiegeDefenseRepairResponse
        public Task HandleDefenseRepairAsync(Memory<byte> packet)
        {
            if (!HasLength(packet, 24, "defense repair")) return Task.CompletedTask;
            var s = packet.Span;
            byte result = s[4];
            uint number = BinaryPrimitives.ReadUInt32LittleEndian(s[8..]);
            uint index = BinaryPrimitives.ReadUInt32LittleEndian(s[12..]);
            uint hp = BinaryPrimitives.ReadUInt32LittleEndian(s[16..]);
            uint maxHp = BinaryPrimitives.ReadUInt32LittleEndian(s[20..]);
            Run(() =>
            {
                if (result == 1) _siege.UpdateDefenseNpcHp(number, index, hp, maxHp);
                RequestDialog.ShowInfo(result switch
                {
                    1 => "The structure has been repaired.",
                    2 => "Only the castle owner's guild master can repair structures.",
                    3 => "You do not have enough Zen.",
                    7 => "The structure was not found.",
                    _ => "The structure could not be repaired.",
                });
            });
            return Task.CompletedTask;
        }

        [PacketHandler(0xB2, 0x07)] // CastleSiegeDefenseUpgradeResponse
        public Task HandleDefenseUpgradeAsync(Memory<byte> packet)
        {
            if (!HasLength(packet, 24, "defense upgrade")) return Task.CompletedTask;
            var s = packet.Span;
            byte result = s[4];
            uint number = BinaryPrimitives.ReadUInt32LittleEndian(s[8..]);
            uint index = BinaryPrimitives.ReadUInt32LittleEndian(s[12..]);
            uint type = BinaryPrimitives.ReadUInt32LittleEndian(s[16..]);
            uint level = BinaryPrimitives.ReadUInt32LittleEndian(s[20..]);
            Run(() =>
            {
                if (result == 1)
                {
                    _siege.SetDefenseNpcUpgrade(number, index, type, level);
                    RefreshDefenseList(number); // a life upgrade changes the HP
                }
                string kind = type switch { 1 => "Defense", 2 => "Regeneration", 3 => "Life", _ => "Upgrade" };
                RequestDialog.ShowInfo(result switch
                {
                    1 => $"{DefenseName(number)} #{index}: {kind} upgraded to level {level}.",
                    2 => "Only the castle owner's guild master or assistant can upgrade structures.",
                    3 => "You do not have enough Zen.",
                    4 => "You do not have enough Jewels of Guardian.",
                    5 => "This upgrade is not available for the structure.",
                    6 => "This upgrade level is not available (upgrade one level at a time).",
                    7 => "The structure was not found.",
                    _ => "The structure could not be upgraded (not during the battle).",
                });
            });
            return Task.CompletedTask;
        }

        [PacketHandler(0xB2, 0x08)] // CastleSiegeTaxInfoResponse
        public Task HandleTaxInfoAsync(Memory<byte> packet)
        {
            if (!HasLength(packet, 15, "tax info")) return Task.CompletedTask;
            var s = packet.Span;
            if (s[4] != 1)
            {
                _logger.LogInformation("Castle Siege tax info refused ({Result}).", s[4]);
                return Task.CompletedTask;
            }
            byte chaos = s[5], store = s[6];
            ulong treasury = BinaryPrimitives.ReadUInt64BigEndian(s[7..]);
            Run(() => _siege.SetTaxInfo(chaos, store, treasury));
            return Task.CompletedTask;
        }

        [PacketHandler(0xB2, 0x09)] // CastleSiegeTaxChangeResponse
        public Task HandleTaxChangeAsync(Memory<byte> packet)
        {
            if (!HasLength(packet, 10, "tax change")) return Task.CompletedTask;
            var s = packet.Span;
            byte result = s[4], type = s[5];
            uint value = BinaryPrimitives.ReadUInt32BigEndian(s[6..]);
            Run(() =>
            {
                if (result == 1) _siege.SetTaxRate(type, (byte)Math.Min(value, byte.MaxValue));
                else RequestDialog.ShowInfo(result == 2
                    ? "Only the castle owner's guild master can change the taxes."
                    : "The tax rate could not be changed (0-3 %, not during the battle).");
            });
            return Task.CompletedTask;
        }

        [PacketHandler(0xB2, 0x10)] // CastleSiegeTributeWithdrawResponse
        public Task HandleTributeWithdrawAsync(Memory<byte> packet)
        {
            if (!HasLength(packet, 13, "tribute withdraw")) return Task.CompletedTask;
            byte result = packet.Span[4];
            ulong amount = BinaryPrimitives.ReadUInt64BigEndian(packet.Span[5..]);
            Run(() =>
            {
                if (result == 1)
                {
                    _siege.SetTreasury(_siege.Treasury >= amount ? _siege.Treasury - amount : 0);
                    Chat($"{amount:N0} Zen withdrawn from the castle treasury.");
                }
                else RequestDialog.ShowInfo(result == 2
                    ? "Only the castle owner's guild master can withdraw the treasury."
                    : "The Zen could not be withdrawn.");
            });
            return Task.CompletedTask;
        }

        [PacketHandler(0xB2, 0x1A)] // CastleSiegeTaxRateNotification
        public Task HandleTaxRateAsync(Memory<byte> packet)
        {
            if (!HasLength(packet, 6, "tax rate")) return Task.CompletedTask;
            byte type = packet.Span[4], rate = packet.Span[5];
            Run(() => _siege.SetTaxRate(type, rate));
            return Task.CompletedTask;
        }

        [PacketHandler(0xB3, PacketRouter.NoSubCode)] // CastleSiegeNpcList (C2): gates or statues
        public Task HandleNpcListAsync(Memory<byte> packet)
        {
            if (!TryGetEntries(packet, 28, out int count, "npc list")) return Task.CompletedTask;
            var s = packet.Span;
            var npcs = new List<DefenseNpc>(count);
            for (int i = 0; i < count; i++)
            {
                var e = s.Slice(12 + i * 28, 28);
                npcs.Add(new DefenseNpc(
                    BinaryPrimitives.ReadUInt32LittleEndian(e),
                    BinaryPrimitives.ReadUInt32LittleEndian(e[4..]),
                    BinaryPrimitives.ReadUInt32LittleEndian(e[8..]),
                    BinaryPrimitives.ReadUInt32LittleEndian(e[12..]),
                    BinaryPrimitives.ReadUInt32LittleEndian(e[16..]),
                    BinaryPrimitives.ReadUInt32LittleEndian(e[20..]),
                    e[24], e[25], e[26] != 0));
            }
            Run(() => _siege.SetDefenseNpcs(npcs));
            return Task.CompletedTask;
        }

        // ─────────────────────────────── Gates ───────────────────────────────

        [PacketHandler(0xB2, 0x11)] // CastleSiegeGateInterfaceResponse: a lever was talked to
        public Task HandleGateInterfaceAsync(Memory<byte> packet)
        {
            if (!HasLength(packet, 7, "gate interface")) return Task.CompletedTask;
            byte result = packet.Span[4];
            ushort gate = BinaryPrimitives.ReadUInt16BigEndian(packet.Span[5..]);
            Run(() =>
            {
                if (result != 1)
                {
                    CloseNpc();
                    RequestDialog.ShowInfo(result == 2
                        ? "Only the defending guilds can operate the castle gates."
                        : "The gate cannot be operated.");
                    return;
                }

                bool open = _siege.GateOpen.TryGetValue(gate, out bool isOpen) && isOpen;
                RequestDialog.Show(open ? "The castle gate is open. Close it?" : "The castle gate is closed. Open it?",
                    onAccept: () => _ = MuGame.Network?.GetCastleSiegeService()?.SendGateToggleAsync(!open, gate),
                    onReject: CloseNpc,
                    acceptText: open ? "Close gate" : "Open gate",
                    rejectText: "Cancel");
            });
            return Task.CompletedTask;
        }

        [PacketHandler(0xB2, 0x12)] // CastleSiegeGateOperateResponse
        public Task HandleGateOperateAsync(Memory<byte> packet)
        {
            if (!HasLength(packet, 8, "gate operate")) return Task.CompletedTask;
            byte result = packet.Span[4];
            bool open = packet.Span[5] != 0;
            ushort gate = BinaryPrimitives.ReadUInt16BigEndian(packet.Span[6..]);
            Run(() =>
            {
                CloseNpc();
                if (result == 1)
                {
                    _siege.GateOpen[gate] = open;
                    Chat(open ? "The castle gate has been opened." : "The castle gate has been closed.");
                }
                else RequestDialog.ShowInfo("The gate could not be operated.");
            });
            return Task.CompletedTask;
        }

        [PacketHandler(0xB2, 0x13)] // CastleSiegeGateStateNotification
        public Task HandleGateStateAsync(Memory<byte> packet)
        {
            if (!HasLength(packet, 7, "gate state")) return Task.CompletedTask;
            bool open = packet.Span[4] != 0;
            ushort gate = BinaryPrimitives.ReadUInt16BigEndian(packet.Span[5..]);
            Run(() => _siege.GateOpen[gate] = open);
            return Task.CompletedTask;
        }

        // ─────────────────────────────── Battle ───────────────────────────────

        [PacketHandler(0xB2, 0x19)] // CastleSiegeJoinSideNotification
        public Task HandleJoinSideAsync(Memory<byte> packet)
        {
            if (!HasLength(packet, 5, "join side")) return Task.CompletedTask;
            var side = (JoinSide)packet.Span[4];
            Run(() =>
            {
                _siege.SetSide(side);
                // The server sends the side when the siege map is entered during Ready/Start: get the state timer.
                _ = MuGame.Network?.GetCastleSiegeService()?.SendStatusRequestAsync();
                if (side != JoinSide.None) Chat($"Castle Siege: you fight on the {GetSideName(side)} side.");
            });
            return Task.CompletedTask;
        }

        [PacketHandler(0xB2, 0x15)] // CastleSiegeCrownAccessState
        public Task HandleCrownAccessAsync(Memory<byte> packet)
        {
            if (!HasLength(packet, 12, "crown access")) return Task.CompletedTask;
            var state = (CrownAccess)packet.Span[4];
            uint ms = BinaryPrimitives.ReadUInt32LittleEndian(packet.Span[8..]);
            Run(() =>
            {
                _siege.SetCrownAccess(state, ms);
                string? text = state switch
                {
                    CrownAccess.Started => "Crown registration started. Hold your position!",
                    CrownAccess.Failed => "Crown registration was interrupted.",
                    CrownAccess.OccupiedByOtherPlayer => "Another player is registering at the Crown.",
                    CrownAccess.OccupiedByOtherSide => "The Crown switches are not held by your side.",
                    _ => null,
                };
                if (text != null) Chat(text);
            });
            return Task.CompletedTask;
        }

        [PacketHandler(0xB2, 0x16)] // CastleSiegeCrownStateUpdate: 0 accessible, 1 protected
        public Task HandleCrownStateAsync(Memory<byte> packet)
        {
            if (!HasLength(packet, 5, "crown state")) return Task.CompletedTask;
            byte state = packet.Span[4];
            Run(() => _siege.SetCrownAccessible(state == 0));
            return Task.CompletedTask;
        }

        [PacketHandler(0xB2, 0x18)] // CastleSiegeBattleProcess: 1 = the crown was registered (ownership change)
        public Task HandleBattleProcessAsync(Memory<byte> packet)
        {
            if (!HasLength(packet, 13, "battle process")) return Task.CompletedTask;
            byte state = packet.Span[4];
            string guild = ReadString(packet.Span.Slice(5, 8));
            Run(() =>
            {
                if (state == 1)
                {
                    Chat($"The guild {guild} has seized the Crown and taken the castle!");
                    // Sides swap on capture: refresh owner and side-dependent data.
                    _ = MuGame.Network?.GetCastleSiegeService()?.SendStatusRequestAsync();
                }
                else Chat($"The guild {guild} started the Crown registration.");
            });
            return Task.CompletedTask;
        }

        [PacketHandler(0xB2, 0x1D)] // CastleSiegeGuildCommand from the commander
        public Task HandleGuildCommandAsync(Memory<byte> packet)
        {
            if (!HasLength(packet, 8, "guild command")) return Task.CompletedTask;
            var s = packet.Span;
            string command = s[7] switch { 0 => "Attack", 1 => "Defend", _ => "Wait" };
            string text = $"Command (group {s[4] + 1}): {command} at {s[5]}, {s[6]}";
            Run(() =>
            {
                _siege.SetCommand(text);
                Chat(text);
            });
            return Task.CompletedTask;
        }

        [PacketHandler(0xB2, 0x20)] // CastleSiegeSwitchInfo
        public Task HandleSwitchInfoAsync(Memory<byte> packet)
        {
            if (!HasLength(packet, 27, "switch info")) return Task.CompletedTask;
            var s = packet.Span;
            ushort id = BinaryPrimitives.ReadUInt16BigEndian(s[4..]);
            var info = new SwitchInfo(s[6] != 0, (JoinSide)s[7], ReadString(s.Slice(8, 8)), ReadString(s.Slice(16, 11)));
            Run(() => _siege.Switches[id] = info);
            return Task.CompletedTask;
        }

        [PacketHandler(0xB9, 0x01)] // CastleSiegeLifeStoneBuildTime: build stage 0-5 of a Life Stone in view
        public Task HandleLifeStoneAsync(Memory<byte> packet)
        {
            if (!HasLength(packet, 7, "life stone")) return Task.CompletedTask;
            ushort id = BinaryPrimitives.ReadUInt16BigEndian(packet.Span[4..]);
            byte stage = packet.Span[6];
            Run(() => _siege.LifeStoneBuildTime[id] = stage);
            return Task.CompletedTask;
        }

        [PacketHandler(0xB6, PacketRouter.NoSubCode)] // CastleSiegeMiniMapPlayerPositions (C2, commanders)
        public Task HandleMiniMapPlayersAsync(Memory<byte> packet)
        {
            var s = packet.Span;
            if (s.Length < 8) return Task.CompletedTask;
            int count = (int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(s[4..]), (uint)(s.Length - 8) / 2);
            var players = new List<(byte, byte)>(count);
            for (int i = 0; i < count; i++) players.Add((s[8 + i * 2], s[9 + i * 2]));
            Run(() => _siege.SetMiniMapPlayers(players));
            return Task.CompletedTask;
        }

        [PacketHandler(0xBB, PacketRouter.NoSubCode)] // CastleSiegeMiniMapNpcPositions (C2, commanders)
        public Task HandleMiniMapNpcsAsync(Memory<byte> packet)
        {
            var s = packet.Span;
            if (s.Length < 5) return Task.CompletedTask;
            int count = Math.Min(s[4], (s.Length - 5) / 3);
            var npcs = new List<(bool, byte, byte)>(count);
            for (int i = 0; i < count; i++) npcs.Add((s[5 + i * 3] == 0, s[6 + i * 3], s[7 + i * 3]));
            Run(() => _siege.SetMiniMapNpcs(npcs));
            return Task.CompletedTask;
        }

        // ─────────────────────────────── Siege machines ───────────────────────────────

        [PacketHandler(0xB7, 0x00)] // CastleSiegeMachineInterface: a catapult was talked to
        public Task HandleMachineInterfaceAsync(Memory<byte> packet)
        {
            if (!HasLength(packet, 8, "machine interface")) return Task.CompletedTask;
            bool ok = packet.Span[4] == 1;
            bool attack = packet.Span[5] == 1;
            ushort id = BinaryPrimitives.ReadUInt16BigEndian(packet.Span[6..]);
            Run(() =>
            {
                if (ok) CastleSiegeMachineWindow.Instance.ShowMachine(id, attack);
                else Chat("The siege machine cannot be used right now.");
            });
            return Task.CompletedTask;
        }

        [PacketHandler(0xB7, 0x01)] // CastleSiegeMachineUseResult
        public Task HandleMachineUseAsync(Memory<byte> packet)
        {
            if (!HasLength(packet, 10, "machine use")) return Task.CompletedTask;
            bool ok = packet.Span[4] == 1;
            byte x = packet.Span[8], y = packet.Span[9];
            Run(() => Chat(ok ? $"Siege machine fired at {x}, {y}." : "The siege machine could not fire."));
            return Task.CompletedTask;
        }

        [PacketHandler(0xB7, 0x02)] // CastleSiegeMachineRegionNotify: impact warning
        public Task HandleMachineRegionAsync(Memory<byte> packet)
        {
            if (!HasLength(packet, 7, "machine region")) return Task.CompletedTask;
            byte x = packet.Span[5], y = packet.Span[6];
            Run(() => Chat($"Siege machine strike incoming at {x}, {y}!"));
            return Task.CompletedTask;
        }

        // ─────────────────────────────── Land of Trials (hunting zone) ───────────────────────────────

        [PacketHandler(0xB9, 0x03)] // CastleSiegeHuntingZoneGuardInfo: the Land of Trials guard was talked to
        public Task HandleHuntZoneGuardInfoAsync(Memory<byte> packet)
        {
            if (!HasLength(packet, 20, "hunting zone guard info")) return Task.CompletedTask;
            var s = packet.Span;
            var access = (HuntZoneAccess)s[4];
            bool isPublic = s[5] != 0;
            uint fee = BinaryPrimitives.ReadUInt32LittleEndian(s[8..]);
            uint maxFee = BinaryPrimitives.ReadUInt32LittleEndian(s[12..]);
            uint step = BinaryPrimitives.ReadUInt32LittleEndian(s[16..]);
            Run(() =>
            {
                _siege.SetHuntZoneInfo(access, isPublic, fee, maxFee, step);
                string? closed = access == HuntZoneAccess.Failed ? "The Land of Trials is closed: the castle has no owner."
                    : access == HuntZoneAccess.Guest && !isPublic ? "The castle owner has closed the Land of Trials to the public."
                    : null;
                if (closed != null)
                {
                    CloseNpc();
                    RequestDialog.ShowInfo(closed);
                    return;
                }

                // ponytail: the owner's guild master only gets the entry dialog; public access / fee setting needs a window.
                string text = access switch
                {
                    HuntZoneAccess.OwnerAllianceMember => "Enter the Land of Trials? The castle owner's alliance enters for free.",
                    HuntZoneAccess.OwnerGuildMaster => $"Land of Trials: {(isPublic ? "open" : "closed")} to the public, entrance fee {fee:N0} Zen. Enter?",
                    _ => fee > 0 ? $"Enter the Land of Trials for {fee:N0} Zen?" : "Enter the Land of Trials?",
                };
                uint charged = access == HuntZoneAccess.Guest ? fee : 0;
                RequestDialog.Show(text,
                    onAccept: () => _ = MuGame.Network?.GetCastleSiegeService()?.SendHuntZoneEnterAsync(charged),
                    onReject: CloseNpc,
                    acceptText: "Enter",
                    rejectText: "Cancel");
            });
            return Task.CompletedTask;
        }

        [PacketHandler(0xB9, 0x05)] // CastleSiegeHuntingZoneEnterResponse: on success the server warps
        public Task HandleHuntZoneEnterAsync(Memory<byte> packet)
        {
            if (!HasLength(packet, 5, "hunting zone enter")) return Task.CompletedTask;
            bool ok = packet.Span[4] == 1;
            Run(() =>
            {
                // The server already dropped the guard dialog on success; closing also clears the client's NPC flag.
                CloseNpc();
                // Missing Zen / level is explained by the server's own blue message.
                Chat(ok ? "You entered the Land of Trials." : "You could not enter the Land of Trials.");
            });
            return Task.CompletedTask;
        }

        [PacketHandler(0xB2, 0x1F)] // CastleSiegeHuntingZoneEntranceSettingResponse: IsPublic is the current setting
        public Task HandleHuntZoneEntranceSettingAsync(Memory<byte> packet)
        {
            if (!HasLength(packet, 6, "hunting zone entrance setting")) return Task.CompletedTask;
            byte result = packet.Span[4];
            bool isPublic = packet.Span[5] != 0;
            Run(() =>
            {
                _siege.SetHuntZonePublic(isPublic);
                if (result == 1) Chat(isPublic ? "The Land of Trials is now open to the public." : "The Land of Trials is now closed to the public.");
                else RequestDialog.ShowInfo(result == 2
                    ? "Only the castle owner's guild master can change the Land of Trials access."
                    : "The Land of Trials access cannot be changed during the battle.");
            });
            return Task.CompletedTask;
        }

        // ─────────────────────────────── Helpers ───────────────────────────────

        private static string DefenseName(uint number) => number == 283 ? "Guardian Statue" : "Gate";

        /// <summary>Re-reads the Senior's gate (277) or statue (283) list after a change the response does not carry.</summary>
        private static void RefreshDefenseList(uint number)
            => _ = MuGame.Network?.GetCastleSiegeService()?.SendDefenseListRequestAsync(number == 283);

        private bool HasLength(Memory<byte> packet, int length, string name)
        {
            if (packet.Length >= length) return true;
            _logger.LogWarning("Castle Siege {Name} packet too short: {Length}", name, packet.Length);
            return false;
        }

        /// <summary>C2 list packets: Result@4, count (uint LE)@8, entries@12.</summary>
        private bool TryGetEntries(Memory<byte> packet, int entrySize, out int count, string name)
        {
            count = 0;
            if (!HasLength(packet, 12, name)) return false;
            uint announced = BinaryPrimitives.ReadUInt32LittleEndian(packet.Span[8..]);
            count = (int)Math.Min(announced, (uint)(packet.Length - 12) / (uint)entrySize);
            return true;
        }

        private static string ReadString(ReadOnlySpan<byte> field) => PlayerShopListParser.ReadString(field);

        /// <summary>Wire date: year (ushort BE), month, day, hour, minute; zeros when the server has none.</summary>
        private static DateTime ReadDate(ReadOnlySpan<byte> s)
        {
            int year = BinaryPrimitives.ReadUInt16BigEndian(s);
            if (year is < 1 or > 9999 || s[2] is < 1 or > 12 || s[3] < 1 || s[3] > DateTime.DaysInMonth(year, s[2]) || s[4] > 23 || s[5] > 59)
                return DateTime.MinValue;
            return new DateTime(year, s[2], s[3], s[4], s[5], 0);
        }

        private static void Run(Action action) => MuGame.ScheduleOnMainThread(action);

        private static void Chat(string text)
            => (MuGame.Instance?.ActiveScene as GameScene)?.ChatLog?.AddMessage("System", text, MessageType.System);

        private static void CloseNpc() => _ = MuGame.Network?.GetCharacterService()?.SendCloseNpcRequestAsync();
    }
}
