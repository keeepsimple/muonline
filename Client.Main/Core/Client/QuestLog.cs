#nullable enable
using System.Collections.Generic;
using System.Linq;
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
    }
}
