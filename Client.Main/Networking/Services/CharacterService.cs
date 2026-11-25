using Microsoft.Extensions.Logging;
using MUnique.OpenMU.Network;
using Client.Main.Networking.PacketHandling;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MUnique.OpenMU.Network.Packets.ClientToServer;
using MUnique.OpenMU.Network.Packets;
using Client.Main.Core.Client;

namespace Client.Main.Networking.Services
{
    /// <summary>
    /// Manages sending character‐related packets to the game server,
    /// including character list requests, character selection, movement, and animations.
    /// </summary>
    public class CharacterService
    {
        private readonly ConnectionManager _connectionManager;
        private readonly ILogger<CharacterService> _logger;

        /// <summary>
        /// Tracks the last character name that was requested for deletion.
        /// Used to update the cached character list after successful deletion.
        /// </summary>
        public string LastDeletedCharacterName { get; internal set; }

        public CharacterService(
            ConnectionManager connectionManager,
            ILogger<CharacterService> logger)
        {
            _connectionManager = connectionManager;
            _logger = logger;
        }

        /// <summary>
        /// Sends a request to drop an item from inventory onto the ground at the specified tile.
        /// </summary>
        /// <param name="tileX">Target tile X.</param>
        /// <param name="tileY">Target tile Y.</param>
        /// <param name="inventorySlot">Inventory slot index (including server offset, e.g. 12 + y*8 + x).</param>
        public async Task SendDropItemRequestAsync(byte tileX, byte tileY, byte inventorySlot)
        {
            if (!_connectionManager.IsConnected)
            {
                _logger.LogError("Not connected - cannot send drop item request.");
                return;
            }

            _logger.LogInformation("Sending drop item request: Slot={Slot}, Pos=({X},{Y})...", inventorySlot, tileX, tileY);
            try
            {
                await _connectionManager.Connection.SendAsync(() =>
                    PacketBuilder.BuildDropItemRequestPacket(_connectionManager.Connection.Output, tileX, tileY, inventorySlot));
                _logger.LogInformation("Drop item request sent: Slot={Slot}, Pos=({X},{Y}).", inventorySlot, tileX, tileY);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending drop item request for slot {Slot} at ({X},{Y}).", inventorySlot, tileX, tileY);
            }
        }

        /// <summary>
        /// Requests the list of characters for the current account.
        /// </summary>
        public async Task RequestCharacterListAsync()
        {
            if (!_connectionManager.IsConnected)
            {
                _logger.LogError("Not connected — cannot request character list.");
                return;
            }

            _logger.LogInformation("Sending character list request...");
            try
            {
                await _connectionManager.Connection.SendAsync(() =>
                    PacketBuilder.BuildRequestCharacterListPacket(_connectionManager.Connection.Output));
                _logger.LogInformation("Character list request sent.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending character list request.");
            }
        }

        /// <summary>
        /// Sends a request to create a new character.
        /// </summary>
        /// <param name="characterName">The name of the new character.</param>
        /// <param name="characterClass">The class of the new character.</param>
        public async Task SendCreateCharacterRequestAsync(string characterName, CharacterClassNumber characterClass)
        {
            if (!_connectionManager.IsConnected)
            {
                _logger.LogError("Not connected — cannot create character.");
                return;
            }

            _logger.LogInformation("Sending create character request: Name={Name}, Class={Class}...",
                characterName, characterClass);
            try
            {
                await _connectionManager.Connection.SendCreateCharacterAsync(characterName, characterClass);

                _logger.LogInformation("Create character request sent: Name={Name}, Class={Class}.",
                    characterName, characterClass);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending create character request for {Name}.", characterName);
            }
        }

