using System;
using System.Text;

namespace Client.Main.Core.Client
{
    /// <summary>
    /// The 257-byte Season 6 MU Helper configuration blob (PRECEIVE_MUHELPER_DATA), stored as-is by OpenMU and sent
    /// back with MuHelperConfigurationData (0xAE) on enter-game. Layout as read by OpenMU's MuHelperSettingsSerializer:
    /// <code>
    ///  0      DataStartMarker (kept)
    ///  1      [unused:3][Jewel:1][SetItem:1][Excellent:1][Zen:1][ExtraItem:1]
    ///  2      [HuntingRange:4][ObtainRange:4]
    ///  3-4    DistanceMin (max seconds away)       5-6   BasicSkill
    ///  7-8    ActivationSkill1   9-10 DelayMinSkill1   11-12 ActivationSkill2   13-14 DelayMinSkill2
    ///  15-16  CastingBuffMin (buff interval s)     17-22 BuffSkill0..2
    ///  23     [HPAutoPotion:4][HPAutoHeal:4]       24    [HPParty:4][HPDrainLife:4]   (nibble * 10 = %)
    ///  25     [AutoPotion][AutoHeal][DrainLife][LongDistanceAttack][OriginalPosition][Combo][Party][PartyHeal]
    ///  26     [BuffPartyDuration][DarkSpirits][BuffDuration][S1Delay][S1Con][S1PreCon][S1SubCon:2]
    ///  27     [S2Delay][S2Con][S2PreCon][S2SubCon:2][RepairItem][PickAllNear][PickSelected]
    ///  28     PetAttack (0 cease, 1 auto, 2 together)
    ///  29     [SelfDefense][AutoAcceptFriend][AutoAcceptGuild][FallbackBasicAttack][unused:4]
    ///  65-244 ExtraItems[12][15] (null-terminated names)
    /// </code>
    /// Bytes and bits not listed are carried over unchanged, so a received blob is written back exactly.
    /// </summary>
    public sealed class MuHelperConfig
    {
        public const int Length = 257;
        public const int ExtraItemCount = 12;
        public const int ExtraItemLength = 15;
        /// <summary>Longest name the window accepts; one byte stays free for the terminator.</summary>
        public const int MaxExtraItemNameLength = ExtraItemLength - 1;
        private const int ExtraItemsOffset = 65;

        private readonly byte[] _raw = new byte[Length];

        public bool PickJewel, PickAncient, PickExcellent, PickZen, PickExtraItems;
        public int HuntingRange = 6, ObtainRange = 3;
        public int MaxSecondsAway = 10;
        public int BasicSkillId, ActivationSkill1Id, DelaySkill1, ActivationSkill2Id, DelaySkill2;
        public int BuffIntervalSeconds;
        public readonly int[] BuffSkillIds = new int[3];
        /// <summary>HP thresholds in percent, multiples of 10 (stored as a nibble).</summary>
        public int PotionThreshold = 50, HealThreshold = 50, HealPartyThreshold = 50, DrainLifeThreshold = 50;
        public bool UseHealPotion, AutoHeal, UseDrainLife, LongRangeCounterAttack, ReturnToOriginalPosition, UseCombo, SupportParty, AutoHealParty;
        public bool BuffDurationForParty, UseDarkRaven, BuffOnDuration;
        public bool Skill1UseTimer, Skill1UseCondition, Skill1ConditionAttacking;
        public int Skill1SubCondition;
        public bool Skill2UseTimer, Skill2UseCondition, Skill2ConditionAttacking;
        public int Skill2SubCondition;
        public bool RepairItem, PickAllItems, PickSelectItems;
        public int DarkRavenMode;
        public bool UseSelfDefense, AutoAcceptFriend, AutoAcceptGuild, FallbackBasicAttack;
        public readonly string[] ExtraItemNames = new string[ExtraItemCount];

        public MuHelperConfig()
        {
            Array.Fill(ExtraItemNames, string.Empty);
        }

        public static MuHelperConfig Parse(ReadOnlySpan<byte> data)
        {
            var c = new MuHelperConfig();
            data[..Math.Min(data.Length, Length)].CopyTo(c._raw);
            ReadOnlySpan<byte> d = c._raw;

            c.PickJewel = Bit(d[1], 3); c.PickAncient = Bit(d[1], 4); c.PickExcellent = Bit(d[1], 5);
            c.PickZen = Bit(d[1], 6); c.PickExtraItems = Bit(d[1], 7);
            c.HuntingRange = d[2] & 0x0F;
            c.ObtainRange = d[2] >> 4;
            c.MaxSecondsAway = Word(d, 3);
            c.BasicSkillId = Word(d, 5);
            c.ActivationSkill1Id = Word(d, 7);
            c.DelaySkill1 = Word(d, 9);
            c.ActivationSkill2Id = Word(d, 11);
            c.DelaySkill2 = Word(d, 13);
            c.BuffIntervalSeconds = Word(d, 15);
            for (int i = 0; i < c.BuffSkillIds.Length; i++)
                c.BuffSkillIds[i] = Word(d, 17 + i * 2);
            c.PotionThreshold = (d[23] & 0x0F) * 10;
            c.HealThreshold = (d[23] >> 4) * 10;
            c.HealPartyThreshold = (d[24] & 0x0F) * 10;
            c.DrainLifeThreshold = (d[24] >> 4) * 10;

            c.UseHealPotion = Bit(d[25], 0); c.AutoHeal = Bit(d[25], 1); c.UseDrainLife = Bit(d[25], 2);
            c.LongRangeCounterAttack = Bit(d[25], 3); c.ReturnToOriginalPosition = Bit(d[25], 4);
            c.UseCombo = Bit(d[25], 5); c.SupportParty = Bit(d[25], 6); c.AutoHealParty = Bit(d[25], 7);

            c.BuffDurationForParty = Bit(d[26], 0); c.UseDarkRaven = Bit(d[26], 1); c.BuffOnDuration = Bit(d[26], 2);
            c.Skill1UseTimer = Bit(d[26], 3); c.Skill1UseCondition = Bit(d[26], 4); c.Skill1ConditionAttacking = Bit(d[26], 5);
            c.Skill1SubCondition = d[26] >> 6;

            c.Skill2UseTimer = Bit(d[27], 0); c.Skill2UseCondition = Bit(d[27], 1); c.Skill2ConditionAttacking = Bit(d[27], 2);
            c.Skill2SubCondition = (d[27] >> 3) & 0x03;
            c.RepairItem = Bit(d[27], 5); c.PickAllItems = Bit(d[27], 6); c.PickSelectItems = Bit(d[27], 7);

            c.DarkRavenMode = d[28];
            c.UseSelfDefense = Bit(d[29], 0); c.AutoAcceptFriend = Bit(d[29], 1);
            c.AutoAcceptGuild = Bit(d[29], 2); c.FallbackBasicAttack = Bit(d[29], 3);

            for (int i = 0; i < ExtraItemCount; i++)
            {
                var slot = d.Slice(ExtraItemsOffset + i * ExtraItemLength, ExtraItemLength);
                int end = slot.IndexOf((byte)0);
                c.ExtraItemNames[i] = Encoding.Latin1.GetString(end < 0 ? slot : slot[..end]);
            }

            return c;
        }

