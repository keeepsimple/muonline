#nullable enable
using System.Collections.Generic;
using System.Linq;
using Client.Main.Core.Utilities;
using MUnique.OpenMU.Network.Packets.ServerToClient;

namespace Client.Main.Core.Client
{
    /// <summary>
    /// Client state of the Season 6 quest system (C1 F6), filled by QuestHandler on the main thread.
    /// The client does not load the quest text files, so quests are known by group and number only.
    /// </summary>
    public sealed class QuestLog
    {
        public readonly record struct QuestId(ushort Group, ushort Number);

        /// <summary>RequirementId: monster number for kills, item type (group &lt;&lt; 9 | number) for items.</summary>
        public readonly record struct Condition(ConditionType Type, ushort RequirementId, uint RequiredCount, uint CurrentCount)
        {
            public bool IsMet => CurrentCount >= RequiredCount;
        }

        /// <summary>RewardId: item type (group &lt;&lt; 9 | number) for item rewards.</summary>
        public readonly record struct Reward(RewardType Type, ushort RewardId, uint Count);

        public sealed record Quest(QuestId Id, IReadOnlyList<Condition> Conditions, IReadOnlyList<Reward> Rewards)
        {
            public bool AllConditionsMet => Conditions.All(c => c.IsMet);
        }

        /// <summary>Active quest per quest group (the server allows one active quest per group).</summary>
        public Dictionary<ushort, Quest> ActiveQuests { get; } = new();

        /// <summary>NPC number of the last AvailableQuests list.</summary>
        public ushort AvailableQuestsNpc { get; set; }

        /// <summary>Quests offered by that NPC; Number is the quest's starting (text) number.</summary>
        public List<QuestId> AvailableQuests { get; } = new();

        /// <summary>Quest picked from the NPC list whose QuestStepInfo / QuestProgress answer is still awaited.</summary>
        public QuestId? SelectedQuest { get; set; }

        /// <summary>Last QuestStepInfo text number per group.</summary>
        public Dictionary<ushort, ushort> LastStepByGroup { get; } = new();

        /// <summary>Quests of QuestEventResponse (F6 03); the original client's use of them is unknown.</summary>
        public List<QuestId> EventQuests { get; } = new();

        /// <summary>Clears all state; the server resends it after entering the game (character switch, reconnect).</summary>
        public void Reset()
        {
            ActiveQuests.Clear();
            AvailableQuestsNpc = 0;
            AvailableQuests.Clear();
            SelectedQuest = null;
            LastStepByGroup.Clear();
            EventQuests.Clear();
        }

        public static string ConditionText(Condition c) => c.Type switch
        {
            ConditionType.MonsterKills => $"Kill {NpcDatabase.GetNpcName(c.RequirementId)}",
            ConditionType.Item => $"Bring {ItemName(c.RequirementId)}",
            _ => c.Type.ToString(),
        };

        public static string RewardText(Reward r) => r.Type switch
        {
            RewardType.Experience => $"{r.Count:N0} Exp",
            RewardType.Money => $"{r.Count:N0} Zen",
            RewardType.Item => r.Count > 1 ? $"{ItemName(r.RewardId)} x{r.Count}" : ItemName(r.RewardId),
            RewardType.GensContribution => $"{r.Count} gens contribution",
            _ => $"{r.Type} {r.Count}",
        };

        /// <summary>OpenMU item type: group &lt;&lt; 9 | number.</summary>
        public static string ItemName(ushort itemType)
        {
            byte group = (byte)(itemType >> 9);
            short number = (short)(itemType & 0x1FF);
            try
            {
                return ItemDatabase.GetItemName(group, number) ?? $"item {group}/{number}";
            }
            catch (System.Exception)
            {
                return $"item {group}/{number}";
            }
        }
    }
}
