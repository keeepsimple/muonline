using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Client.Main.Controls.UI;
using Client.Main.Core.Client;
using Client.Main.Core.Utilities;
using Client.Main.Models;
using Client.Main.Networking.Services;
using Client.Main.Scenes;
using Microsoft.Extensions.Logging;
using MUnique.OpenMU.Network.Packets.ServerToClient;
using QuestStatePacket = MUnique.OpenMU.Network.Packets.ServerToClient.QuestState;

namespace Client.Main.Networking.PacketHandling.Handlers
{
    /// <summary>
    /// Handles the Season 6 quest system (C1/C2 F6). NPC flow (OpenMU): AvailableQuests (0A) → player picks one →
    /// QuestSelectRequest → QuestStepInfo with the starting number (0B) → accept/refuse (QuestProceedRequest) →
    /// QuestStepInfo + QuestProgress (0C). Picking an already active quest answers with QuestProgress, which offers
    /// the completion (QuestCompletionRequest → 0D) when all conditions are met.
    /// Quest text files are not loaded by this client, so quests are shown by group and number.
    /// </summary>
    public class QuestHandler : IGamePacketHandler
    {
        private const int MaxConditions = 5; // fixed slots of QuestProgress / QuestState
        private const int MaxRewards = 5;

        private readonly ILogger<QuestHandler> _logger;
        private readonly CharacterService _characterService;

        public QuestLog Quests { get; } = new();

        public QuestHandler(ILoggerFactory loggerFactory, CharacterService characterService)
        {
            _logger = loggerFactory.CreateLogger<QuestHandler>();
            _characterService = characterService;
        }

