using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Client.Main.Controls.UI;
using Client.Main.Core.Client;
using Client.Main.Core.Utilities;
using Client.Main.Models;
using Client.Main.Networking.Services;
using Client.Main.Scenes;
using Microsoft.Extensions.Logging;
using MUnique.OpenMU.Network.Packets.ServerToClient;

namespace Client.Main.Networking.PacketHandling.Handlers
{
    /// <summary>
    /// Handles guild packets: guild assignment of players in scope, guild cache, member list,
    /// creation flow and kick/leave results. Mirrors WSclient.cpp ReceiveGuild* (l.7036-7600).
    /// </summary>
    public class GuildHandler : IGamePacketHandler
    {
        private readonly ILogger<GuildHandler> _logger;
        private readonly CharacterState _characterState;
        private readonly CharacterService _characterService;
        private readonly NetworkManager _networkManager;

        public GuildHandler(ILoggerFactory loggerFactory, CharacterState characterState, CharacterService characterService, NetworkManager networkManager)
        {
            _logger = loggerFactory.CreateLogger<GuildHandler>();
            _characterState = characterState;
            _characterService = characterService;
            _networkManager = networkManager;
        }

        private GuildManager Guilds => _networkManager.GetGuildManager();

        /// <summary>AssignCharacterToGuild (0x65): players in scope that belong to a guild.</summary>
        [PacketHandler(0x65, PacketRouter.NoSubCode)]
        public Task HandleAssignCharacterToGuildAsync(Memory<byte> packet)
        {
            try
            {
                var assign = new AssignCharacterToGuild(packet);
                int count = assign.PlayerCount;
                while (count > 0 && AssignCharacterToGuild.GetRequiredSize(count) > packet.Length)
                    count--;
                for (int i = 0; i < count; i++)
                {
                    var relation = assign[i];
                    ushort playerId = (ushort)(relation.PlayerId & 0x7FFF);
                    bool isSelf = playerId == _characterState.Id;
                    Guilds.AssignPlayer(playerId, relation.GuildId, relation.Role, isSelf);
                    _logger.LogDebug("Player {Player:X4} in guild {GuildId} as {Role}{Self}.",
                        playerId, relation.GuildId, relation.Role, isSelf ? " (self)" : string.Empty);

                    if (Guilds.ShouldRequestGuild(relation.GuildId))
                        _ = _characterService.SendGuildInfoRequestAsync(relation.GuildId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing AssignCharacterToGuild (0x65).");
            }

            return Task.CompletedTask;
        }

        /// <summary>GuildMemberLeftGuild (0x5D): a player (maybe the hero) is no longer a guild member.</summary>
        [PacketHandler(0x5D, PacketRouter.NoSubCode)]
        public Task HandleGuildMemberLeftGuildAsync(Memory<byte> packet)
        {
            try
            {
                if (packet.Length < GuildMemberLeftGuild.Length)
                {
                    _logger.LogWarning("GuildMemberLeftGuild packet too short: {Length}", packet.Length);
                    return Task.CompletedTask;
                }

                var left = new GuildMemberLeftGuild(packet);
                ushort playerId = (ushort)(left.PlayerId & 0x7FFF);
                bool isSelf = playerId == _characterState.Id;
                string guildName = isSelf ? Guilds.OwnGuild?.Name ?? string.Empty : string.Empty;
                bool wasOwnMember = !isSelf && Guilds.HasGuild && Guilds.GetPlayerGuildId(playerId) == Guilds.OwnGuildId;
                Guilds.RemovePlayer(playerId, isSelf);
                // The guild master gets no kick result from OpenMU; refresh the member list instead.
                if (wasOwnMember)
                    _ = _characterService.SendGuildListRequestAsync();
                _logger.LogDebug("Player {Id:X4} left the guild (GM: {IsGM}).", playerId, left.IsGuildMaster);

                if (isSelf)
                {
                    ShowSystemMessage(string.IsNullOrEmpty(guildName)
                        ? "You are no longer a guild member."
                        : $"You are no longer a member of the guild {guildName}.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing GuildMemberLeftGuild (0x5D).");
            }

            return Task.CompletedTask;
        }

        /// <summary>GuildInformation (0x66): name and mark of a guild, answer to GuildInfoRequest.</summary>
        [PacketHandler(0x66, PacketRouter.NoSubCode)]
        public Task HandleGuildInformationAsync(Memory<byte> packet)
        {
            try
            {
                if (packet.Length < GuildInformation.Length)
                {
                    _logger.LogWarning("GuildInformation packet too short: {Length}", packet.Length);
                    return Task.CompletedTask;
                }

                var info = new GuildInformation(packet);
                Guilds.StoreGuild(new GuildInfo
                {
                    Id = info.GuildId,
                    Name = info.GuildName,
                    AllianceName = info.AllianceGuildName,
                    Type = info.GuildType,
                    Mark = GuildInfo.UnpackMark(info.Logo),
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing GuildInformation (0x66).");
            }

            return Task.CompletedTask;
        }

        /// <summary>GuildList (0x52): members of the own guild, answer to GuildListRequest.</summary>
        [PacketHandler(0x52, PacketRouter.NoSubCode)]
        public Task HandleGuildListAsync(Memory<byte> packet)
        {
            try
            {
                if (packet.Length < 24)
                {
                    _logger.LogWarning("GuildList packet too short: {Length}", packet.Length);
                    return Task.CompletedTask;
                }

                var list = new GuildList(packet);
                int count = list.GuildMemberCount;
                while (count > 0 && GuildList.GetRequiredSize(count) > packet.Length)
                    count--;
                var members = new List<GuildMemberInfo>(count);
                for (int i = 0; i < count; i++)
                {
                    var member = list[i];
                    members.Add(new GuildMemberInfo { Name = member.Name, Role = member.Role, ServerId = member.ServerId });
                }

                Guilds.UpdateMemberList(list.IsInGuild, list.TotalScore, list.CurrentScore, list.RivalGuildName, members);
                _logger.LogDebug("GuildList: InGuild={InGuild}, {Count} members.", list.IsInGuild, members.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing GuildList (0x52).");
            }

            return Task.CompletedTask;
        }

        /// <summary>GuildJoinResponse (0x51): result of the own join request.</summary>
        [PacketHandler(0x51, PacketRouter.NoSubCode)]
        public Task HandleGuildJoinResponseAsync(Memory<byte> packet)
        {
            if (packet.Length < GuildJoinResponse.Length)
                return Task.CompletedTask;

            var result = new GuildJoinResponse(packet).Result;
            string text = result switch
            {
                GuildJoinResponse.GuildJoinRequestResult.Accepted => "You have joined the guild.",
                GuildJoinResponse.GuildJoinRequestResult.Refused => "The guild master refused your request.",
                GuildJoinResponse.GuildJoinRequestResult.GuildFull => "The guild is full.",
                GuildJoinResponse.GuildJoinRequestResult.Disconnected => "The guild master is not connected.",
                GuildJoinResponse.GuildJoinRequestResult.NotTheGuildMaster => "That player is not a guild master.",
                GuildJoinResponse.GuildJoinRequestResult.AlreadyHaveGuild => "You are already in a guild.",
                GuildJoinResponse.GuildJoinRequestResult.GuildMasterOrRequesterIsBusy => "The guild master is busy. Try again later.",
                GuildJoinResponse.GuildJoinRequestResult.MinimumLevel6 => "You need to be at least level 6 to join a guild.",
                _ => $"Guild join failed ({result}).",
            };
            MuGame.ScheduleOnMainThread(() =>
            {
                Guilds.RaiseGuildJoinResult((byte)result);
                RequestDialog.ShowInfo(text);
            });
            return Task.CompletedTask;
        }

        /// <summary>GuildKickResponse (0x53): result of kick / leave / disband.</summary>
        [PacketHandler(0x53, PacketRouter.NoSubCode)]
        public Task HandleGuildKickResponseAsync(Memory<byte> packet)
        {
            if (packet.Length < GuildKickResponse.Length)
                return Task.CompletedTask;

            var result = new GuildKickResponse(packet).Result;
            string text = result switch
            {
                // OpenMU sends KickSucceeded to the kicked player (after 0x5D), not to the guild master.
                GuildKickResponse.GuildKickSuccess.KickSucceeded => Guilds.HasGuild
                    ? "The member has been removed from the guild."
                    : "You have been removed from the guild.",
                GuildKickResponse.GuildKickSuccess.GuildMemberWithdrawn => "You have left the guild.",
                GuildKickResponse.GuildKickSuccess.GuildDisband => "The guild has been disbanded.",
                GuildKickResponse.GuildKickSuccess.FailedPasswordIncorrect => "Incorrect security code.",
                GuildKickResponse.GuildKickSuccess.KickFailedBecausePlayerIsNotGuildMaster => "Only the guild master can do that.",
                _ => "The request failed.",
            };

            if (result == GuildKickResponse.GuildKickSuccess.KickSucceeded && Guilds.HasGuild)
                _ = _characterService.SendGuildListRequestAsync();

            // Leaving or disbanding ends the own membership right away, as ReceiveGuildResult in the original client.
            // OpenMU also does not send GuildMemberLeftGuild (0x5D) to the members of a disbanded guild
            // (GameServer.GuildDeletedAsync removes the guild before iterating its players).
            if (result is GuildKickResponse.GuildKickSuccess.GuildDisband or GuildKickResponse.GuildKickSuccess.GuildMemberWithdrawn)
                Guilds.RemovePlayer(_characterState.Id, isSelf: true);

            MuGame.ScheduleOnMainThread(() =>
            {
                Guilds.RaiseGuildKickResult((byte)result);
                RequestDialog.ShowInfo(text);
            });
            return Task.CompletedTask;
        }

        /// <summary>ShowGuildMasterDialog (0x54): talking to the guild master NPC.</summary>
        [PacketHandler(0x54, PacketRouter.NoSubCode)]
        public Task HandleShowGuildMasterDialogAsync(Memory<byte> packet)
        {
            MuGame.ScheduleOnMainThread(Guilds.RaiseGuildMasterDialog);
            return Task.CompletedTask;
        }

        /// <summary>ShowGuildCreationDialog (0x55): open the guild creation window.</summary>
        [PacketHandler(0x55, PacketRouter.NoSubCode)]
        public Task HandleShowGuildCreationDialogAsync(Memory<byte> packet)
        {
            MuGame.ScheduleOnMainThread(Guilds.RaiseGuildCreationDialog);
            return Task.CompletedTask;
        }

        /// <summary>GuildCreationResult (0x56).</summary>
        [PacketHandler(0x56, PacketRouter.NoSubCode)]
        public Task HandleGuildCreationResultAsync(Memory<byte> packet)
        {
            if (packet.Length < GuildCreationResult.Length)
                return Task.CompletedTask;

            var result = new GuildCreationResult(packet);
            bool success = result.Success;
            var error = result.Error;
            MuGame.ScheduleOnMainThread(() =>
            {
                Guilds.RaiseGuildCreationResult(success, (byte)error);
                (MuGame.Instance?.ActiveScene as GameScene)?.GuildCreate?.OnCreationResult(success);
                RequestDialog.ShowInfo(success
                    ? "Your guild has been created."
                    : error == GuildCreationResult.GuildCreationErrorType.GuildNameAlreadyTaken
                        ? "This guild name is already taken."
                        : "The guild could not be created.");
            });
            return Task.CompletedTask;
        }

        // ─────────────────────────── Guild war / battle soccer ───────────────────────────

        /// <summary>GuildWarRequestResult (0x60): result of the own /war or /battlesoccer request.</summary>
        [PacketHandler(0x60, PacketRouter.NoSubCode)]
        public Task HandleGuildWarRequestResultAsync(Memory<byte> packet)
        {
            try
            {
                if (packet.Length < GuildWarRequestResult.Length)
                {
                    _logger.LogWarning("GuildWarRequestResult packet too short: {Length}", packet.Length);
                    return Task.CompletedTask;
                }

                var result = new GuildWarRequestResult(packet).Result;
                ShowSystemMessage(result switch
                {
                    GuildWarRequestResult.RequestResult.GuildNotFound => "That guild does not exist.",
                    GuildWarRequestResult.RequestResult.RequestSentToGuildMaster => "The request has been sent. Waiting for the guild master's answer.",
                    GuildWarRequestResult.RequestResult.GuildMasterOffline => "The guild master of that guild is not connected.",
                    GuildWarRequestResult.RequestResult.NotInGuild => "You are not in a guild.",
                    GuildWarRequestResult.RequestResult.NotTheGuildMaster => "Only the guild master can declare a guild war.",
                    GuildWarRequestResult.RequestResult.AlreadyInWar => "That guild is already at war.",
                    _ => "The guild war could not be started.",
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing GuildWarRequestResult (0x60).");
            }

            return Task.CompletedTask;
        }

        /// <summary>GuildWarRequest (0x61): another guild master asks the own guild master for a war or battle soccer.</summary>
        [PacketHandler(0x61, PacketRouter.NoSubCode)]
        public Task HandleGuildWarRequestAsync(Memory<byte> packet)
        {
            try
            {
                if (packet.Length < GuildWarRequest.Length)
                {
                    _logger.LogWarning("GuildWarRequest packet too short: {Length}", packet.Length);
                    return Task.CompletedTask;
                }

                var request = new GuildWarRequest(packet);
                string guildName = request.GuildName;
                var type = request.Type;
                Guilds.PendingWarRequestGuild = guildName;
                Guilds.PendingWarRequestType = type;

                void Answer(bool accepted)
                {
                    Guilds.PendingWarRequestGuild = string.Empty;
                    _ = _characterService.SendGuildWarResponseAsync(accepted);
                }

                MuGame.ScheduleOnMainThread(() => RequestDialog.Show(
                    type == GuildWarType.Soccer
                        ? $"The guild {guildName} challenges your guild to a battle soccer match."
                        : $"The guild {guildName} declares war on your guild.",
                    onAccept: () => Answer(true),
                    onReject: () => Answer(false)));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing GuildWarRequest (0x61).");
            }

            return Task.CompletedTask;
        }

        /// <summary>GuildWarDeclared (0x62): the war started, sent to all members of both guilds.</summary>
        [PacketHandler(0x62, PacketRouter.NoSubCode)]
        public Task HandleGuildWarDeclaredAsync(Memory<byte> packet)
        {
            try
            {
                if (packet.Length < GuildWarDeclared.Length)
                {
                    _logger.LogWarning("GuildWarDeclared packet too short: {Length}", packet.Length);
                    return Task.CompletedTask;
                }

                var declared = new GuildWarDeclared(packet);
                string guildName = declared.GuildName;
                Guilds.StartWar(guildName, declared.Type, declared.TeamCode);
                ShowSystemMessage(declared.Type == GuildWarType.Soccer
                    ? $"Battle soccer against {guildName} has begun."
                    : $"The guild war against {guildName} has begun.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing GuildWarDeclared (0x62).");
            }

            return Task.CompletedTask;
        }

        /// <summary>GuildWarEnded (0x63).</summary>
        [PacketHandler(0x63, PacketRouter.NoSubCode)]
        public Task HandleGuildWarEndedAsync(Memory<byte> packet)
        {
            try
            {
                if (packet.Length < GuildWarEnded.Length)
                {
                    _logger.LogWarning("GuildWarEnded packet too short: {Length}", packet.Length);
                    return Task.CompletedTask;
                }

                var ended = new GuildWarEnded(packet);
                string guildName = ended.GuildName;
                Guilds.EndWar();
                ShowSystemMessage(ended.Result switch
                {
                    GuildWarEnded.GuildWarResult.Won => $"Your guild won the war against {guildName}!",
                    GuildWarEnded.GuildWarResult.Lost => $"Your guild lost the war against {guildName}.",
                    GuildWarEnded.GuildWarResult.OtherGuildMasterCancelledWar => $"The guild master of {guildName} cancelled the war.",
                    GuildWarEnded.GuildWarResult.CancelledWar => $"The war against {guildName} was cancelled.",
                    _ => $"The war against {guildName} has ended.",
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing GuildWarEnded (0x63).");
            }

            return Task.CompletedTask;
        }

        /// <summary>GuildWarScoreUpdate (0x64): sent at the start and on every score change.</summary>
        [PacketHandler(0x64, PacketRouter.NoSubCode)]
        public Task HandleGuildWarScoreUpdateAsync(Memory<byte> packet)
        {
            try
            {
                if (packet.Length < GuildWarScoreUpdate.Length)
                {
                    _logger.LogWarning("GuildWarScoreUpdate packet too short: {Length}", packet.Length);
                    return Task.CompletedTask;
                }

                var score = new GuildWarScoreUpdate(packet);
                byte own = score.ScoreOfOwnGuild, enemy = score.ScoreOfEnemyGuild;
                bool changed = own != Guilds.WarOwnScore || enemy != Guilds.WarEnemyScore;
                Guilds.UpdateWarScore(own, enemy);
                if (changed) // the initial 0:0 already came with the declaration message
                    ShowSystemMessage($"Guild war score: {Guilds.OwnGuild?.Name ?? "Your guild"} {own} : {enemy} {Guilds.WarEnemyGuild}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing GuildWarScoreUpdate (0x64).");
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// GuildSoccerTimeUpdate (0xF3 0x22): remaining seconds, every second of a match. Parsed by hand: OpenMU sends
        /// 8 bytes with the seconds at offset 6, the 0.9.8 struct expects them at offset 4.
        /// </summary>
        [PacketHandler(0xF3, 0x22)]
        public Task HandleGuildSoccerTimeUpdateAsync(Memory<byte> packet)
        {
            try
            {
                if (packet.Length < 8)
                {
                    _logger.LogWarning("GuildSoccerTimeUpdate packet too short: {Length}", packet.Length);
                    return Task.CompletedTask;
                }

                int seconds = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(packet.Span[6..]);
                Guilds.SoccerRemainingSeconds = seconds;
                // ponytail: chat line per full minute / last 10 seconds; a scoreboard HUD can read GuildManager.Soccer*.
                if (seconds > 0 && (seconds % 60 == 0 || seconds <= 10))
                    ShowSystemMessage(seconds >= 60 ? $"Battle soccer: {seconds / 60} minute(s) left." : $"Battle soccer: {seconds} second(s) left.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing GuildSoccerTimeUpdate (0xF3 0x22).");
            }

            return Task.CompletedTask;
        }

        /// <summary>GuildSoccerScoreUpdate (0xF3 0x23): at the start and on every goal; RedTeamGoals = 255 hides the board.</summary>
        [PacketHandler(0xF3, 0x23)]
        public Task HandleGuildSoccerScoreUpdateAsync(Memory<byte> packet)
        {
            try
            {
                if (packet.Length < GuildSoccerScoreUpdate.Length)
                {
                    _logger.LogWarning("GuildSoccerScoreUpdate packet too short: {Length}", packet.Length);
                    return Task.CompletedTask;
                }

                var score = new GuildSoccerScoreUpdate(packet);
                Guilds.UpdateSoccerScore(score.RedTeamName, score.RedTeamGoals, score.BlueTeamName, score.BlueTeamGoals);
                if (Guilds.SoccerScoreVisible)
                    ShowSystemMessage($"Battle soccer: {Guilds.SoccerRedTeam} {Guilds.SoccerRedGoals} : {Guilds.SoccerBlueGoals} {Guilds.SoccerBlueTeam}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing GuildSoccerScoreUpdate (0xF3 0x23).");
            }

            return Task.CompletedTask;
        }

        // ─────────────────────────── Alliance / hostility ───────────────────────────
        // The 0.9.8 packet package has no structs for these server packets; layouts follow OpenMU ServerToClientPackets.xml.

        private const byte RelationshipAlliance = 1;
        private const byte RelationshipHostility = 2;
        private const byte RequestJoin = 1;
        private const int AllianceGuildEntrySize = 41;

        /// <summary>GuildRelationshipRequest (0xE5): another guild master asks for an alliance / hostility change.</summary>
        [PacketHandler(0xE5, PacketRouter.NoSubCode)]
        public Task HandleGuildRelationshipRequestAsync(Memory<byte> packet)
        {
            try
            {
                if (packet.Length < 7)
                {
                    _logger.LogWarning("GuildRelationshipRequest packet too short: {Length}", packet.Length);
                    return Task.CompletedTask;
                }

                var span = packet.Span;
                byte relationship = span[3], request = span[4];
                ushort senderId = System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(span[5..]);
                Guilds.PendingRelationshipRequest = (relationship, request, senderId);

                string who = _networkManager.GetScopeManager().TryGetScopeObjectName(senderId, out var name) ? name : "A guild master";
                string guild = Guilds.GetPlayerGuildName((ushort)(senderId & 0x7FFF));
                if (guild.Length > 0)
                    who += $" ({guild})";
                string text = (relationship, request == RequestJoin) switch
                {
                    (RelationshipAlliance, true) => $"{who} wants to form an alliance with your guild.",
                    (RelationshipAlliance, false) => $"{who} wants to leave the alliance.",
                    (RelationshipHostility, true) => $"{who} wants to declare hostility against your guild.",
                    _ => $"{who} wants to end the hostility with your guild.",
                };

                void Answer(bool accepted)
                {
                    Guilds.PendingRelationshipRequest = null;
                    _ = _characterService.SendGuildRelationshipChangeResponseAsync(relationship, request, accepted, senderId);
                }

                MuGame.ScheduleOnMainThread(() => RequestDialog.Show(text, onAccept: () => Answer(true), onReject: () => Answer(false)));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing GuildRelationshipRequest (0xE5).");
            }

            return Task.CompletedTask;
        }

        /// <summary>GuildRelationshipChangeResult (0xE6): sent to requester and responder.</summary>
        [PacketHandler(0xE6, PacketRouter.NoSubCode)]
        public Task HandleGuildRelationshipChangeResultAsync(Memory<byte> packet)
        {
            try
            {
                if (packet.Length < 8)
                {
                    _logger.LogWarning("GuildRelationshipChangeResult packet too short: {Length}", packet.Length);
                    return Task.CompletedTask;
                }

                var span = packet.Span;
                byte relationship = span[3], request = span[4], result = span[5];
                bool alliance = relationship == RelationshipAlliance, join = request == RequestJoin;
                ShowSystemMessage(result switch
                {
                    1 => (alliance, join) switch
                    {
                        (true, true) => "The guild alliance has been formed.",
                        (true, false) => "The guild has left the alliance.",
                        (false, true) => "Hostility has been declared.",
                        _ => "The hostility has ended.",
                    },
                    16 => "The guild does not exist, or alliances are restricted during the Castle Siege.",
                    17 => "You have no authorization to do that.",
                    21 => "That guild is already in an alliance.",
                    22 => "That guild is already hostile.",
                    23 => "Your guild is already in an alliance.",
                    24 => "Your guild already has a hostile guild.",
                    25 => "The guild alliance does not exist.",
                    26 => "The hostile guild does not exist.",
                    27 => "Only the alliance master can do that.",
                    28 => "That guild is not a rival guild.",
                    29 => "The requirements to create an alliance are not met.",
                    30 => "The alliance has reached the maximum number of guilds.",
                    32 => "The request has been declined.",
                    161 => "The alliance master is not in a Gens.",
                    162 => "The guild master is not in a Gens.",
                    163 => "The alliance master and guild master belong to different Gens.",
                    _ => "The guild relationship could not be changed.",
                });

                // Alliance name (0x66) and rival guild (0x52) of the own guild changed.
                if (result == 1 && Guilds.HasGuild)
                {
                    _ = _characterService.SendGuildInfoRequestAsync(Guilds.OwnGuildId);
                    _ = _characterService.SendGuildListRequestAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing GuildRelationshipChangeResult (0xE6).");
            }

            return Task.CompletedTask;
        }

        /// <summary>AllianceList (0xE9, C2): guilds of the own alliance, answer to RequestAllianceList.</summary>
        [PacketHandler(0xE9, PacketRouter.NoSubCode)]
        public Task HandleAllianceListAsync(Memory<byte> packet)
        {
            try
            {
                if (packet.Length < 8)
                {
                    _logger.LogWarning("AllianceList packet too short: {Length}", packet.Length);
                    return Task.CompletedTask;
                }

                var span = packet.Span;
                int count = Math.Min(span[4], (packet.Length - 8) / AllianceGuildEntrySize);
                var guilds = new List<AllianceGuildInfo>(count);
                for (int i = 0; i < count; i++)
                {
                    var entry = span.Slice(8 + i * AllianceGuildEntrySize, AllianceGuildEntrySize);
                    guilds.Add(new AllianceGuildInfo(ReadName(entry[33..41]), entry[0], GuildInfo.UnpackMark(entry[1..33])));
                }

                Guilds.UpdateAllianceList(guilds);
                // ponytail: no alliance window yet (GuildInfoWindow alliance tab is a placeholder); list goes to chat.
                ShowSystemMessage(guilds.Count == 0
                    ? "Your guild is not part of an alliance."
                    : "Alliance: " + string.Join(", ", guilds.ConvertAll(g => $"{g.Name} ({g.MemberCount})")));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing AllianceList (0xE9).");
            }

            return Task.CompletedTask;
        }

        /// <summary>RemoveAllianceGuildResult (0xEB 0x01): result of kicking a guild from the alliance / leaving it.</summary>
        [PacketHandler(0xEB, 0x01)]
        public Task HandleRemoveAllianceGuildResultAsync(Memory<byte> packet)
        {
            try
            {
                if (packet.Length < 7)
                {
                    _logger.LogWarning("RemoveAllianceGuildResult packet too short: {Length}", packet.Length);
                    return Task.CompletedTask;
                }

                bool success = packet.Span[4] != 0;
                ShowSystemMessage(success ? "The guild has been removed from the alliance." : "The guild could not be removed from the alliance.");
                if (success && Guilds.HasGuild)
                    _ = _characterService.SendGuildInfoRequestAsync(Guilds.OwnGuildId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing RemoveAllianceGuildResult (0xEB 0x01).");
            }

            return Task.CompletedTask;
        }

        private static string ReadName(ReadOnlySpan<byte> field)
        {
            int end = field.IndexOf((byte)0);
            return System.Text.Encoding.UTF8.GetString(end < 0 ? field : field[..end]);
        }

        private static void ShowSystemMessage(string text)
        {
            MuGame.ScheduleOnMainThread(() =>
                (MuGame.Instance?.ActiveScene as GameScene)?.ChatLog?.AddMessage("System", text, MessageType.Info));
        }
    }
}