        public byte[] ToBytes()
        {
            var d = (byte[])_raw.Clone();

            SetBit(d, 1, 3, PickJewel); SetBit(d, 1, 4, PickAncient); SetBit(d, 1, 5, PickExcellent);
            SetBit(d, 1, 6, PickZen); SetBit(d, 1, 7, PickExtraItems);
            d[2] = (byte)(Nibble(HuntingRange) | Nibble(ObtainRange) << 4);
            SetWord(d, 3, MaxSecondsAway);
            SetWord(d, 5, BasicSkillId);
            SetWord(d, 7, ActivationSkill1Id);
            SetWord(d, 9, DelaySkill1);
            SetWord(d, 11, ActivationSkill2Id);
            SetWord(d, 13, DelaySkill2);
            SetWord(d, 15, BuffIntervalSeconds);
            for (int i = 0; i < BuffSkillIds.Length; i++)
                SetWord(d, 17 + i * 2, BuffSkillIds[i]);
            d[23] = (byte)(Nibble(PotionThreshold / 10) | Nibble(HealThreshold / 10) << 4);
            d[24] = (byte)(Nibble(HealPartyThreshold / 10) | Nibble(DrainLifeThreshold / 10) << 4);

            SetBit(d, 25, 0, UseHealPotion); SetBit(d, 25, 1, AutoHeal); SetBit(d, 25, 2, UseDrainLife);
            SetBit(d, 25, 3, LongRangeCounterAttack); SetBit(d, 25, 4, ReturnToOriginalPosition);
            SetBit(d, 25, 5, UseCombo); SetBit(d, 25, 6, SupportParty); SetBit(d, 25, 7, AutoHealParty);

            SetBit(d, 26, 0, BuffDurationForParty); SetBit(d, 26, 1, UseDarkRaven); SetBit(d, 26, 2, BuffOnDuration);
            SetBit(d, 26, 3, Skill1UseTimer); SetBit(d, 26, 4, Skill1UseCondition); SetBit(d, 26, 5, Skill1ConditionAttacking);
            d[26] = (byte)(d[26] & 0x3F | (Skill1SubCondition & 0x03) << 6);

            SetBit(d, 27, 0, Skill2UseTimer); SetBit(d, 27, 1, Skill2UseCondition); SetBit(d, 27, 2, Skill2ConditionAttacking);
            d[27] = (byte)(d[27] & ~0x18 | (Skill2SubCondition & 0x03) << 3);
            SetBit(d, 27, 5, RepairItem); SetBit(d, 27, 6, PickAllItems); SetBit(d, 27, 7, PickSelectItems);

            d[28] = (byte)DarkRavenMode;
            SetBit(d, 29, 0, UseSelfDefense); SetBit(d, 29, 1, AutoAcceptFriend);
            SetBit(d, 29, 2, AutoAcceptGuild); SetBit(d, 29, 3, FallbackBasicAttack);

            for (int i = 0; i < ExtraItemCount; i++)
            {
                var slot = d.AsSpan(ExtraItemsOffset + i * ExtraItemLength, ExtraItemLength);
                slot.Clear();
                string name = ExtraItemNames[i] ?? string.Empty;
                Encoding.Latin1.GetBytes(name.AsSpan(0, Math.Min(name.Length, ExtraItemLength)), slot);
            }

            return d;
        }

        public MuHelperConfig Clone() => Parse(ToBytes());

        private static bool Bit(byte value, int bit) => (value & (1 << bit)) != 0;
        private static int Word(ReadOnlySpan<byte> d, int offset) => d[offset] | d[offset + 1] << 8;
        private static int Nibble(int value) => Math.Clamp(value, 0, 15);

        private static void SetWord(byte[] d, int offset, int value)
        {
            value = Math.Clamp(value, 0, ushort.MaxValue);
            d[offset] = (byte)value;
            d[offset + 1] = (byte)(value >> 8);
        }

        private static void SetBit(byte[] d, int index, int bit, bool on)
        {
            if (on) d[index] |= (byte)(1 << bit);
            else d[index] &= (byte)~(1 << bit);
        }
    }
}