        [PacketHandler(0xF6, 0x03)] // QuestEventResponse
        public Task HandleQuestEventResponseAsync(Memory<byte> packet)
        {
            try
            {
                if (packet.Length < QuestEventResponse.Length)
                {
                    _logger.LogWarning("QuestEventResponse too short: {Length}", packet.Length);
                    return Task.CompletedTask;
                }

                QuestEventResponse response = packet; // implicit conversion does not rewrite the header
                var quests = new[] { response[0], response[1] }.Select(q => new QuestLog.QuestId(q.Group, q.Number)).ToList();
                _logger.LogInformation("QuestEventResponse: {Quests}", string.Join(", ", quests));
                MuGame.ScheduleOnMainThread(() =>
                {
                    Quests.EventQuests.Clear();
                    Quests.EventQuests.AddRange(quests);
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing QuestEventResponse packet.");
            }

            return Task.CompletedTask;
        }

        [PacketHandler(0xF6, 0x0A)] // AvailableQuests
        public Task HandleAvailableQuestsAsync(Memory<byte> packet)
        {
            try
            {
                if (packet.Length < AvailableQuests.GetRequiredSize(0))
                {
                    _logger.LogWarning("AvailableQuests too short: {Length}", packet.Length);
                    return Task.CompletedTask;
                }

                AvailableQuests list = packet;
                int count = Math.Min(list.QuestCount, (packet.Length - AvailableQuests.GetRequiredSize(0)) / QuestIdentification.Length);
                var quests = Enumerable.Range(0, count).Select(i => new QuestLog.QuestId(list[i].Group, list[i].Number)).ToList();
                ushort npc = list.QuestNpcNumber;

                MuGame.ScheduleOnMainThread(() =>
                {
                    Quests.AvailableQuestsNpc = npc;
                    Quests.AvailableQuests.Clear();
                    Quests.AvailableQuests.AddRange(quests);
                    Quests.SelectedQuest = null;
                    if (quests.Count == 0)
                    {
                        ShowSystemMessage($"{NpcDatabase.GetNpcName(npc)} has no quests for you right now.");
                        return;
                    }

                    ShowAvailableQuest(0);
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing AvailableQuests packet.");
            }

            return Task.CompletedTask;
        }

        [PacketHandler(0xF6, 0x0B)] // QuestStepInfo
        public Task HandleQuestStepInfoAsync(Memory<byte> packet)
        {
            try
            {
                if (packet.Length < 8)
                {
                    _logger.LogWarning("QuestStepInfo too short: {Length}", packet.Length);
                    return Task.CompletedTask;
                }

                QuestStepInfo info = packet;
                ushort step = info.QuestStepNumber;
                ushort group = info.QuestGroup;

                MuGame.ScheduleOnMainThread(() =>
                {
                    Quests.LastStepByGroup[group] = step;
                    if (Quests.SelectedQuest == new QuestLog.QuestId(group, step))
                    {
                        // Starting step of the picked quest: the player decides whether to take it.
                        Quests.SelectedQuest = null;
                        RequestDialog.Show(
                            $"Quest {group}/{step}\nAccept this quest?",
                            onAccept: () => _ = _characterService.SendQuestProceedRequestAsync(step, group, accept: true),
                            onReject: () => _ = _characterService.SendQuestProceedRequestAsync(step, group, accept: false),
                            acceptText: "Accept",
                            rejectText: "Refuse");
                        return;
                    }

                    ShowSystemMessage($"Quest {group}: step {step}.");
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing QuestStepInfo packet.");
            }

            return Task.CompletedTask;
        }

        [PacketHandler(0xF6, 0x0C)] // QuestProgress (C1) and QuestProgressExtended (C2)
        public Task HandleQuestProgressAsync(Memory<byte> packet)
        {
            try
            {
                var quest = ParseQuest(packet, out ushort group);
                if (group == ushort.MaxValue)
                {
                    _logger.LogWarning("QuestProgress too short: {Length}", packet.Length);
                    return Task.CompletedTask;
                }

                MuGame.ScheduleOnMainThread(() =>
                {
                    bool answersSelection = Quests.SelectedQuest?.Group == group;
                    if (answersSelection) Quests.SelectedQuest = null;
                    if (quest == null)
                    {
                        // The server sends only the group when there is no active quest in it.
                        Quests.ActiveQuests.Remove(group);
                        return;
                    }

                    Quests.ActiveQuests[group] = quest;
                    string text = Describe(quest);
                    ShowSystemMessage(text);

                    if (answersSelection && quest.AllConditionsMet)
                    {
                        RequestDialog.Show(
                            text + "\nComplete this quest?",
                            onAccept: () => _ = _characterService.SendQuestCompletionRequestAsync(quest.Id.Number, quest.Id.Group),
                            acceptText: "Complete",
                            rejectText: "Close");
                    }
                    else if (answersSelection)
                    {
                        RequestDialog.ShowInfo(text);
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing QuestProgress packet.");
            }

            return Task.CompletedTask;
        }

        [PacketHandler(0xF6, 0x0D)] // QuestCompletionResponse
        public Task HandleQuestCompletionResponseAsync(Memory<byte> packet)
        {
            try
            {
                if (packet.Length < 9)
                {
                    _logger.LogWarning("QuestCompletionResponse too short: {Length}", packet.Length);
                    return Task.CompletedTask;
                }

                QuestCompletionResponse response = packet;
                ushort number = response.QuestNumber;
                ushort group = response.QuestGroup;
                byte result = packet.Span[8]; // OpenMU: 0 not completed, 1 completed, 2 zen limit, 3 inventory full (NuGet 0.9.8 only knows a bool)

                MuGame.ScheduleOnMainThread(() =>
                {
                    if (result == 1)
                    {
                        Quests.ActiveQuests.Remove(group);
                        ShowSystemMessage($"Quest {group}/{number} completed.");
                        if (_characterService.IsNpcDialogOpen)
                        {
                            _ = _characterService.SendAvailableQuestsRequestAsync(); // follow-up quests of the NPC
                        }

                        return;
                    }

                    ShowSystemMessage(result switch
                    {
                        2 => $"Quest {group}/{number} can't be completed: the Zen reward would exceed the Zen limit.",
                        3 => $"Quest {group}/{number} can't be completed: the inventory has no room for the rewards.",
                        _ => $"Quest {group}/{number} is not completed yet.",
                    });
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing QuestCompletionResponse packet.");
            }

            return Task.CompletedTask;
        }

        [PacketHandler(0xF6, 0x0F)] // QuestCancelled
        public Task HandleQuestCancelledAsync(Memory<byte> packet)
        {
            try
            {
                if (packet.Length < QuestCancelled.Length)
                {
                    _logger.LogWarning("QuestCancelled too short: {Length}", packet.Length);
                    return Task.CompletedTask;
                }

                ushort group = ((QuestCancelled)packet).QuestGroup; // the number is always 0 here
                MuGame.ScheduleOnMainThread(() =>
                {
                    Quests.ActiveQuests.Remove(group);
                    ShowSystemMessage($"Quest {group} was cancelled.");
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing QuestCancelled packet.");
            }

            return Task.CompletedTask;
        }

        [PacketHandler(0xF6, 0x1B)] // QuestState (C1) and QuestStateExtended (C2)
        public Task HandleQuestStateAsync(Memory<byte> packet)
        {
            try
            {
                var quest = ParseQuest(packet, out ushort group);
                if (group == ushort.MaxValue)
                {
                    _logger.LogWarning("QuestState too short: {Length}", packet.Length);
                    return Task.CompletedTask;
                }

                // Answer of the quest window's QuestStateRequest; kept for a quest window, no message.
                MuGame.ScheduleOnMainThread(() =>
                {
                    if (quest == null)
                        Quests.ActiveQuests.Remove(group);
                    else
                        Quests.ActiveQuests[group] = quest;
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing QuestState packet.");
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Reads QuestProgress/QuestState (same layout, OpenMU writes both with the same code) and their C2 extended
        /// variants (wider condition/reward structs). Returns null when the group has no active quest;
        /// group is ushort.MaxValue when the packet is too short.
        /// </summary>
        private static QuestLog.Quest ParseQuest(Memory<byte> packet, out ushort group)
        {
            group = ushort.MaxValue;
            bool extended = packet.Span[0] is 0xC2 or 0xC4;
            var conditions = new List<QuestLog.Condition>();
            var rewards = new List<QuestLog.Reward>();
            ushort number;
            if (extended)
            {
                if (packet.Length < QuestStateExtended.Length) return null;
                QuestStateExtended p = packet;
                group = p.QuestGroup;
                number = p.QuestNumber;
                for (int i = 0; i < Math.Min((int)p.ConditionCount, MaxConditions); i++)
                {
                    var c = p.GetQuestConditionExtended(i);
                    conditions.Add(new(c.Type, c.RequirementId, c.RequiredCount, c.CurrentCount));
                }

                for (int i = 0; i < Math.Min((int)p.RewardCount, MaxRewards); i++)
                {
                    var r = p.GetQuestRewardExtended(i);
                    rewards.Add(new(r.Type, r.RewardId, r.RewardCount));
                }
            }
            else
            {
                if (packet.Length < QuestStatePacket.Length) return null;
                QuestStatePacket p = packet;
                group = p.QuestGroup;
                number = p.QuestNumber;
                for (int i = 0; i < Math.Min((int)p.ConditionCount, MaxConditions); i++)
                {
                    var c = p.GetQuestCondition(i);
                    conditions.Add(new(c.Type, c.RequirementId, c.RequiredCount, c.CurrentCount));
                }

                for (int i = 0; i < Math.Min((int)p.RewardCount, MaxRewards); i++)
                {
                    var r = p.GetQuestReward(i);
                    rewards.Add(new(r.Type, r.RewardId, r.RewardCount));
                }
            }

            if (number == 0 && conditions.Count == 0 && rewards.Count == 0)
                return null;

            return new QuestLog.Quest(new QuestLog.QuestId(group, number), conditions, rewards);
        }

        private void ShowAvailableQuest(int index)
        {
            var quests = Quests.AvailableQuests;
            if (index >= quests.Count) return;

            var quest = quests[index];
            bool last = index == quests.Count - 1;
            RequestDialog.Show(
                $"{NpcDatabase.GetNpcName(Quests.AvailableQuestsNpc)} - quest {index + 1} of {quests.Count}\nQuest {quest.Group}/{quest.Number}",
                onAccept: () => SelectQuest(quest),
                onReject: last ? null : () => MuGame.ScheduleOnMainThread(() => ShowAvailableQuest(index + 1)),
                acceptText: "Select",
                rejectText: last ? "Close" : "Next");
        }

        /// <summary>The player picked a quest of the NPC list (Number is its starting number).</summary>
        public void SelectQuest(QuestLog.QuestId quest)
        {
            Quests.SelectedQuest = quest;
            _ = _characterService.SendQuestSelectRequestAsync(quest.Number, quest.Group);
        }

        private static string Describe(QuestLog.Quest quest)
        {
            var parts = quest.Conditions.Select(c => c.Type switch
            {
                ConditionType.MonsterKills => $"kill {NpcDatabase.GetNpcName(c.RequirementId)} {c.CurrentCount}/{c.RequiredCount}",
                ConditionType.Item => $"bring {ItemName(c.RequirementId)} {c.CurrentCount}/{c.RequiredCount}",
                _ => $"{c.Type} {c.CurrentCount}/{c.RequiredCount}",
            }).ToList();
            string text = $"Quest {quest.Id.Group}/{quest.Id.Number}: " + (parts.Count > 0 ? string.Join(", ", parts) : "no conditions");
            if (quest.Rewards.Count > 0)
            {
                text += ". Reward: " + string.Join(", ", quest.Rewards.Select(r => r.Type switch
                {
                    RewardType.Experience => $"{r.Count:N0} Exp",
                    RewardType.Money => $"{r.Count:N0} Zen",
                    RewardType.Item => ItemName(r.RewardId),
                    RewardType.GensContribution => $"{r.Count} gens contribution",
                    _ => $"{r.Type} {r.Count}",
                }));
            }

            return text;
        }

        /// <summary>OpenMU item type: group &lt;&lt; 9 | number.</summary>
        private static string ItemName(ushort itemType)
        {
            byte group = (byte)(itemType >> 9);
            short number = (short)(itemType & 0x1FF);
            try
            {
                return ItemDatabase.GetItemName(group, number) ?? $"item {group}/{number}";
            }
            catch (Exception)
            {
                return $"item {group}/{number}";
            }
        }

        private static void ShowSystemMessage(string text)
            => (MuGame.Instance?.ActiveScene as GameScene)?.ChatLog?.AddMessage("System", text, MessageType.System);
    }
}
