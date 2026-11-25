using Client.Data.BMD;
using Client.Main.Content;
using Client.Main.Core.Utilities;
using Client.Main.Models;
using Client.Main.Objects.Player;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    /// <summary>
    /// Chaos Castle monsters are armed characters, not monster models: Player.bmd with an armour set and
    /// weapons (same composition as <see cref="CursedWizard"/>). Each castle has one gear tier; its first
    /// monster id carries sword and shield, the second a spear.
    /// </summary>
    public abstract class ChaosCastleMonster : MonsterObject
    {
        // Per castle 1..7: armour set index (groups 7-11), item level, sword (group 0), shield (group 6), spear (group 3).
        // Sets: Bronze, Scale, Plate, Dragon, Black Dragon, Dark Phoenix, Great Dragon; weapons rise with the tier.
        private static readonly (short Set, byte Level, short Sword, short Shield, short Spear)[] Tiers =
        {
            (0, 0, 1, 0, 1),
            (6, 3, 5, 4, 2),
            (9, 5, 7, 2, 5),
            (1, 7, 8, 9, 6),
            (16, 9, 10, 11, 7),
            (17, 11, 13, 13, 9),
            (21, 13, 17, 14, 10)
        };

        private static readonly Lazy<Task<BMD>> SwordModel = new(() => BuildModelAsync(spear: false));
        private static readonly Lazy<Task<BMD>> SpearModel = new(() => BuildModelAsync(spear: true));

        private readonly int _tier;
        private readonly bool _spear;
        private readonly ModelObject[] _parts;
        private readonly WeaponObject _rightHand;
        private readonly WeaponObject _leftHand;

        protected ChaosCastleMonster(int castle, bool spear)
        {
            _tier = castle - 1;
            _spear = spear;
            RenderShadow = true;
            MoveSpeed = 250f;
            AnimationSpeed = 25f; // Player.bmd animations

            byte level = Tiers[_tier].Level;
            _parts = new ModelObject[]
            {
                new PlayerHelmObject { LinkParentAnimation = true, ItemLevel = level },
                new PlayerArmorObject { LinkParentAnimation = true, ItemLevel = level },
                new PlayerPantObject { LinkParentAnimation = true, ItemLevel = level },
                new PlayerGloveObject { LinkParentAnimation = true, ItemLevel = level },
                new PlayerBootObject { LinkParentAnimation = true, ItemLevel = level }
            };
            _rightHand = new WeaponObject { LinkParentAnimation = false, ParentBoneLink = 33, ItemLevel = level };
            _leftHand = new WeaponObject { LinkParentAnimation = false, ParentBoneLink = 42, ItemLevel = level };

            foreach (var part in _parts)
                Children.Add(part);
            Children.Add(_rightHand);
            Children.Add(_leftHand);
        }

        public override async Task Load()
        {
            var tier = Tiers[_tier];
            Model = await (_spear ? SpearModel : SwordModel).Value;
            for (int i = 0; i < _parts.Length; i++)
                await LoadItemAsync(_parts[i], (byte)(7 + i), tier.Set, bodyPart: true);
            if (_spear)
            {
                await LoadItemAsync(_rightHand, 3, tier.Spear, bodyPart: false);
            }
            else
            {
                await LoadItemAsync(_rightHand, 0, tier.Sword, bodyPart: false);
                await LoadItemAsync(_leftHand, 6, tier.Shield, bodyPart: false);
            }

            await base.Load();
        }

        private static async Task LoadItemAsync(ModelObject target, byte group, short id, bool bodyPart)
        {
            target.MaterialItemGroup = group;
            target.MaterialItemIndex = id;
            var item = ItemDatabase.GetItemDefinition(group, id);
            if (item == null || string.IsNullOrEmpty(item.TexturePath))
                return;

            string path = bodyPart ? item.TexturePath.Replace("Item/", "Player/") : item.TexturePath;
            target.Model = await BMDLoader.Instance.Prepare(path);
        }

        private static async Task<BMD> BuildModelAsync(bool spear)
        {
            var playerModel = await BMDLoader.Instance.Prepare("Player/Player.bmd");
            if (playerModel == null)
                return null;

            PlayerAction stop = spear ? PlayerAction.PlayerStopSpear : PlayerAction.PlayerStopSword;
            PlayerAction walk = spear ? PlayerAction.PlayerWalkSpear : PlayerAction.PlayerWalkSword;
            PlayerAction run = spear ? PlayerAction.PlayerRunSpear : PlayerAction.PlayerRunSword;
            PlayerAction attack1 = spear ? PlayerAction.PlayerAttackSpear1 : PlayerAction.PlayerAttackSwordRight1;
            PlayerAction attack2 = spear ? PlayerAction.PlayerAttackSpear1 : PlayerAction.PlayerAttackSwordRight2;

            int count = Enum.GetValues(typeof(MonsterActionType)).Length;
            var map = new Dictionary<int, int>
            {
                [(int)MonsterActionType.Stop1] = (int)stop,
                [(int)MonsterActionType.Stop2] = (int)stop,
                [(int)MonsterActionType.Walk] = (int)walk,
                [(int)MonsterActionType.Attack1] = (int)attack1,
                [(int)MonsterActionType.Attack2] = (int)attack2,
                [(int)MonsterActionType.Shock] = (int)PlayerAction.PlayerShock,
                [(int)MonsterActionType.Die] = (int)PlayerAction.PlayerDie1,
                [(int)MonsterActionType.Appear] = (int)PlayerAction.PlayerComeUp,
                [(int)MonsterActionType.Attack3] = (int)attack1,
                [(int)MonsterActionType.Attack4] = (int)attack2,
                [(int)MonsterActionType.Run] = (int)run
            };
            var model = new BMD
            {
                Version = playerModel.Version,
                Name = playerModel.Name,
                Meshes = playerModel.Meshes,
                Actions = BuildActionArray(playerModel, count, map),
                Bones = BuildBoneArray(playerModel, count, map)
            };
            BMDLoader.Instance.RegisterDerivedModel(playerModel, model);
            return model;
        }
    }

    // Ids per OpenMU ChaosCastle1-7 map definitions.
    [NpcInfo(162, "Chaos Castle 1")] internal class ChaosCastleMonster162 : ChaosCastleMonster { public ChaosCastleMonster162() : base(1, false) { } }
    [NpcInfo(163, "Chaos Castle 2")] internal class ChaosCastleMonster163 : ChaosCastleMonster { public ChaosCastleMonster163() : base(1, true) { } }
    [NpcInfo(164, "Chaos Castle 3")] internal class ChaosCastleMonster164 : ChaosCastleMonster { public ChaosCastleMonster164() : base(2, false) { } }
    [NpcInfo(165, "Chaos Castle 4")] internal class ChaosCastleMonster165 : ChaosCastleMonster { public ChaosCastleMonster165() : base(2, true) { } }
    [NpcInfo(166, "Chaos Castle 5")] internal class ChaosCastleMonster166 : ChaosCastleMonster { public ChaosCastleMonster166() : base(3, false) { } }
    [NpcInfo(167, "Chaos Castle 6")] internal class ChaosCastleMonster167 : ChaosCastleMonster { public ChaosCastleMonster167() : base(3, true) { } }
    [NpcInfo(168, "Chaos Castle 7")] internal class ChaosCastleMonster168 : ChaosCastleMonster { public ChaosCastleMonster168() : base(4, false) { } }
    [NpcInfo(169, "Chaos Castle 8")] internal class ChaosCastleMonster169 : ChaosCastleMonster { public ChaosCastleMonster169() : base(4, true) { } }
    [NpcInfo(170, "Chaos Castle 9")] internal class ChaosCastleMonster170 : ChaosCastleMonster { public ChaosCastleMonster170() : base(5, false) { } }
    [NpcInfo(171, "Chaos Castle 10")] internal class ChaosCastleMonster171 : ChaosCastleMonster { public ChaosCastleMonster171() : base(5, true) { } }
    [NpcInfo(172, "Chaos Castle 11")] internal class ChaosCastleMonster172 : ChaosCastleMonster { public ChaosCastleMonster172() : base(6, false) { } }
    [NpcInfo(173, "Chaos Castle 12")] internal class ChaosCastleMonster173 : ChaosCastleMonster { public ChaosCastleMonster173() : base(6, true) { } }
    [NpcInfo(426, "Chaos Castle 13")] internal class ChaosCastleMonster426 : ChaosCastleMonster { public ChaosCastleMonster426() : base(7, false) { } }
    [NpcInfo(427, "Chaos Castle 14")] internal class ChaosCastleMonster427 : ChaosCastleMonster { public ChaosCastleMonster427() : base(7, true) { } }
}