        /// <summary>
        /// Sends a request to delete a character.
        /// </summary>
        /// <param name="characterName">The name of the character to delete.</param>
        /// <param name="securityCode">The security code (default empty string for no security).</param>
        public async Task SendDeleteCharacterRequestAsync(string characterName, string securityCode = "")
        {
            if (!_connectionManager.IsConnected)
            {
                _logger.LogError("Not connected — cannot delete character.");
                return;
            }

            // Track the character name for use in the response handler
            LastDeletedCharacterName = characterName;

            _logger.LogInformation("Sending delete character request: Name={Name}...", characterName);
            try
            {
                await _connectionManager.Connection.SendDeleteCharacterAsync(characterName, securityCode);

                _logger.LogInformation("Delete character request sent: Name={Name}.", characterName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending delete character request for {Name}.", characterName);
            }
        }

        /// <summary>
        /// Sends a party invite to another player.
        /// </summary>
        public async Task SendPartyInviteAsync(ushort targetPlayerId)
        {
            if (!_connectionManager.IsConnected)
            {
                _logger.LogError("Not connected - cannot send party invite.");
                return;
            }

            _logger.LogInformation("Sending party invite to player ID {PlayerId}", targetPlayerId);
            try
            {
                await _connectionManager.Connection.SendAsync(() =>
                {
                    var packet = new PartyInviteRequest(_connectionManager.Connection.Output.GetMemory(PartyInviteRequest.Length).Slice(0, PartyInviteRequest.Length));
                    packet.TargetPlayerId = targetPlayerId;
                    return PartyInviteRequest.Length;
                });
                _logger.LogInformation("Party invite sent to player ID {PlayerId}", targetPlayerId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending party invite to player ID {PlayerId}", targetPlayerId);
            }
        }

        /// <summary>
        /// Sends a response to a party invitation.
        /// </summary>
        public async Task SendPartyResponseAsync(bool accepted, ushort requesterId)
        {
            if (!_connectionManager.IsConnected)
            {
                _logger.LogError("Not connected — cannot respond to party invite.");
                return;
            }

            _logger.LogInformation("Sending party response: Accepted={Accepted}, RequesterId={Id}", accepted, requesterId);
            try
            {
                await _connectionManager.Connection.SendAsync(() =>
                {
                    var packet = new PartyInviteResponse(_connectionManager.Connection.Output.GetMemory(PartyInviteResponse.Length).Slice(0, PartyInviteResponse.Length));
                    packet.Accepted = accepted;
                    packet.RequesterId = requesterId;
                    return PartyInviteResponse.Length;
                });
                _logger.LogInformation("Party response sent.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending party response.");
            }
        }

        /// <summary>
        /// Sends a request to kick a player from party (or leave party yourself).
        /// </summary>
        /// <param name="playerIndex">Index of player to kick (or your own index to leave party)</param>
        public async Task SendPartyKickRequestAsync(byte playerIndex)
        {
            if (!_connectionManager.IsConnected)
            {
                _logger.LogError("Not connected — cannot send party kick request.");
                return;
            }

            _logger.LogInformation("Sending party kick request for player index {PlayerIndex}", playerIndex);
            try
            {
                await _connectionManager.Connection.SendAsync(() =>
                {
                    var packet = new PartyPlayerKickRequest(_connectionManager.Connection.Output.GetMemory(PartyPlayerKickRequest.Length).Slice(0, PartyPlayerKickRequest.Length));
                    packet.PlayerIndex = playerIndex;
                    return PartyPlayerKickRequest.Length;
                });
                _logger.LogInformation("Party kick request sent.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending party kick request.");
            }
        }

        /// <summary>
        /// Sends a request for the current party list.
        /// </summary>
        public async Task RequestPartyListAsync()
        {
            if (!_connectionManager.IsConnected)
            {
                _logger.LogWarning("Not connected — cannot request party list.");
                return;
            }

            _logger.LogTrace("Sending PartyListRequest..."); // Użyj LogTrace, aby nie zaśmiecać logów
            try
            {
                await _connectionManager.Connection.SendAsync(() =>
                {
                    var packet = new PartyListRequest(_connectionManager.Connection.Output.GetMemory(PartyListRequest.Length).Slice(0, PartyListRequest.Length));
                    return PartyListRequest.Length;
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending PartyListRequest packet.");
            }
        }

        /// <summary>
        /// Sends a response to a guild join invitation.
        /// </summary>
        public async Task SendGuildJoinResponseAsync(bool accepted, ushort requesterId)
        {
            if (!_connectionManager.IsConnected)
            {
                _logger.LogError("Not connected — cannot respond to guild invite.");
                return;
            }

            _logger.LogInformation("Sending guild join response: Accepted={Accepted}, RequesterId={Id}", accepted, requesterId);
            try
            {
                await _connectionManager.Connection.SendAsync(() =>
                {
                    var packet = new GuildJoinResponse(_connectionManager.Connection.Output.GetMemory(GuildJoinResponse.Length).Slice(0, GuildJoinResponse.Length));
                    packet.Accepted = accepted;
                    packet.RequesterId = requesterId;
                    return GuildJoinResponse.Length;
                });
                _logger.LogInformation("Guild join response sent.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending guild join response.");
            }
        }

        /// <summary>
        /// Sends a response to a trade request.
        /// </summary>
        public async Task SendTradeResponseAsync(bool accepted)
        {
            if (!_connectionManager.IsConnected)
            {
                _logger.LogError("Not connected — cannot respond to trade request.");
                return;
            }

            _logger.LogInformation("Sending trade response: Accepted={Accepted}", accepted);
            try
            {
                await _connectionManager.Connection.SendAsync(() =>
                {
                    var packet = new TradeRequestResponse(_connectionManager.Connection.Output.GetMemory(TradeRequestResponse.Length).Slice(0, TradeRequestResponse.Length));
                    packet.TradeAccepted = accepted;
                    return TradeRequestResponse.Length;
                });
                _logger.LogInformation("Trade response sent.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending trade response.");
            }
        }

        /// <summary>
        /// Sends a trade request to another player.
        /// </summary>
        public async Task SendTradeRequestAsync(ushort targetPlayerId)
        {
            if (!_connectionManager.IsConnected)
            {
                _logger.LogError("Not connected — cannot send trade request.");
                return;
            }

            _logger.LogInformation("Sending trade request to player ID {PlayerId}", targetPlayerId);
            try
            {
                await _connectionManager.Connection.SendAsync(() =>
                {
                    var packet = new TradeRequest(_connectionManager.Connection.Output.GetMemory(TradeRequest.Length).Slice(0, TradeRequest.Length));
                    packet.PlayerId = targetPlayerId;
                    return TradeRequest.Length;
                });
                _logger.LogInformation("Trade request sent.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending trade request.");
            }
        }

        /// <summary>
        /// Sends a duel start request to another player.
        /// </summary>
        public async Task SendDuelStartRequestAsync(ushort targetPlayerId, string targetPlayerName)
        {
            if (!_connectionManager.IsConnected)
            {
                _logger.LogError("Not connected - cannot send duel request.");
                return;
            }

            _logger.LogInformation("Sending duel request to player ID {PlayerId}", targetPlayerId);
            try
            {
                await _connectionManager.Connection.SendAsync(() =>
                {
                    var packet = new DuelStartRequest(_connectionManager.Connection.Output.GetMemory(DuelStartRequest.Length).Slice(0, DuelStartRequest.Length));
                    packet.PlayerId = targetPlayerId;
                    packet.PlayerName = targetPlayerName ?? string.Empty;
                    return DuelStartRequest.Length;
                });
                _logger.LogInformation("Duel request sent.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending duel request to player ID {PlayerId}", targetPlayerId);
            }
        }

        /// <summary>
        /// Sends a guild join request to the specified guild master (target player).
        /// </summary>
        public async Task SendGuildJoinRequestAsync(ushort guildMasterPlayerId)
        {
            if (!_connectionManager.IsConnected)
            {
                _logger.LogError("Not connected - cannot send guild join request.");
                return;
            }

            _logger.LogInformation("Sending guild join request to guild master ID {PlayerId}", guildMasterPlayerId);
            try
            {
                await _connectionManager.Connection.SendAsync(() =>
                {
                    var packet = new GuildJoinRequest(_connectionManager.Connection.Output.GetMemory(GuildJoinRequest.Length).Slice(0, GuildJoinRequest.Length));
                    packet.GuildMasterPlayerId = guildMasterPlayerId;
                    return GuildJoinRequest.Length;
                });
                _logger.LogInformation("Guild join request sent.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending guild join request to player ID {PlayerId}", guildMasterPlayerId);
            }
        }

        /// <summary>Cuts a string to the UTF-8 size of a fixed packet field (the packet structs throw when it does not fit).</summary>
        private static string FitField(string value, int maxBytes)
        {
            value ??= string.Empty;
            while (value.Length > 0 && System.Text.Encoding.UTF8.GetByteCount(value) > maxBytes)
                value = value[..^1];
            return value;
        }

        /// <summary>
        /// Output buffer of <paramref name="length"/> bytes, zeroed: the packet structs only write their fields and
        /// the pooled buffer may still hold old data (e.g. OpenMU rejects LetterDeleteRequest unless byte 3 is 0).
        /// </summary>
        private Memory<byte> GetClearedPacketMemory(int length)
        {
            var memory = _connectionManager.Connection.Output.GetMemory(length).Slice(0, length);
            memory.Span.Clear();
            return memory;
        }

        /// <summary>
        /// Sends a guild packet built by <paramref name="write"/>; returns false when it could not be sent.
        /// </summary>
        private async Task<bool> SendSimplePacketAsync(string description, Func<int> write)
        {
            if (!_connectionManager.IsConnected)
            {
                _logger.LogError("Not connected - cannot send {Description}.", description);
                return false;
            }

            try
            {
                await _connectionManager.Connection.SendAsync(write);
                _logger.LogInformation("{Description} sent.", description);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending {Description}.", description);
                return false;
            }
        }

        // ─────────────────────────── Messenger ───────────────────────────

        private int _nextLetterId;

        /// <summary>Asks another character to become a friend (0xC1).</summary>
        public Task<bool> SendFriendAddRequestAsync(string friendName)
            => SendSimplePacketAsync($"friend add request ({friendName})", () =>
            {
                var packet = new FriendAddRequest(GetClearedPacketMemory(FriendAddRequest.Length));
                packet.FriendName = FitField(friendName, 10);
                return FriendAddRequest.Length;
            });

        /// <summary>Answers a friend request (0xC2).</summary>
        public Task<bool> SendFriendAddResponseAsync(bool accepted, string requesterName)
            => SendSimplePacketAsync("friend add response", () =>
            {
                var packet = new FriendAddResponse(GetClearedPacketMemory(FriendAddResponse.Length));
                packet.Accepted = accepted;
                packet.FriendRequesterName = FitField(requesterName, 10);
                return FriendAddResponse.Length;
            });

        /// <summary>Removes a friend from the friend list (0xC3).</summary>
        public Task<bool> SendFriendDeleteAsync(string friendName)
            => SendSimplePacketAsync($"friend delete ({friendName})", () =>
            {
                var packet = new FriendDelete(GetClearedPacketMemory(FriendDelete.Length));
                packet.FriendName = FitField(friendName, 10);
                return FriendDelete.Length;
            });

        /// <summary>Shows the own character online or offline in the friend lists of others (0xC4).</summary>
        public Task<bool> SendSetFriendOnlineStateAsync(bool online)
            => SendSimplePacketAsync("friend online state", () =>
            {
                var packet = new SetFriendOnlineState(GetClearedPacketMemory(SetFriendOnlineState.Length));
                packet.OnlineState = online;
                return SetFriendOnlineState.Length;
            });

        /// <summary>Sends a letter (0xC5). Rotation/animation describe the sender pose shown to the receiver.</summary>
        public Task<bool> SendLetterAsync(string receiver, string title, string message, byte rotation = 0, byte animation = 0)
        {
            uint letterId = (uint)System.Threading.Interlocked.Increment(ref _nextLetterId);
            message ??= string.Empty;
            return SendSimplePacketAsync($"letter to {receiver}", () =>
            {
                int length = LetterSendRequest.GetRequiredSize(message);
                var packet = new LetterSendRequest(GetClearedPacketMemory(length));
                packet.LetterId = letterId;
                packet.Receiver = FitField(receiver, 10);
                packet.Title = FitField(title, 60);
                packet.Rotation = rotation;
                packet.Animation = animation;
                packet.MessageLength = (ushort)System.Text.Encoding.UTF8.GetByteCount(message);
                packet.Message = message;
                return length;
            });
        }

        /// <summary>Requests the text of a letter (0xC7).</summary>
        public Task<bool> SendLetterReadRequestAsync(ushort letterIndex)
            => SendSimplePacketAsync($"letter read ({letterIndex})", () =>
            {
                var packet = new LetterReadRequest(GetClearedPacketMemory(LetterReadRequest.Length));
                packet.LetterIndex = letterIndex;
                return LetterReadRequest.Length;
            });

        /// <summary>Deletes a letter (0xC8).</summary>
        public Task<bool> SendLetterDeleteRequestAsync(ushort letterIndex)
            => SendSimplePacketAsync($"letter delete ({letterIndex})", () =>
            {
                var packet = new LetterDeleteRequest(GetClearedPacketMemory(LetterDeleteRequest.Length));
                packet.LetterIndex = letterIndex;
                return LetterDeleteRequest.Length;
            });

        /// <summary>Opens a chat room with an online friend (0xCA); answered with ChatRoomConnectionInfo.</summary>
        public Task<bool> SendChatRoomCreateRequestAsync(string friendName)
            => SendSimplePacketAsync($"chat room create ({friendName})", () =>
            {
                var packet = new ChatRoomCreateRequest(GetClearedPacketMemory(ChatRoomCreateRequest.Length));
                packet.FriendName = FitField(friendName, 10);
                return ChatRoomCreateRequest.Length;
            });

        /// <summary>Invites another friend into an existing chat room (0xCB).</summary>
        public Task<bool> SendChatRoomInvitationAsync(string friendName, ushort roomId, uint requestId)
            => SendSimplePacketAsync($"chat room invitation ({friendName})", () =>
            {
                var packet = new ChatRoomInvitationRequest(GetClearedPacketMemory(ChatRoomInvitationRequest.Length));
                packet.FriendName = FitField(friendName, 10);
                packet.RoomId = roomId;
                packet.RequestId = requestId;
                return ChatRoomInvitationRequest.Length;
            });

        /// <summary>Asks when a mini game opens (0x91): 1 DS, 2 BC, 4 CC, 5 IT, 6 Doppelganger.</summary>
        public Task<bool> SendMiniGameOpeningStateRequestAsync(byte miniGameType, byte eventLevel)
            => SendSimplePacketAsync("mini game opening state request", () =>
            {
                var span = GetClearedPacketMemory(5).Span;
                span[0] = 0xC1; span[1] = 5; span[2] = 0x91; span[3] = miniGameType; span[4] = eventLevel;
                return 5;
            });

        /// <summary>Enters Chaos Castle with the ticket in the given inventory slot (0xAF/0x01).</summary>
        public Task<bool> SendChaosCastleEnterRequestAsync(byte castleLevel, byte ticketSlot)
            => SendSimplePacketAsync("chaos castle enter request", () =>
            {
                var span = GetClearedPacketMemory(6).Span;
                span[0] = 0xC1; span[1] = 6; span[2] = 0xAF; span[3] = 0x01; span[4] = castleLevel; span[5] = ticketSlot;
                return 6;
            });

        /// <summary>Requests level/experience of a Dark Raven (0) or Dark Horse (1) item (0xA9).</summary>
        public Task<bool> SendPetInfoRequestAsync(byte petType, byte storage, byte slot)
            => SendSimplePacketAsync("pet info request", () =>
            {
                var memory = GetClearedPacketMemory(6);
                var span = memory.Span;
                span[0] = 0xC1;
                span[1] = 6;
                span[2] = 0xA9;
                span[3] = petType;
                span[4] = storage;
                span[5] = slot;
                return 6;
            });

        /// <summary>Saves the key configuration on the server (0xF3/0x30), see <see cref="KeyConfiguration"/>.</summary>
        public Task<bool> SendSaveKeyConfigurationAsync(byte[] configuration)
            => SendSimplePacketAsync("key configuration", () =>
            {
                int length = 4 + configuration.Length;
                var memory = GetClearedPacketMemory(length);
                var span = memory.Span;
                span[0] = 0xC1;
                span[1] = (byte)length;
                span[2] = 0xF3;
                span[3] = 0x30;
                configuration.CopyTo(span[4..]);
                return length;
            });

        /// <summary>Starts (<paramref name="pause"/> false) or stops the MU Helper (0xBF 0x51); answered by MuHelperStatusUpdate.</summary>
        public Task<bool> SendMuHelperStatusChangeAsync(bool pause)
            => SendSimplePacketAsync($"MU Helper status change (pause={pause})", () =>
            {
                new MuHelperStatusChangeRequest(GetClearedPacketMemory(MuHelperStatusChangeRequest.Length)).PauseStatus = pause;
                return MuHelperStatusChangeRequest.Length;
            });

        /// <summary>Saves the 257-byte MU Helper configuration (0xAE); the server echoes it with MuHelperConfigurationData.</summary>
        public Task<bool> SendMuHelperSaveDataAsync(byte[] data)
            => SendSimplePacketAsync("MU Helper configuration", () =>
            {
                var packet = new MuHelperSaveDataRequest(GetClearedPacketMemory(MuHelperSaveDataRequest.Length));
                data.AsSpan(0, Math.Min(data.Length, packet.HelperData.Length)).CopyTo(packet.HelperData);
                return MuHelperSaveDataRequest.Length;
            });

        /// <summary>Requests name and mark of a guild that is not cached yet (0x66).</summary>
        public Task<bool> SendGuildInfoRequestAsync(uint guildId)
            => SendSimplePacketAsync($"guild info request ({guildId})", () =>
            {
                var packet = new GuildInfoRequest(GetClearedPacketMemory(GuildInfoRequest.Length));
                packet.GuildId = guildId;
                return GuildInfoRequest.Length;
            });

        /// <summary>Requests the member list of the own guild (0x52).</summary>
        public Task<bool> SendGuildListRequestAsync()
            => SendSimplePacketAsync("guild list request", () =>
            {
                _ = new GuildListRequest(GetClearedPacketMemory(GuildListRequest.Length));
                return GuildListRequest.Length;
            });

        /// <summary>Requests to join a gens at the opened gens NPC (0xF8 01).</summary>
        public Task<bool> SendGensJoinRequestAsync(GensType gens)
            => SendSimplePacketAsync("gens join request", () =>
            {
                new GensJoinRequest(GetClearedPacketMemory(GensJoinRequest.Length)).GensType = gens;
                return GensJoinRequest.Length;
            });

        /// <summary>Requests to leave the gens at the opened gens NPC (0xF8 03).</summary>
        public Task<bool> SendGensLeaveRequestAsync()
            => SendSimplePacketAsync("gens leave request", () =>
            {
                _ = new GensLeaveRequest(GetClearedPacketMemory(GensLeaveRequest.Length));
                return GensLeaveRequest.Length;
            });

        /// <summary>Requests the ranking reward at the opened gens NPC (0xF8 09).</summary>
        public Task<bool> SendGensRewardRequestAsync(GensType gens)
            => SendSimplePacketAsync("gens reward request", () =>
            {
                new GensRewardRequest(GetClearedPacketMemory(GensRewardRequest.Length)).GensType = gens;
                return GensRewardRequest.Length;
            });

        /// <summary>Requests the own gens info (0xF8 0B); OpenMU answers with GensInfo (0xF8 07).</summary>
        public Task<bool> SendGensRankingRequestAsync()
            => SendSimplePacketAsync("gens ranking request", () =>
            {
                _ = new GensRankingRequest(GetClearedPacketMemory(GensRankingRequest.Length));
                return GensRankingRequest.Length;
            });

        /// <summary>
        /// Kicks a member (guild master), leaves the guild (own name) or disbands it (guild master, own name) (0x53).
        /// </summary>
        public Task<bool> SendGuildKickPlayerAsync(string playerName, string securityCode)
            => SendSimplePacketAsync("guild kick request", () =>
            {
                securityCode ??= string.Empty;
                int length = GuildKickPlayerRequest.GetRequiredSize(securityCode);
                var packet = new GuildKickPlayerRequest(GetClearedPacketMemory(length));
                packet.PlayerName = FitField(playerName, 10);
                packet.SecurityCode = securityCode;
                return length;
            });

        /// <summary>Answers the guild master NPC dialog: proceed to the creation window or cancel (0x54).</summary>
        public Task<bool> SendGuildMasterAnswerAsync(bool showCreationDialog)
            => SendSimplePacketAsync("guild master answer", () =>
            {
                var packet = new GuildMasterAnswer(GetClearedPacketMemory(GuildMasterAnswer.Length));
                packet.ShowCreationDialog = showCreationDialog;
                return GuildMasterAnswer.Length;
            });

        /// <summary>Creates a guild with the given name and packed 32-byte mark (0x55).</summary>
        public Task<bool> SendGuildCreateAsync(string guildName, byte[] packedMark)
            => SendSimplePacketAsync("guild create request", () =>
            {
                var packet = new GuildCreateRequest(GetClearedPacketMemory(GuildCreateRequest.Length));
                packet.GuildName = FitField(guildName, 8);
                packedMark.AsSpan(0, Math.Min(packedMark.Length, packet.GuildEmblem.Length)).CopyTo(packet.GuildEmblem);
                return GuildCreateRequest.Length;
            });

        /// <summary>Cancels the guild creation window (0x57).</summary>
        public Task<bool> SendCancelGuildCreationAsync()
            => SendSimplePacketAsync("guild creation cancel", () =>
            {
                _ = new CancelGuildCreation(GetClearedPacketMemory(CancelGuildCreation.Length));
                return CancelGuildCreation.Length;
            });

        /// <summary>Answers a guild war / battle soccer request of another guild master (0x61).</summary>
        public Task<bool> SendGuildWarResponseAsync(bool accepted)
            => SendSimplePacketAsync("guild war response", () =>
            {
                new GuildWarResponse(GetClearedPacketMemory(GuildWarResponse.Length)).Accepted = accepted;
                return GuildWarResponse.Length;
            });

        public const int GuildRelationshipChangeResponseLength = 8;

        /// <summary>Answers an alliance / hostility request (0xE6); type values must echo the request (0xE5).</summary>
        public Task<bool> SendGuildRelationshipChangeResponseAsync(byte relationshipType, byte requestType, bool accepted, ushort targetPlayerId)
            => SendSimplePacketAsync("guild relationship response", () =>
            {
                WriteGuildRelationshipChangeResponse(GetClearedPacketMemory(GuildRelationshipChangeResponseLength).Span,
                    relationshipType, requestType, accepted, targetPlayerId);
                return GuildRelationshipChangeResponseLength;
            });

        /// <summary>
        /// Written by hand: the OpenMU 0.9.8 struct writes TargetPlayerId little endian, the server definition is ShortBigEndian.
        /// </summary>
        public static void WriteGuildRelationshipChangeResponse(Span<byte> packet, byte relationshipType, byte requestType, bool accepted, ushort targetPlayerId)
        {
            packet[0] = 0xC1;
            packet[1] = GuildRelationshipChangeResponseLength;
            packet[2] = 0xE6;
            packet[3] = relationshipType;
            packet[4] = requestType;
            packet[5] = (byte)(accepted ? 1 : 0);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(packet[6..], targetPlayerId);
        }

        public const int GuildRelationshipChangeRequestLength = 7;

        /// <summary>Asks the guild master <paramref name="targetPlayerId"/> (in scope) for an alliance / hostility change (0xE5).</summary>
        public Task<bool> SendGuildRelationshipChangeRequestAsync(byte relationshipType, byte requestType, ushort targetPlayerId)
            => SendSimplePacketAsync("guild relationship request", () =>
            {
                WriteGuildRelationshipChangeRequest(GetClearedPacketMemory(GuildRelationshipChangeRequestLength).Span,
                    relationshipType, requestType, targetPlayerId);
                return GuildRelationshipChangeRequestLength;
            });

        /// <summary>Written by hand for the same reason as the 0xE6 response: TargetPlayerId is ShortBigEndian on the server.</summary>
        public static void WriteGuildRelationshipChangeRequest(Span<byte> packet, byte relationshipType, byte requestType, ushort targetPlayerId)
        {
            packet[0] = 0xC1;
            packet[1] = GuildRelationshipChangeRequestLength;
            packet[2] = 0xE5;
            packet[3] = relationshipType;
            packet[4] = requestType;
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(packet[5..], targetPlayerId);
        }

        /// <summary>Requests the guilds of the own alliance (0xE9); answered with AllianceList.</summary>
        public Task<bool> SendAllianceListRequestAsync()
            => SendSimplePacketAsync("alliance list request", () =>
            {
                _ = new RequestAllianceList(GetClearedPacketMemory(RequestAllianceList.Length));
                return RequestAllianceList.Length;
            });

        /// <summary>Removes a guild from the alliance (alliance master), or leaves it with the own guild name (0xEB 0x01).</summary>
        public Task<bool> SendRemoveAllianceGuildAsync(string guildName)
            => SendSimplePacketAsync("remove alliance guild request", () =>
            {
                new RemoveAllianceGuildRequest(GetClearedPacketMemory(RemoveAllianceGuildRequest.Length)).GuildName = FitField(guildName, 8);
                return RemoveAllianceGuildRequest.Length;
            });

        /// <summary>
        /// Requests the item list of another player's personal store.
        /// </summary>
        public async Task SendPlayerStoreListRequestAsync(ushort targetPlayerId, string targetPlayerName)
        {
            if (!_connectionManager.IsConnected)
            {
                _logger.LogError("Not connected - cannot request player store.");
                return;
            }

            _logger.LogInformation("Requesting player store list from player ID {PlayerId}", targetPlayerId);
            try
            {
                await _connectionManager.Connection.SendAsync(() =>
                {
                    var packet = new PlayerShopItemListRequest(_connectionManager.Connection.Output.GetMemory(PlayerShopItemListRequest.Length).Slice(0, PlayerShopItemListRequest.Length));
                    packet.PlayerId = targetPlayerId;
                    packet.PlayerName = targetPlayerName ?? string.Empty;
                    return PlayerShopItemListRequest.Length;
                });
                _logger.LogInformation("Player store list request sent.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error requesting player store list from player ID {PlayerId}", targetPlayerId);
            }
        }

        /// <summary>
        /// Sets the money amount in the trade.
        /// </summary>
        public async Task SendTradeMoneyAsync(uint amount)
        {
            if (!_connectionManager.IsConnected)
            {
                _logger.LogError("Not connected — cannot set trade money.");
                return;
            }

            _logger.LogInformation("Setting trade money to {Amount}", amount);
            try
            {
                await _connectionManager.Connection.SendAsync(() =>
                {
                    var packet = new SetTradeMoney(_connectionManager.Connection.Output.GetMemory(SetTradeMoney.Length).Slice(0, SetTradeMoney.Length));
                    packet.Amount = amount;
                    return SetTradeMoney.Length;
                });
                _logger.LogInformation("Trade money set sent.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending trade money.");
            }
        }

        /// <summary>
        /// Toggles the trade confirmation button (check/uncheck).
        /// </summary>
        public async Task SendTradeButtonAsync(bool pressed)
        {
            if (!_connectionManager.IsConnected)
            {
                _logger.LogError("Not connected — cannot toggle trade button.");
                return;
            }

            _logger.LogInformation("Setting trade button to {State}", pressed ? "Pressed" : "Unpressed");
            try
            {
                await _connectionManager.Connection.SendAsync(() =>
                {
                    var packet = new TradeButtonStateChange(_connectionManager.Connection.Output.GetMemory(TradeButtonStateChange.Length).Slice(0, TradeButtonStateChange.Length));
                    packet.NewState = pressed
                        ? TradeButtonState.Checked
                        : TradeButtonState.Unchecked;
                    _logger.LogDebug("TradeButtonStateChange payload: NewState={NewState} ({NewStateValue})", packet.NewState, (byte)packet.NewState);
                    if (_logger.IsEnabled(LogLevel.Debug))
                    {
                        try
                        {
                            Memory<byte> bytes = packet;
                            _logger.LogDebug("Sending TradeButtonStateChange ({Length} bytes): {Bytes}", bytes.Length, Convert.ToHexString(bytes.Span));
                        }
                        catch (Exception ex)
                        {
                            _logger.LogDebug(ex, "Failed to log TradeButtonStateChange bytes.");
                        }
                    }
                    return TradeButtonStateChange.Length;
                });
                _logger.LogInformation("Trade button toggle sent.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending trade button toggle.");
            }
        }

        /// <summary>
        /// Cancels the active trade.
        /// </summary>
        public async Task SendTradeCancelAsync()
        {
            if (!_connectionManager.IsConnected)
            {
                _logger.LogError("Not connected — cannot cancel trade.");
                return;
            }

            _logger.LogInformation("Cancelling trade");
            try
            {
                await _connectionManager.Connection.SendAsync(() =>
                {
                    var packet = new TradeCancel(_connectionManager.Connection.Output.GetMemory(TradeCancel.Length).Slice(0, TradeCancel.Length));
                    if (_logger.IsEnabled(LogLevel.Debug))
                    {
                        try
                        {
                            Memory<byte> bytes = packet;
                            _logger.LogDebug("Sending TradeCancel ({Length} bytes): {Bytes}", bytes.Length, Convert.ToHexString(bytes.Span));
                        }
                        catch (Exception ex)
                        {
                            _logger.LogDebug(ex, "Failed to log TradeCancel bytes.");
                        }
                    }
                    return TradeCancel.Length;
                });
                _logger.LogInformation("Trade cancel sent.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending trade cancel.");
            }
        }

        /// <summary>
        /// Sends a response to a duel request.
        /// </summary>
        public async Task SendDuelResponseAsync(bool accepted, ushort requesterId, string requesterName)
        {
            if (!_connectionManager.IsConnected)
            {
                _logger.LogError("Not connected — cannot respond to duel request.");
                return;
            }

            _logger.LogInformation("Sending duel response: Accepted={Accepted}, RequesterId={Id}", accepted, requesterId);
            try
            {
                await _connectionManager.Connection.SendAsync(() =>
                {
                    var packet = new DuelStartResponse(_connectionManager.Connection.Output.GetMemory(DuelStartResponse.Length).Slice(0, DuelStartResponse.Length));
                    packet.Response = accepted;
                    packet.PlayerId = requesterId;
                    packet.PlayerName = requesterName;
                    return DuelStartResponse.Length;
                });
                _logger.LogInformation("Duel response sent.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending duel response.");
            }
        }

        /// <summary>
        /// Sends a request to stop the currently running duel.
        /// </summary>
        public async Task SendDuelStopRequestAsync()
        {
            if (!_connectionManager.IsConnected)
            {
                _logger.LogError("Not connected — cannot send duel stop request.");
                return;
            }

            _logger.LogInformation("Sending duel stop request...");
            try
            {
                await _connectionManager.Connection.SendAsync(() =>
                {
                    var packet = new DuelStopRequest(_connectionManager.Connection.Output.GetMemory(DuelStopRequest.Length).Slice(0, DuelStopRequest.Length));
                    return DuelStopRequest.Length;
                });
                _logger.LogInformation("Duel stop request sent.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending duel stop request.");
            }
        }

        /// <summary>
        /// Sends a request to join a duel channel as spectator.
        /// </summary>
        public async Task SendDuelChannelJoinRequestAsync(byte channelId)
        {
            if (!_connectionManager.IsConnected)
            {
                _logger.LogError("Not connected — cannot send duel channel join request.");
                return;
            }

            _logger.LogInformation("Sending duel channel join request: ChannelId={ChannelId}", channelId);
            try
            {
                await _connectionManager.Connection.SendAsync(() =>
                {
                    var packet = new DuelChannelJoinRequest(_connectionManager.Connection.Output.GetMemory(DuelChannelJoinRequest.Length).Slice(0, DuelChannelJoinRequest.Length));
                    packet.ChannelId = channelId;
                    return DuelChannelJoinRequest.Length;
                });
                _logger.LogInformation("Duel channel join request sent.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending duel channel join request.");
            }
        }

        /// <summary>
        /// Sends a request to quit the duel channel spectator mode.
        /// </summary>
        public async Task SendDuelChannelQuitRequestAsync()
        {
            if (!_connectionManager.IsConnected)
            {
                _logger.LogError("Not connected — cannot send duel channel quit request.");
                return;
            }

            _logger.LogInformation("Sending duel channel quit request...");
            try
            {
                await _connectionManager.Connection.SendAsync(() =>
                {
                    var packet = new DuelChannelQuitRequest(_connectionManager.Connection.Output.GetMemory(DuelChannelQuitRequest.Length).Slice(0, DuelChannelQuitRequest.Length));
                    return DuelChannelQuitRequest.Length;
                });
                _logger.LogInformation("Duel channel quit request sent.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending duel channel quit request.");
            }
        }

        public async Task SendWarpCommandRequestAsync(ushort warpInfoIndex, uint commandKey = 0)
        {
            if (!_connectionManager.IsConnected)
            {
                _logger.LogError("Not connected — cannot send warp command request.");
                return;
            }

            _logger.LogInformation("Sending Warp Command Request for index {WarpInfoIndex}...", warpInfoIndex);
            try
            {
                await _connectionManager.Connection.SendAsync(() =>
                {
                    var packet = new WarpCommandRequest(_connectionManager.Connection.Output.GetMemory(WarpCommandRequest.Length).Slice(0, WarpCommandRequest.Length));
                    packet.CommandKey = commandKey;
                    packet.WarpInfoIndex = warpInfoIndex;
                    return WarpCommandRequest.Length;
                });
                _logger.LogInformation("Warp Command Request sent.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending Warp Command Request.");
            }
        }

        /// <summary>
        /// Sends a logout request instructing the server how to transition the client out of the current game session.
        /// </summary>
        public async Task SendLogoutRequestAsync(LogOutType type)
        {
            if (!_connectionManager.IsConnected)
            {
                _logger.LogError("Not connected — cannot send logout request.");
                return;
            }

            _logger.LogInformation("Sending logout request with type {Type}...", type);
            try
            {
                await _connectionManager.Connection.SendAsync(() =>
                    PacketBuilder.BuildLogoutRequestPacket(_connectionManager.Connection.Output, type));
                _logger.LogInformation("Logout request ({Type}) sent.", type);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending logout request ({Type}).", type);
            }
        }

        public async Task SendClientReadyAfterMapChangeAsync()
        {
            if (!_connectionManager.IsConnected)
            {
                _logger.LogError("Not connected — cannot send ClientReadyAfterMapChange.");
                return;
            }

            _logger.LogInformation("Sending ClientReadyAfterMapChange packet...");
            try
            {
                await _connectionManager.Connection.SendAsync(() =>
                    PacketBuilder.BuildClientReadyAfterMapChangePacket(_connectionManager.Connection.Output));
                _logger.LogInformation("ClientReadyAfterMapChange packet sent.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending ClientReadyAfterMapChange packet.");
            }
        }

        /// <summary>
        /// Selects the specified character on the game server.
        /// </summary>
        public async Task SelectCharacterAsync(string characterName)
        {
            if (!_connectionManager.IsConnected)
            {
                _logger.LogError("Not connected — cannot select character.");
                return;
            }

            _logger.LogInformation("Selecting character '{CharacterName}'...", characterName);
            try
            {
                await _connectionManager.Connection.SendAsync(() =>
                    PacketBuilder.BuildSelectCharacterPacket(_connectionManager.Connection.Output, characterName));
                _logger.LogInformation("Character selection packet sent.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending character selection packet.");
            }
        }

        /// <summary>
        /// Sends an instant move (teleport) request to the given coordinates.
        /// </summary>
        public async Task SendInstantMoveRequestAsync(byte x, byte y)
        {
            if (!_connectionManager.IsConnected)
            {
                _logger.LogError("Not connected — cannot move instantly.");
                return;
            }

            _logger.LogInformation("Sending instant move to ({X}, {Y})...", x, y);
            try
            {
                await _connectionManager.Connection.SendAsync(() =>
                    PacketBuilder.BuildInstantMoveRequestPacket(_connectionManager.Connection.Output, x, y));
                _logger.LogInformation("Instant move request sent.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending instant move request.");
            }
        }

        /// <summary>
        /// Sends an animation request with the specified rotation and animation number.
        /// </summary>
        public async Task SendAnimationRequestAsync(byte rotation, byte animationNumber)
        {
            if (!_connectionManager.IsConnected)
            {
                _logger.LogError("Not connected — cannot send animation request.");
                return;
            }

            _logger.LogInformation(
                "Sending animation request (rotation={Rotation}, animation={AnimationNumber})...",
                rotation, animationNumber);

            try
            {
                await _connectionManager.Connection.SendAsync(() =>
                    PacketBuilder.BuildAnimationRequestPacket(_connectionManager.Connection.Output, rotation, animationNumber));
                _logger.LogInformation("Animation request sent.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending animation request.");
            }
        }

        /// <summary>
        /// Sends a walk request along a path of direction steps.
        /// </summary>
        public async Task SendWalkRequestAsync(byte startX, byte startY, byte[] path)
        {
            if (!_connectionManager.IsConnected)
            {
                _logger.LogError("Not connected — cannot send walk request.");
                return;
            }
            if (path == null || path.Length == 0)
            {
                _logger.LogWarning("Empty path — walk request not sent.");
                return;
            }

            _logger.LogInformation(
                "Sending walk request from ({StartX}, {StartY}) with {Steps} steps...",
                startX, startY, path.Length);

            try
            {
                await _connectionManager.Connection.SendAsync(() =>
                    PacketBuilder.BuildWalkRequestPacket(
                        _connectionManager.Connection.Output,
                        startX, startY, path));
                _logger.LogInformation("Walk request sent.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending walk request.");
            }
        }

        /// <summary>
        /// Sends a hit request packet to the server.
        /// </summary>
        public async Task SendHitRequestAsync(ushort targetId, byte attackAnimation, byte lookingDirection)
        {
            if (!_connectionManager.IsConnected)
            {
                _logger.LogError("Not connected — cannot send hit request.");
                return;
            }

            _logger.LogInformation(
                "Sending hit request for TargetID: {TargetId}, Anim: {Animation}, Dir: {Direction}...",
                targetId, attackAnimation, lookingDirection);

            try
            {
                await _connectionManager.Connection.SendHitRequestAsync(targetId, attackAnimation, lookingDirection);

                _logger.LogInformation("Hit request sent.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending hit request.");
            }
        }

        /// <summary>
        /// Sends a skill usage request packet to the server.
        /// </summary>
        public async Task SendSkillRequestAsync(ushort skillId, ushort targetId)
        {
            if (!_connectionManager.IsConnected)
            {
                _logger.LogError("Not connected — cannot send skill request.");
                return;
            }

            _logger.LogInformation(
                "Sending skill request: SkillID={SkillId}, TargetID={TargetId}...",
                skillId, targetId);
            MuGame.Network?.GetCharacterState()?.NoteLocalSkillCast(skillId);

            try
            {
                await _connectionManager.Connection.SendAsync(() =>
                    PacketBuilder.BuildSkillRequestPacket(_connectionManager.Connection.Output, skillId, targetId));
                _logger.LogInformation("Skill request sent: SkillID={SkillId}, TargetID={TargetId}.", skillId, targetId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending skill request for skill {SkillId} on target {TargetId}.", skillId, targetId);
            }
        }

        /// <summary>
        /// Sends one of the four Dark Raven command modes used by the classic client.
        /// </summary>
        public async Task SendDarkRavenCommandAsync(PetCommandMode commandMode, ushort targetId)
        {
            if (!_connectionManager.IsConnected)
            {
                _logger.LogError("Not connected — cannot send Dark Raven command.");
                return;
            }

            try
            {
                await _connectionManager.Connection.SendPetCommandRequestAsync(
                    PetType.DarkRaven,
                    commandMode,
                    targetId);
                _logger.LogDebug(
                    "Dark Raven command sent: Mode={Mode}, TargetId={TargetId:X4}.",
                    commandMode,
                    targetId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending Dark Raven command {Mode}.", commandMode);
            }
        }

        /// <summary>
        /// Sends a consume item request packet to the server (potions, jewels, etc.).
        /// </summary>
        public async Task SendConsumeItemRequestAsync(byte itemSlot, byte targetSlot = 0)
        {
            if (!_connectionManager.IsConnected)
            {
                _logger.LogError("Not connected — cannot send consume item request.");
                return;
            }

            _logger.LogInformation(
                "Sending consume item request: ItemSlot={ItemSlot}, TargetSlot={TargetSlot}...",
                itemSlot, targetSlot);

            try
            {
                await _connectionManager.Connection.SendConsumeItemRequestAsync(itemSlot, targetSlot, default);

                _logger.LogInformation("Consume item request sent: ItemSlot={ItemSlot}, TargetSlot={TargetSlot}.", itemSlot, targetSlot);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending consume item request for slot {ItemSlot}.", itemSlot);
            }
        }

        /// <summary>
        /// Sends an area skill usage request packet to the server (buffs, area attacks, etc.).
        /// </summary>
        public async Task SendAreaSkillRequestAsync(ushort skillId, byte targetX, byte targetY, byte rotation, ushort extraTargetId = 0, byte animationCounter = 0)
        {
            if (!_connectionManager.IsConnected)
            {
                _logger.LogError("Not connected — cannot send area skill request.");
                return;
            }

            _logger.LogInformation(
                "Sending area skill request: SkillID={SkillId}, Position=({X},{Y}), Rotation={Rotation}...",
                skillId, targetX, targetY, rotation);

            try
            {
                await _connectionManager.Connection.SendAreaSkillAsync(skillId, targetX, targetY, rotation, extraTargetId, animationCounter);

                _logger.LogInformation("Area skill request sent: SkillID={SkillId}.", skillId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending area skill request for skill {SkillId}.", skillId);
            }
        }

        /// <summary>
        /// Sends an area skill hit packet (damage application) to the server (Season 6+: C3 DB).
        /// </summary>
        public async Task SendAreaSkillHitAsync(ushort skillId, byte targetX, byte targetY, byte hitCounter, IReadOnlyList<ushort> targetIds, byte animationCounter)
        {
            if (targetIds == null || targetIds.Count == 0)
                return;

            if (!_connectionManager.IsConnected)
            {
                _logger.LogError("Not connected — cannot send area skill hit.");
                return;
            }

            _logger.LogDebug(
                "Sending area skill hit: SkillID={SkillId}, Position=({X},{Y}), Targets={Count}, HitCounter={HitCounter}, AnimCounter={AnimCounter}...",
                skillId, targetX, targetY, targetIds.Count, hitCounter, animationCounter);

            try
            {
                await _connectionManager.Connection.SendAsync(() =>
                    PacketBuilder.BuildAreaSkillHitPacket(_connectionManager.Connection.Output, skillId, targetX, targetY, hitCounter, targetIds, animationCounter));
                _logger.LogTrace("Area skill hit sent: SkillID={SkillId}, Targets={Count}.", skillId, targetIds.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending area skill hit for skill {SkillId}.", skillId);
            }
        }

        /// <summary>
        /// Sends a request to increase a specific character stat attribute.
        /// </summary>
        /// <param name="attribute">The attribute to be increased.</param>
        public async Task SendIncreaseCharacterStatPointRequestAsync(CharacterStatAttribute attribute)
        {
            if (!_connectionManager.IsConnected)
            {
                _logger.LogError("Not connected — cannot send stat increase request.");
                return;
            }

            _logger.LogInformation("Sending stat increase request for attribute: {Attribute}...", attribute);
            try
            {
                await _connectionManager.Connection.SendIncreaseCharacterStatPointAsync(attribute);
                _logger.LogInformation("Stat increase request sent for attribute: {Attribute}.", attribute);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending stat increase request for attribute {Attribute}.", attribute);
            }
        }

        /// <summary>
        /// Sends a request to pick up a dropped item or money by its network ID.
        /// </summary>
        /// <returns>True if the request was sent successfully, false otherwise.</returns>
        public async Task<bool> SendPickupItemRequestAsync(ushort itemId, TargetProtocolVersion version)
        {
            ushort itemIdMasked = (ushort)(itemId & 0x7FFF);
            if (!_connectionManager.IsConnected)
            {
                _logger.LogError("Not connected - cannot send pickup item request.");
                return false;
            }

            _logger.LogInformation("Sending pickup item request for itemId: {ItemId}...", itemIdMasked);
            try
            {
                // Using the ConnectionExtensions directly based on protocol version
                switch (version)
                {
                    case TargetProtocolVersion.Season6:
                    case TargetProtocolVersion.Version097:
                        await _connectionManager.Connection.SendPickupItemRequestAsync(itemIdMasked);
                        break;
                    case TargetProtocolVersion.Version075:
                        await _connectionManager.Connection.SendPickupItemRequest075Async(itemIdMasked);
                        break;
                }
                _logger.LogInformation("Pickup item request sent for itemId: {ItemId}.", itemIdMasked);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending pickup item request for itemId {ItemId}.", itemIdMasked);
                return false;
            }
        }

        private volatile bool _npcDialogOpen;

        /// <summary>
        /// True between a talk-to-NPC request and the next close-NPC request. The server keeps the player in
        /// its NpcDialogOpened state meanwhile, so the client has to end the conversation (e.g. when walking away).
        /// </summary>
        public bool IsNpcDialogOpen => _npcDialogOpen;

        /// <summary>Forgets session state that cannot survive a dropped connection.</summary>
        public void ResetAfterDisconnect() => _npcDialogOpen = false;

        /// <summary>
        /// Sends a talk-to-NPC request to the server for the specified NPC network id (masked).
        /// </summary>
        public async Task SendTalkToNpcRequestAsync(ushort npcNetworkId)
        {
            if (!_connectionManager.IsConnected)
            {
                _logger.LogError("Not connected - cannot send talk to NPC request.");
                return;
            }

            // Ensure masked id (server expects 0x7FFF range)
            ushort masked = (ushort)(npcNetworkId & 0x7FFF);

            _logger.LogInformation("Sending TalkToNpcRequest for NPC {NpcId:X4}...", masked);
            try
            {
                await _connectionManager.Connection.SendAsync(() =>
                {
                    var len = TalkToNpcRequest.Length;
                    var packet = new TalkToNpcRequest(_connectionManager.Connection.Output.GetMemory(len).Slice(0, len));
                    packet.NpcId = masked; // BigEndian is handled by the struct
                    return len;
                });
                _npcDialogOpen = true;
                _logger.LogInformation("TalkToNpcRequest sent for NPC {NpcId:X4}.", masked);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending TalkToNpcRequest for NPC {NpcId:X4}.", masked);
            }
        }

        /// <summary>
        /// Sends a request to enter Devil Square (Charon NPC).
        /// </summary>
        /// <param name="squareLevel">Devil Square level index (0-based).</param>
        /// <param name="ticketInventoryIndex">Inventory slot index of the Devil's Invitation (includes offset 12), or 0xFF.</param>
        public async Task SendDevilSquareEnterRequestAsync(byte squareLevel, byte ticketInventoryIndex)
        {
            if (!_connectionManager.IsConnected)
            {
                _logger.LogError("Not connected - cannot send Devil Square enter request.");
                return;
            }

            _logger.LogInformation("Sending DevilSquareEnterRequest: Level={Level}, TicketSlot={Slot}", squareLevel, ticketInventoryIndex);
            try
            {
                await _connectionManager.Connection.SendAsync(() =>
                {
                    var len = DevilSquareEnterRequest.Length;
                    var packet = new DevilSquareEnterRequest(_connectionManager.Connection.Output.GetMemory(len).Slice(0, len));
                    packet.SquareLevel = squareLevel;
                    packet.TicketItemInventoryIndex = ticketInventoryIndex;
                    return len;
                });
                _logger.LogInformation("DevilSquareEnterRequest sent.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending DevilSquareEnterRequest.");
            }
        }

        /// <summary>
        /// Sends a request to enter Blood Castle.
        /// </summary>
        /// <param name="castleLevel">Blood Castle level (1-8).</param>
        /// <param name="ticketInventoryIndex">Inventory slot index of the Blood Bone or Scroll of Archangel (includes offset 12), or 0xFF.</param>
        public async Task SendBloodCastleEnterRequestAsync(byte castleLevel, byte ticketInventoryIndex)
        {
            if (!_connectionManager.IsConnected)
            {
                _logger.LogError("Not connected - cannot send Blood Castle enter request.");
                return;
            }

            _logger.LogInformation("Sending BloodCastleEnterRequest: Level={Level}, TicketSlot={Slot}", castleLevel, ticketInventoryIndex);
            try
            {
                await _connectionManager.Connection.SendAsync(() =>
                {
                    var len = BloodCastleEnterRequest.Length;
                    var packet = new BloodCastleEnterRequest(_connectionManager.Connection.Output.GetMemory(len).Slice(0, len));
                    packet.CastleLevel = castleLevel;
                    packet.TicketItemInventoryIndex = ticketInventoryIndex;
                    return len;
                });
                _logger.LogInformation("BloodCastleEnterRequest sent.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending BloodCastleEnterRequest.");
            }
        }

        /// <summary>
        /// Requests the legacy quest state list (C1 A0). In the original client this is sent after entering the game world.
        /// </summary>
        public async Task RequestLegacyQuestStateListAsync()
        {
            if (!_connectionManager.IsConnected)
            {
                _logger.LogError("Not connected - cannot request legacy quest state list.");
                return;
            }

            _logger.LogInformation("Requesting LegacyQuestStateList (0xA0) ...");
            try
            {
                await _connectionManager.Connection.SendAsync(() =>
                {
                    var len = LegacyQuestStateRequest.Length;
                    _ = new LegacyQuestStateRequest(_connectionManager.Connection.Output.GetMemory(len).Slice(0, len));
                    return len;
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending LegacyQuestStateRequest (0xA0).");
            }
        }

        // ───────────────────── Season 6 quest system (C1 F6) ─────────────────────

        /// <summary>AvailableQuestsRequest (F6 30): asks the opened quest NPC for its quests, answered by AvailableQuests (F6 0A).</summary>
        public Task<bool> SendAvailableQuestsRequestAsync()
            => SendSimplePacketAsync("AvailableQuestsRequest (F6 30)", () =>
            {
                _ = new AvailableQuestsRequest(GetClearedPacketMemory(AvailableQuestsRequest.Length));
                return AvailableQuestsRequest.Length;
            });

        /// <summary>QuestSelectRequest (F6 0A): the player picked a quest (its starting number) from the NPC quest list.</summary>
        public Task<bool> SendQuestSelectRequestAsync(ushort questNumber, ushort questGroup)
            => SendSimplePacketAsync($"QuestSelectRequest (F6 0A) {questGroup}/{questNumber}", () =>
            {
                var packet = new QuestSelectRequest(GetClearedPacketMemory(QuestSelectRequest.Length));
                packet.QuestNumber = questNumber;
                packet.QuestGroup = questGroup;
                return QuestSelectRequest.Length;
            });

        /// <summary>QuestProceedRequest (F6 0B): accepts or refuses the quest whose starting step is shown.</summary>
        public Task<bool> SendQuestProceedRequestAsync(ushort questNumber, ushort questGroup, bool accept)
            => SendSimplePacketAsync($"QuestProceedRequest (F6 0B) {questGroup}/{questNumber} accept={accept}", () =>
            {
                var packet = new QuestProceedRequest(GetClearedPacketMemory(QuestProceedRequest.Length));
                packet.QuestNumber = questNumber;
                packet.QuestGroup = questGroup;
                packet.ProceedAction = accept
                    ? QuestProceedRequest.QuestProceedAction.AcceptQuest
                    : QuestProceedRequest.QuestProceedAction.RefuseQuest;
                return QuestProceedRequest.Length;
            });

        /// <summary>QuestCompletionRequest (F6 0D): hands in the active quest of the group.</summary>
        public Task<bool> SendQuestCompletionRequestAsync(ushort questNumber, ushort questGroup)
            => SendSimplePacketAsync($"QuestCompletionRequest (F6 0D) {questGroup}/{questNumber}", () =>
            {
                var packet = new QuestCompletionRequest(GetClearedPacketMemory(QuestCompletionRequest.Length));
                packet.QuestNumber = questNumber;
                packet.QuestGroup = questGroup;
                return QuestCompletionRequest.Length;
            });

        /// <summary>ActiveQuestListRequest (F6 1A): OpenMU answers with QuestStateList (F6 1A).</summary>
        public Task<bool> SendActiveQuestListRequestAsync()
            => SendSimplePacketAsync("ActiveQuestListRequest (F6 1A)", () =>
            {
                _ = new ActiveQuestListRequest(GetClearedPacketMemory(ActiveQuestListRequest.Length));
                return ActiveQuestListRequest.Length;
            });

        /// <summary>QuestStateRequest (F6 1B): OpenMU answers with QuestState (F6 1B) if the quest is in progress.</summary>
        public Task<bool> SendQuestStateRequestAsync(ushort questNumber, ushort questGroup)
            => SendSimplePacketAsync($"QuestStateRequest (F6 1B) {questGroup}/{questNumber}", () =>
            {
                var packet = new QuestStateRequest(GetClearedPacketMemory(QuestStateRequest.Length));
                packet.QuestNumber = questNumber;
                packet.QuestGroup = questGroup;
                return QuestStateRequest.Length;
            });

        /// <summary>QuestCancelRequest (F6 0F): OpenMU resets the active quest and answers with QuestCancelled (F6 0F).</summary>
        public Task<bool> SendQuestCancelRequestAsync(ushort questNumber, ushort questGroup)
            => SendSimplePacketAsync($"QuestCancelRequest (F6 0F) {questGroup}/{questNumber}", () =>
            {
                var packet = new QuestCancelRequest(GetClearedPacketMemory(QuestCancelRequest.Length));
                packet.QuestNumber = questNumber;
                packet.QuestGroup = questGroup;
                return QuestCancelRequest.Length;
            });

        /// <summary>
        /// Sends a legacy quest progress request (C1 A2). SourceMain5.2 sends NewState=1 for progression.
        /// </summary>
        public async Task SendLegacyQuestProceedRequestAsync(byte questNumber)
        {
            if (!_connectionManager.IsConnected)
            {
                _logger.LogError("Not connected - cannot send legacy quest proceed request.");
                return;
            }

            _logger.LogInformation("Sending LegacyQuestStateSetRequest (0xA2): QuestNumber={QuestNumber}", questNumber);
            try
            {
                await _connectionManager.Connection.SendAsync(() =>
                {
                    var len = LegacyQuestStateSetRequest.Length;
                    var packet = new LegacyQuestStateSetRequest(_connectionManager.Connection.Output.GetMemory(len).Slice(0, len));
                    packet.QuestNumber = questNumber;
                    packet.NewState = (LegacyQuestState)1;
                    return len;
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending LegacyQuestStateSetRequest (0xA2).");
            }
        }

        /// <summary>
        /// Sends EnterOnWerewolfRequest (C1 D0 07) to enter Barracks of Balgass during 3rd class quest.
        /// </summary>
        public async Task SendEnterOnWerewolfRequestAsync()
        {
            if (!_connectionManager.IsConnected)
            {
                _logger.LogError("Not connected - cannot send EnterOnWerewolfRequest.");
                return;
            }

            _logger.LogInformation("Sending EnterOnWerewolfRequest (0xD0/0x07) ...");
            try
            {
                await _connectionManager.Connection.SendAsync(() =>
                {
                    var len = EnterOnWerewolfRequest.Length;
                    _ = new EnterOnWerewolfRequest(_connectionManager.Connection.Output.GetMemory(len).Slice(0, len));
                    return len;
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending EnterOnWerewolfRequest.");
            }
        }

        /// <summary>
        /// Sends EnterOnGatekeeperRequest (C1 D0 08) to enter Balgass Refuge during 3rd class quest.
        /// </summary>
        public async Task SendEnterOnGatekeeperRequestAsync()
        {
            if (!_connectionManager.IsConnected)
            {
                _logger.LogError("Not connected - cannot send EnterOnGatekeeperRequest.");
                return;
            }

            _logger.LogInformation("Sending EnterOnGatekeeperRequest (0xD0/0x08) ...");
            try
            {
                await _connectionManager.Connection.SendAsync(() =>
                {
                    var len = EnterOnGatekeeperRequest.Length;
                    _ = new EnterOnGatekeeperRequest(_connectionManager.Connection.Output.GetMemory(len).Slice(0, len));
                    return len;
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending EnterOnGatekeeperRequest.");
            }
        }

        /// <summary>
        /// Sends a close-NPC dialog request to the server.
        /// </summary>
        public async Task SendCloseNpcRequestAsync()
        {
            if (!_connectionManager.IsConnected)
            {
                _logger.LogError("Not connected - cannot send close NPC request.");
                return;
            }

            _npcDialogOpen = false;
            _logger.LogInformation("Sending CloseNpcRequest (0x31) ...");
            try
            {
                await _connectionManager.Connection.SendAsync(() =>
                {
                    var len = CloseNpcRequest.Length;
                    var packet = new CloseNpcRequest(_connectionManager.Connection.Output.GetMemory(len).Slice(0, len));
                    return len;
                });
                _logger.LogInformation("CloseNpcRequest sent.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending CloseNpcRequest.");
            }
        }

        /// <summary>
        /// Sends a chaos machine mix request (C1 86) with a mix type id.
        /// </summary>
        public async Task SendChaosMachineMixRequestAsync(int mixType, byte socketSlot)
        {
            if (!_connectionManager.IsConnected)
            {
                _logger.LogError("Not connected - cannot send chaos machine mix request.");
                return;
            }

            _logger.LogInformation("Sending ChaosMachineMixRequest: MixType={MixType}, SocketSlot={SocketSlot}", mixType, socketSlot);
            try
            {
                await _connectionManager.Connection.SendAsync(() =>
                {
                    var len = ChaosMachineMixRequest.Length;
                    var packet = new ChaosMachineMixRequest(_connectionManager.Connection.Output.GetMemory(len).Slice(0, len));
                    packet.MixType = (ChaosMachineMixRequest.ChaosMachineMixType)mixType;
                    packet.SocketSlot = socketSlot;
                    return len;
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending ChaosMachineMixRequest.");
            }
        }

        /// <summary>
        /// Sends CraftingDialogCloseRequest (C1 87) to notify server that the crafting dialog was closed.
        /// </summary>
        public async Task SendCraftingDialogCloseRequestAsync()
        {
            if (!_connectionManager.IsConnected)
            {
                _logger.LogError("Not connected - cannot send crafting dialog close request.");
                return;
            }

            _logger.LogInformation("Sending CraftingDialogCloseRequest (0x87) ...");
            try
            {
                await _connectionManager.Connection.SendAsync(() =>
                {
                    var len = CraftingDialogCloseRequest.Length;
                    _ = new CraftingDialogCloseRequest(_connectionManager.Connection.Output.GetMemory(len).Slice(0, len));
                    return len;
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending CraftingDialogCloseRequest.");
            }
        }

        /// <summary>
        /// Requests to move money between inventory and vault storage.
        /// </summary>
        public async Task SendVaultMoveMoneyAsync(VaultMoveMoneyRequest.VaultMoneyMoveDirection direction, uint amount)
        {
            if (!_connectionManager.IsConnected)
            {
                _logger.LogError("Not connected - cannot move vault money.");
                return;
            }

            _logger.LogInformation("Sending VaultMoveMoneyRequest: Direction={Direction}, Amount={Amount}", direction, amount);
            try
            {
                await _connectionManager.Connection.SendAsync(() =>
                {
                    var packet = new VaultMoveMoneyRequest(_connectionManager.Connection.Output.GetMemory(VaultMoveMoneyRequest.Length).Slice(0, VaultMoveMoneyRequest.Length));
                    packet.Direction = direction;
                    packet.Amount = amount;
                    return VaultMoveMoneyRequest.Length;
                });
                _logger.LogInformation("VaultMoveMoneyRequest sent.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending VaultMoveMoneyRequest.");
            }
        }

        /// <summary>
        /// Sends a buy request for the given NPC shop slot.
        /// </summary>
        public async Task SendBuyItemFromNpcRequestAsync(byte shopSlot)
        {
            if (!_connectionManager.IsConnected)
            {
                _logger.LogError("Not connected - cannot send buy item request.");
                return;
            }

            _logger.LogInformation("Sending BuyItemFromNpcRequest for slot {Slot}...", shopSlot);
            try
            {
                await _connectionManager.Connection.SendAsync(() =>
                {
                    var len = BuyItemFromNpcRequest.Length;
                    var packet = new BuyItemFromNpcRequest(_connectionManager.Connection.Output.GetMemory(len).Slice(0, len));
                    packet.ItemSlot = shopSlot;
                    return len;
                });
                _logger.LogInformation("BuyItemFromNpcRequest sent for slot {Slot}.", shopSlot);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending BuyItemFromNpcRequest for slot {Slot}.", shopSlot);
            }
        }

        /// <summary>
        /// Sends a request to sell an item from the inventory to the currently opened NPC merchant.
        /// </summary>
        public async Task SendSellItemToNpcRequestAsync(byte inventorySlot)
        {
            if (!_connectionManager.IsConnected)
            {
                _logger.LogError("Not connected - cannot send sell item request.");
                return;
            }

            _logger.LogInformation("Sending SellItemToNpcRequest for inv slot {Slot}...", inventorySlot);
            try
            {
                await _connectionManager.Connection.SendAsync(() =>
                {
                    var len = SellItemToNpcRequest.Length;
                    var packet = new SellItemToNpcRequest(_connectionManager.Connection.Output.GetMemory(len).Slice(0, len));
                    packet.ItemSlot = inventorySlot;
                    return len;
                });
                _logger.LogInformation("SellItemToNpcRequest sent for slot {Slot}.", inventorySlot);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending SellItemToNpcRequest for inv slot {Slot}.", inventorySlot);
            }
        }

        /// <summary>
        /// Sends an inventory item move request (drag and drop inside inventory) using the proper packet format for the protocol version.
        /// </summary>
        /// <param name="fromSlot">Source slot index (including server offset).</param>
        /// <param name="toSlot">Destination slot index (including server offset).</param>
        /// <param name="version">Target protocol version.</param>
        /// <param name="itemData">Raw item data for Season6 (12 bytes expected). Optional for other versions.</param>
        public async Task SendItemMoveRequestAsync(byte fromSlot, byte toSlot, TargetProtocolVersion version, byte[] itemData)
        {
            if (!_connectionManager.IsConnected)
            {
                _logger.LogError("Not connected - cannot send item move request.");
                return;
            }

            const ItemStorageKind store = ItemStorageKind.Inventory;

            try
            {
                switch (version)
                {
                    case TargetProtocolVersion.Season6:
                        if (itemData == null || itemData.Length < 12)
                        {
                            _logger.LogWarning("Season6 item move missing 12-byte item data. Falling back to extended packet.");
                            await _connectionManager.Connection.SendAsync(() =>
                                PacketBuilder.BuildItemMoveRequestExtendedPacket(
                                    _connectionManager.Connection.Output, store, fromSlot, store, toSlot));
                        }
                        else
                        {
                            await _connectionManager.Connection.SendAsync(() =>
                                PacketBuilder.BuildItemMoveRequestPacket(
                                    _connectionManager.Connection.Output, store, fromSlot, itemData, store, toSlot));
                        }
                        break;

                    case TargetProtocolVersion.Version097:
                    case TargetProtocolVersion.Version075:
                    default:
                        await _connectionManager.Connection.SendAsync(() =>
                            PacketBuilder.BuildItemMoveRequestExtendedPacket(
                                _connectionManager.Connection.Output, store, fromSlot, store, toSlot));
                        break;
                }

                _logger.LogInformation("Item move request sent: {From} -> {To}", fromSlot, toSlot);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending item move request from {From} to {To}.", fromSlot, toSlot);
            }
        }

        /// <summary>
        /// Sends an item move between arbitrary storages (for example, Inventory ↔ Vault or Vault ↔ Vault).
        /// </summary>
        public async Task SendStorageItemMoveAsync(
            ItemStorageKind fromStorage,
            byte fromSlot,
            ItemStorageKind toStorage,
            byte toSlot,
            TargetProtocolVersion version,
            byte[] itemData)
        {
            if (!_connectionManager.IsConnected)
            {
                _logger.LogError("Not connected - cannot send storage item move request.");
                return;
            }

            try
            {
                switch (version)
                {
                    case TargetProtocolVersion.Season6:
                        bool hasItemData = itemData != null && itemData.Length >= 12;
                        _logger.LogDebug("Storage move (S6) using {PacketType}: {FromStore}[{From}] -> {ToStore}[{To}]",
                            hasItemData ? "ItemMoveRequest" : "ItemMoveRequestExtended",
                            fromStorage, fromSlot, toStorage, toSlot);

                        if (!hasItemData)
                        {
                            await _connectionManager.Connection.SendAsync(() =>
                                PacketBuilder.BuildItemMoveRequestExtendedPacket(
                                    _connectionManager.Connection.Output,
                                    fromStorage, fromSlot, toStorage, toSlot));
                        }
                        else
                        {
                            await _connectionManager.Connection.SendAsync(() =>
                                PacketBuilder.BuildItemMoveRequestPacket(
                                    _connectionManager.Connection.Output,
                                    fromStorage, fromSlot, itemData, toStorage, toSlot));
                        }
                        break;

                    default:
                        await _connectionManager.Connection.SendAsync(() =>
                            PacketBuilder.BuildItemMoveRequestExtendedPacket(
                                _connectionManager.Connection.Output,
                                fromStorage, fromSlot, toStorage, toSlot));
                        break;
                }

                _logger.LogInformation("Storage item move sent: {FromStore}[{From}] -> {ToStore}[{To}]",
                    fromStorage, fromSlot, toStorage, toSlot);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending storage item move {FromStore}[{From}] -> {ToStore}[{To}]",
                    fromStorage, fromSlot, toStorage, toSlot);
            }
        }

        public async Task SendEnterGateRequestAsync(ushort gateId, byte teleportTargetX = 0, byte teleportTargetY = 0)
        {
            if (!_connectionManager.IsConnected)
            {
                _logger.LogError("Not connected — cannot send enter gate request.");
                return;
            }

            if (gateId == 0 && (teleportTargetX != 0 || teleportTargetY != 0))
            {
                _logger.LogInformation("Sending teleport request to ({X},{Y})...", teleportTargetX, teleportTargetY);
            }
            else
            {
                _logger.LogInformation("Sending enter gate request for gate {GateId}...", gateId);
            }
            try
            {
                await _connectionManager.Connection.SendAsync(() =>
                {
                    var packet = new EnterGateRequest(_connectionManager.Connection.Output.GetMemory(EnterGateRequest.Length).Slice(0, EnterGateRequest.Length));
                    packet.GateNumber = gateId;
                    packet.TeleportTargetX = teleportTargetX;
                    packet.TeleportTargetY = teleportTargetY;
                    return EnterGateRequest.Length;
                });
                if (gateId == 0 && (teleportTargetX != 0 || teleportTargetY != 0))
                {
                    _logger.LogInformation("Teleport request sent to ({X},{Y}).", teleportTargetX, teleportTargetY);
                }
                else
                {
                    _logger.LogInformation("Enter gate request sent for gate {GateId}.", gateId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending enter gate request for gate {GateId}.", gateId);
            }
        }

        /// <summary>Sends KanturuEnterRequest (0xD1/0x01) from the Gateway Machine dialog.</summary>
        public async Task SendKanturuEnterRequestAsync()
        {
            if (!_connectionManager.IsConnected) return;
            try
            {
                await _connectionManager.Connection.SendAsync(() =>
                {
                    _ = new KanturuEnterRequest(_connectionManager.Connection.Output.GetMemory(KanturuEnterRequest.Length).Slice(0, KanturuEnterRequest.Length));
                    return KanturuEnterRequest.Length;
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending Kanturu enter request.");
            }
        }

        /// <summary>Sends CrywolfChaosRateBenefitRequest (0xBD/0x09); the server answers with the chaos rate benefit of the Crywolf result.</summary>
        public async Task SendCrywolfChaosRateBenefitRequestAsync()
        {
            if (!_connectionManager.IsConnected) return;
            try
            {
                await _connectionManager.Connection.SendAsync(() =>
                {
                    _ = new CrywolfChaosRateBenefitRequest(_connectionManager.Connection.Output.GetMemory(CrywolfChaosRateBenefitRequest.Length).Slice(0, CrywolfChaosRateBenefitRequest.Length));
                    return CrywolfChaosRateBenefitRequest.Length;
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending Crywolf chaos rate benefit request.");
            }
        }

        /// <summary>Sends CrywolfContractRequest (0xBD/0x03) for the altar standing at the player's tile (masked altar id).</summary>
        public async Task SendCrywolfContractRequestAsync(ushort altarNetworkId)
        {
            if (!_connectionManager.IsConnected) return;
            try
            {
                await _connectionManager.Connection.SendAsync(() =>
                {
                    var packet = new CrywolfContractRequest(_connectionManager.Connection.Output.GetMemory(CrywolfContractRequest.Length).Slice(0, CrywolfContractRequest.Length));
                    packet.StatueId = (ushort)(altarNetworkId & 0x7FFF);
                    return CrywolfContractRequest.Length;
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending Crywolf contract request.");
            }
        }

        // Event NPC dialogs. The Write* methods build the packets into a cleared span of the packet length
        // and are public for the offline packet checks.

        /// <summary>DoppelgangerEnterRequest (0xBF/0x0E); slot 0xFF lets the server find the ticket.</summary>
        public static int WriteDoppelgangerEnterRequest(Span<byte> span, byte ticketSlot)
        {
            _ = new DoppelgangerEnterRequestRef(span) { TicketItemSlot = ticketSlot };
            return DoppelgangerEnterRequestRef.Length;
        }

        /// <summary>IllusionTempleEnterRequest (0xBF/0x00) for temple 1-6; slot 0xFF lets the server find the scroll.</summary>
        public static int WriteIllusionTempleEnterRequest(Span<byte> span, byte temple, byte ticketSlot)
        {
            _ = new IllusionTempleEnterRequestRef(span) { MapNumber = temple, ItemSlot = ticketSlot };
            return IllusionTempleEnterRequestRef.Length;
        }

        public const int IllusionTempleSkillRequestLength = 9;

        /// <summary>
        /// IllusionTempleSkillRequest (0xBF/0x02) for the special skills 210-213: C1 09 BF 02, skill and target big endian,
        /// distance (ignored by OpenMU). Written by hand: the packet struct of the referenced Network.Packets package has a
        /// one-byte target, the server reads the 9-byte layout of its ClientToServerPackets.xml.
        /// </summary>
        public static int WriteIllusionTempleSkillRequest(Span<byte> span, ushort skill, ushort targetId, byte distance)
        {
            span[0] = 0xC1;
            span[1] = IllusionTempleSkillRequestLength;
            span[2] = 0xBF;
            span[3] = 0x02;
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(span[4..], skill);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(span[6..], targetId);
            span[8] = distance;
            return IllusionTempleSkillRequestLength;
        }

        /// <summary>LuckyCoinCountRequest (0xBF/0x0B).</summary>
        public static int WriteLuckyCoinCountRequest(Span<byte> span)
        {
            _ = new LuckyCoinCountRequestRef(span);
            return LuckyCoinCountRequestRef.Length;
        }

        /// <summary>LuckyCoinRegistrationRequest (0xBF/0x0C): registers one coin of the inventory.</summary>
        public static int WriteLuckyCoinRegistrationRequest(Span<byte> span)
        {
            _ = new LuckyCoinRegistrationRequestRef(span);
            return LuckyCoinRegistrationRequestRef.Length;
        }

        /// <summary>LuckyCoinExchangeRequest (0xBF/0x0D) for 10, 20 or 30 coins.</summary>
        public static int WriteLuckyCoinExchangeRequest(Span<byte> span, uint coinCount)
        {
            _ = new LuckyCoinExchangeRequestRef(span) { CoinCount = coinCount };
            return LuckyCoinExchangeRequestRef.Length;
        }

        /// <summary>EventChipRegistrationRequest (0x95) at the Golden Archer; OpenMU ignores type and item index.</summary>
        public static int WriteEventChipRegistrationRequest(Span<byte> span)
        {
            _ = new EventChipRegistrationRequestRef(span);
            return EventChipRegistrationRequestRef.Length;
        }

        public Task<bool> SendDoppelgangerEnterRequestAsync(byte ticketSlot = 0xFF)
            => SendSimplePacketAsync("doppelganger enter request", () => WriteDoppelgangerEnterRequest(GetClearedPacketMemory(DoppelgangerEnterRequestRef.Length).Span, ticketSlot));

        public Task<bool> SendIllusionTempleEnterRequestAsync(byte temple, byte ticketSlot = 0xFF)
            => SendSimplePacketAsync("illusion temple enter request", () => WriteIllusionTempleEnterRequest(GetClearedPacketMemory(IllusionTempleEnterRequestRef.Length).Span, temple, ticketSlot));

        public Task<bool> SendIllusionTempleSkillRequestAsync(ushort skill, ushort targetId, byte distance = 0)
            => SendSimplePacketAsync("illusion temple skill request", () => WriteIllusionTempleSkillRequest(GetClearedPacketMemory(IllusionTempleSkillRequestLength).Span, skill, targetId, distance));

        public Task<bool> SendLuckyCoinCountRequestAsync()
            => SendSimplePacketAsync("lucky coin count request", () => WriteLuckyCoinCountRequest(GetClearedPacketMemory(LuckyCoinCountRequestRef.Length).Span));

        public Task<bool> SendLuckyCoinRegistrationRequestAsync()
            => SendSimplePacketAsync("lucky coin registration request", () => WriteLuckyCoinRegistrationRequest(GetClearedPacketMemory(LuckyCoinRegistrationRequestRef.Length).Span));

        public Task<bool> SendLuckyCoinExchangeRequestAsync(uint coinCount)
            => SendSimplePacketAsync("lucky coin exchange request", () => WriteLuckyCoinExchangeRequest(GetClearedPacketMemory(LuckyCoinExchangeRequestRef.Length).Span, coinCount));

        public Task<bool> SendEventChipRegistrationRequestAsync()
            => SendSimplePacketAsync("event chip registration request", () => WriteEventChipRegistrationRequest(GetClearedPacketMemory(EventChipRegistrationRequestRef.Length).Span));

        /// <summary>
        /// Sends a request to an NPC to receive a buff.
        /// Must be called after opening NPC dialog with SendTalkToNpcRequestAsync.
        /// </summary>
        public async Task SendNpcBuffRequestAsync()
        {
            if (!_connectionManager.IsConnected)
            {
                _logger.LogError("Not connected - cannot send NPC buff request.");
                return;
            }

            _logger.LogInformation("Sending NPC buff request...");
            try
            {
                await _connectionManager.Connection.SendAsync(() =>
                {
                    var packet = new NpcBuffRequest(_connectionManager.Connection.Output.GetMemory(NpcBuffRequest.Length).Slice(0, NpcBuffRequest.Length));
                    return NpcBuffRequest.Length;
                });
                _logger.LogInformation("NPC buff request sent.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending NPC buff request.");
            }
        }

        /// <summary>
        /// Sends a complete sequence to get buff from Elf Soldier NPC:
        /// 1. Opens dialog with NPC (TalkToNpcRequest)
        /// 2. Requests buff (NpcBuffRequest)
        /// </summary>
        /// <param name="npcId">The network ID of the Elf Soldier NPC.</param>
        public async Task SendElfSoldierBuffSequenceAsync(ushort npcId)
        {
            if (!_connectionManager.IsConnected)
            {
                _logger.LogError("Not connected - cannot send Elf Soldier buff sequence.");
                return;
            }

            _logger.LogInformation("Sending Elf Soldier buff sequence for NPC ID {NpcId}...", npcId);

            // Step 1: Open dialog with NPC
            await SendTalkToNpcRequestAsync(npcId);

            // Step 2: Small delay to ensure dialog is processed
            await Task.Delay(100);

            // Step 3: Request buff
            await SendNpcBuffRequestAsync();

            // Step 4: Close NPC dialog
            await SendCloseNpcRequestAsync();

            _logger.LogInformation("Elf Soldier buff sequence completed for NPC ID {NpcId}.", npcId);
        }

        /// <summary>
        /// Sends a repair item request to the server (C1 34).
        /// </summary>
        /// <param name="inventorySlot">The inventory slot of the item to repair. Use 0xFF for "repair all".</param>
        /// <param name="isSelfRepair">True if repairing from inventory (costs 2.5x more), false if using NPC shop.</param>
        public async Task SendRepairItemRequestAsync(byte inventorySlot, bool isSelfRepair)
        {
            if (!_connectionManager.IsConnected)
            {
                _logger.LogError("Not connected - cannot send repair item request.");
                return;
            }

            _logger.LogInformation("Sending RepairItemRequest for slot {Slot}, selfRepair={SelfRepair}...", inventorySlot, isSelfRepair);
            try
            {
                await _connectionManager.Connection.SendAsync(() =>
                {
                    var len = RepairItemRequest.Length;
                    var packet = new RepairItemRequest(_connectionManager.Connection.Output.GetMemory(len).Slice(0, len));
                    packet.ItemSlot = inventorySlot;
                    packet.IsSelfRepair = isSelfRepair;
                    return len;
                });
                _logger.LogInformation("RepairItemRequest sent for slot {Slot}.", inventorySlot);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending RepairItemRequest for slot {Slot}.", inventorySlot);
            }
        }

        // ────────────────── Personal Shop ──────────────────

        /// <summary>
        /// Opens a personal shop with the given store name.
        /// </summary>
        public async Task SendPlayerShopOpenAsync(string storeName)
        {
            if (!_connectionManager.IsConnected)
            {
                _logger.LogError("Not connected - cannot open shop.");
                return;
            }

            _logger.LogInformation("Opening personal shop: {StoreName}", storeName);
            try
            {
                await _connectionManager.Connection.SendAsync(() =>
                {
                    var len = PlayerShopOpen.Length;
                    var packet = new PlayerShopOpen(
                        _connectionManager.Connection.Output.GetMemory(len).Slice(0, len));
                    packet.StoreName = storeName ?? string.Empty;
                    return len;
                });
                _logger.LogInformation("Personal shop open request sent.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error opening personal shop.");
            }
        }

        /// <summary>
        /// Closes the personal shop.
        /// </summary>
        public async Task SendPlayerShopCloseAsync()
        {
            if (!_connectionManager.IsConnected)
            {
                _logger.LogError("Not connected - cannot close shop.");
                return;
            }

            _logger.LogInformation("Closing personal shop.");
            try
            {
                await _connectionManager.Connection.SendAsync(() =>
                {
                    return PacketBuilder.BuildPlayerShopClosePacket(_connectionManager.Connection.Output);
                });
                _logger.LogInformation("Personal shop close request sent.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error closing personal shop.");
            }
        }

        /// <summary>
        /// Sets the price of an item in the personal shop.
        /// </summary>
        public async Task<bool> SendPlayerShopSetItemPriceAsync(byte itemSlot, uint price)
        {
            if (!_connectionManager.IsConnected)
            {
                _logger.LogError("Not connected - cannot set shop item price.");
                return false;
            }

            _logger.LogInformation("Setting shop item price: slot={Slot}, price={Price}", itemSlot, price);
            try
            {
                await _connectionManager.Connection.SendAsync(() =>
                {
                    var len = PlayerShopSetItemPrice.Length;
                    var packet = new PlayerShopSetItemPrice(
                        _connectionManager.Connection.Output.GetMemory(len).Slice(0, len));
                    packet.ItemSlot = itemSlot;
                    packet.Price = price;
                    return len;
                });
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting shop item price.");
                return false;
            }
        }

        public async Task<bool> SendPlayerShopMoveAsync(byte fromSlot, byte toSlot)
        {
            if (!_connectionManager.IsConnected) return false;
            try
            {
                await _connectionManager.Connection.SendAsync(() =>
                    PacketBuilder.BuildPlayerShopMovePacket(_connectionManager.Connection.Output, fromSlot, toSlot));
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error moving player shop item {From} to {To}.", fromSlot, toSlot);
                return false;
            }
        }

        /// <summary>
        /// Notifies the server that another player's shop is no longer being viewed.
        /// </summary>
        public async Task SendPlayerShopCloseOtherAsync(ushort playerId, string playerName)
        {
            if (!_connectionManager.IsConnected) return;
            try
            {
                await _connectionManager.Connection.SendAsync(() =>
                {
                    var packet = new PlayerShopCloseOther(_connectionManager.Connection.Output.GetMemory(PlayerShopCloseOther.Length).Slice(0, PlayerShopCloseOther.Length));
                    packet.PlayerId = playerId;
                    packet.PlayerName = playerName ?? string.Empty;
                    return PlayerShopCloseOther.Length;
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error closing player shop {PlayerId}.", playerId);
            }
        }

        /// <summary>Requests a purchase from another player's personal shop.</summary>
        public async Task<bool> SendPlayerShopItemBuyRequestAsync(ushort playerId, string playerName, byte itemSlot)
        {
            if (!_connectionManager.IsConnected)
            {
                _logger.LogError("Not connected - cannot buy shop item.");
                return false;
            }

            _logger.LogInformation("Buying shop item: player={PlayerId}, slot={Slot}", playerId, itemSlot);
            try
            {
                await _connectionManager.Connection.SendAsync(() =>
                {
                    var len = PlayerShopItemBuyRequest.Length;
                    var packet = new PlayerShopItemBuyRequest(
                        _connectionManager.Connection.Output.GetMemory(len).Slice(0, len));
                    packet.PlayerId = playerId;
                    packet.PlayerName = playerName ?? string.Empty;
                    packet.ItemSlot = itemSlot;
                    return len;
                });
                _logger.LogInformation("Shop item buy request sent.");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error buying shop item from player {PlayerId}.", playerId);
                return false;
            }
        }
    }
}
