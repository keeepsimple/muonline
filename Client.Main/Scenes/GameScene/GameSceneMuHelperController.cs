using System;
using System.Collections.Generic;
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Controls.UI;
using Client.Main.Controls.UI.Game.MuHelper;
using Client.Main.Core.Client;
using Client.Main.Core.Models;
using Client.Main.Core.Utilities;
using Client.Main.Models;
using Client.Main.Objects;
using Client.Main.Objects.Player;
using Microsoft.Xna.Framework;

namespace Client.Main.Scenes
{
    /// <summary>
    /// Client-side MU Helper (in S6 the helper plays on the client, the server only charges zen). Each tick follows
    /// CMuHelper::Work as mirrored by OpenMU's offline helper: buff, recover, pick up, regroup, attack. The loop runs
    /// while the server reports the helper as started and stops on death, map change or disconnect.
    /// </summary>
    internal sealed class GameSceneMuHelperController
    {
        private const double TickMs = 150;
        private const double BuffConfirmMs = 3000;
        private const double IgnoreMs = 10000; // unreachable drops / targets and drops already requested
        private const float PickupDistance = 2f; // OpenMU accepts pick-ups up to 3 tiles away
        private const float AtOrigin = 1.5f;
        private const ushort HealSkillId = 26;
        private static readonly ushort[] DrainLifeSkillIds = { 214, 458 };
        private static readonly short[] HealingPotionIds = { 3, 2, 1, 0 }; // large, medium, small healing potion, apple
        private static readonly ScopeObjectType[] DropTypes = { ScopeObjectType.Item, ScopeObjectType.Money };

        // Buff skill -> magic effect, so "recast when expired" can see the effect is gone.
        private static readonly Dictionary<int, byte> BuffEffects = new()
        {
            [16] = 4, [27] = 2, [28] = 1, [48] = 8, [64] = 5, [77] = 6, [218] = 81, [233] = 82,
        };

        private readonly GameScene _scene;
        private readonly LabelControl _hud;
        private readonly Dictionary<ushort, double> _ignoredIds = new();
        private readonly MuHelperApproach _dropApproach = new();
        private readonly MuHelperApproach _targetApproach = new();
        private readonly Dictionary<(ushort Target, int Skill), double> _buffCasts = new();
        private readonly double[] _slotLastUse = new double[2];
        private bool _active;
        private WorldControl _world;
        private Vector2 _origin;
        private double _nextTickMs, _nextPotionMs, _awaySinceMs;
        private ushort _targetId;
        private int _comboStep;

        public GameSceneMuHelperController(GameScene scene)
        {
            _scene = scene;
            Window = new MuHelperWindow();
            _hud = new LabelControl
            {
                Align = ControlAlign.Top | ControlAlign.HorizontalCenter,
                Margin = new Margin { Top = 70 },
                FontSize = 12,
                TextColor = new Color(255, 210, 90),
                Visible = false
            };
            scene.Controls.Add(Window);
            scene.Controls.Add(_hud);

            // A helper started in a previous scene (relog, other character) must not carry over.
            if (MuHelperState.IsRunning)
            {
                MuHelperState.MarkStopped();
                _ = MuGame.Network?.GetCharacterService()?.SendMuHelperStatusChangeAsync(true);
            }
        }

        public MuHelperWindow Window { get; }

        private GameSceneSkillController Skills => _scene.SkillController;

        public void Update(GameTime gameTime)
        {
            var network = MuGame.Network;
            if (!MuHelperState.IsRunning)
            {
                if (_active)
                    Finish("MU Helper stopped.");
                return;
            }

            if (network == null || !network.IsConnected)
            {
                MuHelperState.MarkStopped();
                Finish(null);
                return;
            }

            var state = network.GetCharacterState();
            var hero = _scene.Hero;
            if (state == null || hero == null || _scene.World is not WalkableWorldControl world)
                return;

            double now = gameTime.TotalGameTime.TotalMilliseconds;
            if (!_active)
                Begin(hero, world);
            if (hero.IsDead) { Stop("MU Helper stopped: character died."); return; }
            if (!ReferenceEquals(world, _world)) { Stop("MU Helper stopped: map changed."); return; }
            if (now < _nextTickMs) return;
            _nextTickMs = now + TickMs;

            UpdateHud();
            var config = MuHelperState.GetConfig(state.Name);
            int range = Math.Max(1, config.HuntingRange);

            DrinkPotion(config, state, now);
            if (hero.IsAttackOrSkillAnimationPlaying())
                return;
            if (CastBuffs(config, hero, world, state, range, now) || Recover(config, hero, world, state, range, now) || PickUp(config, hero, now))
                return;
            if (hero.IsMoving || hero.MovementIntent)
                return;
            if (Regroup(config, hero, range, now))
                return;
            Attack(config, hero, world, state, range, now);
        }

        private void Begin(HeroObject hero, WorldControl world)
        {
            _active = true;
            _world = world;
            _origin = hero.Location;
            _targetId = 0;
            _comboStep = 0;
            _awaySinceMs = -1;
            _nextTickMs = _nextPotionMs = 0;
            Array.Fill(_slotLastUse, double.MinValue);
            _ignoredIds.Clear();
            _dropApproach.Reset();
            _targetApproach.Reset();
            _buffCasts.Clear();
            _hud.Visible = true;
            Chat("MU Helper started.");
        }

        private void Stop(string message)
        {
            MuHelperState.MarkStopped();
            _ = MuGame.Network?.GetCharacterService()?.SendMuHelperStatusChangeAsync(true);
            Finish(message);
        }

        private void Finish(string message)
        {
            _active = false;
            _hud.Visible = false;
            if (message != null)
                Chat(message);
        }

        private void Chat(string message) => _scene.ChatLog?.AddMessage("System", message, MessageType.System);

        private void UpdateHud()
        {
            var elapsed = DateTime.UtcNow - MuHelperState.StartedAtUtc;
            string text = $"MU HELPER  {(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}  -  {MuHelperState.TotalZenCost:N0} zen";
            if (_hud.Text != text)
                _hud.Text = text;
        }

        // ─────────────────────────────── Recovery ───────────────────────────────

        private static int HpPercent(CharacterState state)
            => state.MaximumHealth == 0 ? 100 : (int)(state.CurrentHealth * 100L / state.MaximumHealth);

        private void DrinkPotion(MuHelperConfig c, CharacterState state, double now)
        {
            if (!c.UseHealPotion || now < _nextPotionMs || HpPercent(state) > c.PotionThreshold)
                return;

            var items = state.GetInventoryItems();
            foreach (short id in HealingPotionIds)
            {
                foreach (var (slot, data) in items)
                {
                    // 12..203: inventory and its extensions; the own shop starts at 204.
                    if (slot < 12 || slot >= 204 || ItemDatabase.GetItemDefinition(data) is not { } definition ||
                        definition.Group != 14 || definition.Id != id)
                        continue;

                    _nextPotionMs = now + 1000;
                    SoundController.Instance.PlayBuffer(id == 0 ? "Sound/pEatApple.wav" : "Sound/pDrink.wav");
                    _ = MuGame.Network.GetCharacterService().SendConsumeItemRequestAsync(slot);
                    return;
                }
            }
        }

        private bool Recover(MuHelperConfig c, HeroObject hero, WalkableWorldControl world, CharacterState state, int range, double now)
        {
            var heal = FindSkill(state, HealSkillId);
            if (heal != null && c.AutoHeal && HpPercent(state) <= c.HealThreshold && Skills.CastSkillFromHotbar(heal, null))
                return true;

            if (heal != null && c.AutoHealParty)
            {
                foreach (var (member, hp) in PartyMembersInRange(world, hero, range))
                    if (hp * 100 <= c.HealPartyThreshold && Skills.CastSkillOnPlayer(heal, member))
                        return true;
            }

            if (c.UseDrainLife && HpPercent(state) <= c.DrainLifeThreshold && FindTarget(world, hero, range, now) is { } target)
            {
                foreach (ushort id in DrainLifeSkillIds)
                    if (FindSkill(state, id) is { } drain && InSkillRange(hero, target, id) && Skills.CastSkillFromHotbar(drain, target))
                        return true;
            }

            return false;
        }

        // ─────────────────────────────── Buffs ───────────────────────────────

        private bool CastBuffs(MuHelperConfig c, HeroObject hero, WalkableWorldControl world, CharacterState state, int range, double now)
        {
            foreach (int skillId in c.BuffSkillIds)
            {
                if (FindSkill(state, skillId) is not { } skill)
                    continue;

                if (BuffDue(c, state, hero.NetworkId, skillId, useDuration: true, now) && Skills.CastSkillFromHotbar(skill, null))
                {
                    _buffCasts[(hero.NetworkId, skillId)] = now;
                    return true;
                }

                if (!c.SupportParty)
                    continue;

                foreach (var (member, _) in PartyMembersInRange(world, hero, range))
                {
                    if (BuffDue(c, state, member.NetworkId, skillId, c.BuffDurationForParty, now) && Skills.CastSkillOnPlayer(skill, member))
                    {
                        _buffCasts[(member.NetworkId, skillId)] = now;
                        return true;
                    }
                }
            }

            return false;
        }

        private bool BuffDue(MuHelperConfig c, CharacterState state, ushort targetId, int skillId, bool useDuration, double now)
        {
            if (!_buffCasts.TryGetValue((targetId, skillId), out double last))
                return true;

            double elapsed = now - last;
            if (c.BuffIntervalSeconds > 0 && elapsed >= c.BuffIntervalSeconds * 1000.0)
                return true;
            if (!c.BuffOnDuration || !useDuration || elapsed < BuffConfirmMs)
                return false;

            // ponytail: buffs without a known effect are recast every minute; add the mapping above to track them exactly.
            return BuffEffects.TryGetValue(skillId, out byte effect) ? !state.HasActiveBuff(effect, targetId) : elapsed >= 60000;
        }

        private static IEnumerable<(PlayerObject Member, float Health)> PartyMembersInRange(WalkableWorldControl world, HeroObject hero, int range)
        {
            var party = MuGame.Network?.GetPartyManager();
            if (party == null || !party.IsPartyActive())
                yield break;

            foreach (var info in party.GetPartyMembers())
            {
                if ((info.Id & 0x7FFF) == (hero.NetworkId & 0x7FFF) || !world.TryGetWalkerById(info.Id, out var walker) ||
                    walker is not PlayerObject member || member.IsDead || Vector2.Distance(hero.Location, member.Location) > range)
                    continue;
                yield return (member, info.HealthPercentage);
            }
        }

        // ─────────────────────────────── Pick-up ───────────────────────────────

        private bool PickUp(MuHelperConfig c, HeroObject hero, double now)
        {
            if (!c.PickAllItems && !c.PickSelectItems)
                return false;

            var network = MuGame.Network;
            var scope = network.GetScopeManager();
            int range = Math.Max(1, c.ObtainRange);
            ScopeObject best = null;
            float bestDistance = float.MaxValue;
            foreach (var type in DropTypes)
            {
                foreach (var drop in scope.GetScopeItems(type))
                {
                    if (drop.PositionX == 0 && drop.PositionY == 0)
                        continue;
                    if (IsIgnored(drop.Id, now))
                        continue;
                    float distance = Vector2.Distance(hero.Location, new Vector2(drop.PositionX, drop.PositionY));
                    if (distance > range || distance >= bestDistance || !ShouldPickUp(c, drop))
                        continue;
                    best = drop;
                    bestDistance = distance;
                }
            }

            if (best == null)
                return false;

            if (bestDistance > PickupDistance)
            {
                if (_dropApproach.IsStuck(best.Id, bestDistance, now))
                {
                    _ignoredIds[best.Id] = now + IgnoreMs; // no path to it: skip it for a while
                    _dropApproach.Reset();
                    return false;
                }

                if (!hero.IsMoving && !hero.MovementIntent)
                    hero.MoveTo(new Vector2(best.PositionX, best.PositionY));
                return true;
            }

            _dropApproach.Reset();

            // Same request as the Space pick-up: the item data is stashed for the inventory answer.
            var state = network.GetCharacterState();
            state.SetPendingPickupRawId(best.RawId);
            if (best is ItemScopeObject item)
                state.StashPickedItem(item.ItemData.ToArray());
            _ = network.GetCharacterService().SendPickupItemRequestAsync(best.RawId, network.TargetVersion);
            _ignoredIds[best.Id] = now + IgnoreMs; // a full inventory must not stall the hunt
            return true;
        }

        /// <summary>OpenMU ItemPickupHandler rules: all items, or the selected kinds (jewels, excellent, ancient, zen, names).</summary>
        private static bool ShouldPickUp(MuHelperConfig c, ScopeObject drop)
        {
            if (c.PickAllItems)
                return true;
            if (drop is MoneyScopeObject)
                return c.PickZen;
            if (drop is not ItemScopeObject item)
                return false;

            var data = item.ItemData.Span;
            if (c.PickJewel && ItemDatabase.IsJewelItem(data))
                return true;
            var details = ItemDatabase.ParseItemDetails(data);
            if ((c.PickExcellent && details.IsExcellent) || (c.PickAncient && details.IsAncient))
                return true;
            if (!c.PickExtraItems)
                return false;

            string name = ItemDatabase.GetItemName(data) ?? string.Empty;
            foreach (string filter in c.ExtraItemNames)
                if (filter.Length > 0 && name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        // ─────────────────────────────── Hunting ───────────────────────────────

        /// <summary>Walks back to the start position when out of range, or after the max. time away.</summary>
        private bool Regroup(MuHelperConfig c, HeroObject hero, int range, double now)
        {
            float distance = Vector2.Distance(hero.Location, _origin);
            if (distance <= AtOrigin)
            {
                _awaySinceMs = -1;
                return false;
            }

            if (_awaySinceMs < 0)
                _awaySinceMs = now;
            bool awayTooLong = c.ReturnToOriginalPosition && c.MaxSecondsAway > 0 && now - _awaySinceMs >= c.MaxSecondsAway * 1000.0;
            if (!awayTooLong && distance <= range)
                return false;

            _targetId = 0;
            _awaySinceMs = now;
            hero.MoveTo(_origin);
            return true;
        }

        private void Attack(MuHelperConfig c, HeroObject hero, WalkableWorldControl world, CharacterState state, int range, double now)
        {
            var target = FindTarget(world, hero, range, now);
            if (target == null)
            {
                if (Vector2.Distance(hero.Location, _origin) > AtOrigin)
                    hero.MoveTo(_origin);
                return;
            }

            int slot = -1, skillId = 0;
            for (int i = 0; i < _slotLastUse.Length && skillId == 0; i++)
            {
                if (SlotReady(c, i, world, hero, range, now))
                {
                    slot = i;
                    skillId = i == 0 ? c.ActivationSkill1Id : c.ActivationSkill2Id;
                }
            }

            bool combo = skillId == 0 && c.UseCombo && c.BasicSkillId > 0 && c.ActivationSkill1Id > 0 && c.ActivationSkill2Id > 0;
            if (combo)
                skillId = (_comboStep % 3) switch { 0 => c.BasicSkillId, 1 => c.ActivationSkill1Id, _ => c.ActivationSkill2Id };
            if (skillId == 0)
                skillId = c.BasicSkillId;

            if (FindSkill(state, skillId) is { } skill)
            {
                if (!InSkillRange(hero, target, skill.SkillId))
                {
                    if (!TargetStuck(hero, target, now))
                        hero.MoveTo(target.Location);
                    return;
                }

                if (Skills.CastSkillFromHotbar(skill, target))
                {
                    _targetApproach.Reset();
                    if (slot >= 0) _slotLastUse[slot] = now;
                    if (combo) _comboStep++;
                    return;
                }

                if (!c.FallbackBasicAttack)
                    return;
            }

            // Attack walks into melee range by itself; give up on targets it cannot reach.
            if (Vector2.Distance(hero.Location, target.Location) > hero.GetAttackRangeTiles())
            {
                if (TargetStuck(hero, target, now))
                    return;
            }
            else
            {
                _targetApproach.Reset();
            }

            hero.Attack(target);
        }

        /// <summary>True (and the target is skipped for a while) when walking to it made no progress.</summary>
        private bool TargetStuck(HeroObject hero, MonsterObject target, double now)
        {
            if (!_targetApproach.IsStuck(target.NetworkId, Vector2.Distance(hero.Location, target.Location), now))
                return false;

            _ignoredIds[target.NetworkId] = now + IgnoreMs;
            _targetApproach.Reset();
            _targetId = 0;
            return true;
        }

        private bool IsIgnored(ushort id, double now) => _ignoredIds.TryGetValue(id, out double until) && until > now;

        /// <summary>Activation skill rules of OpenMU's CombatHandler: either every N seconds, or with N+ monsters around.</summary>
        private bool SlotReady(MuHelperConfig c, int slot, WalkableWorldControl world, HeroObject hero, int range, double now)
        {
            bool first = slot == 0;
            int skillId = first ? c.ActivationSkill1Id : c.ActivationSkill2Id;
            bool timer = first ? c.Skill1UseTimer : c.Skill2UseTimer;
            bool condition = first ? c.Skill1UseCondition : c.Skill2UseCondition;
            if (skillId <= 0)
                return false;

            if (timer && !condition)
            {
                int delay = first ? c.DelaySkill1 : c.DelaySkill2;
                return delay > 0 && now - _slotLastUse[slot] >= delay * 1000.0;
            }

            if (condition && !timer)
            {
                bool attacking = first ? c.Skill1ConditionAttacking : c.Skill2ConditionAttacking;
                int needed = (first ? c.Skill1SubCondition : c.Skill2SubCondition) + 2;
                return CountMonsters(world, hero, range, attacking) >= needed;
            }

            return false;
        }

        private bool IsHuntable(MonsterObject monster, WorldControl world, int range)
            => monster != null && !monster.IsDead && !monster.IsSummoned && monster.World == world &&
               Vector2.Distance(monster.Location, _origin) <= range;

        private int CountMonsters(WalkableWorldControl world, HeroObject hero, int range, bool attackingHero)
        {
            int count = 0;
            ushort heroId = (ushort)(hero.NetworkId & 0x7FFF);
            var monsters = world.Monsters;
            for (int i = 0; i < monsters.Count; i++)
            {
                var monster = monsters[i];
                if (IsHuntable(monster, world, range) && (!attackingHero || (monster.LastAttackTargetId & 0x7FFF) == heroId))
                    count++;
            }
            return count;
        }

        /// <summary>Keeps the current target while it is alive and inside the hunting area, else takes the nearest monster.</summary>
        private MonsterObject FindTarget(WalkableWorldControl world, HeroObject hero, int range, double now)
        {
            if (_targetId != 0 && world.TryGetWalkerById(_targetId, out var walker) && walker is MonsterObject current && IsHuntable(current, world, range))
                return current;

            MonsterObject best = null;
            float bestDistance = float.MaxValue;
            var monsters = world.Monsters;
            for (int i = 0; i < monsters.Count; i++)
            {
                var monster = monsters[i];
                if (!IsHuntable(monster, world, range) || IsIgnored(monster.NetworkId, now))
                    continue;
                float distance = Vector2.DistanceSquared(hero.Location, monster.Location);
                if (distance < bestDistance)
                {
                    best = monster;
                    bestDistance = distance;
                }
            }

            _targetId = best?.NetworkId ?? 0;
            return best;
        }

        private static bool InSkillRange(HeroObject hero, WalkerObject target, ushort skillId)
        {
            uint range = SkillDatabase.GetSkillRange(skillId);
            return range == 0 || Vector2.Distance(hero.Location, target.Location) <= range;
        }

        private static SkillEntryState FindSkill(CharacterState state, int skillId)
        {
            if (skillId <= 0)
                return null;
            foreach (var skill in state.GetSkills())
                if (skill.SkillId == skillId)
                    return skill;
            return null;
        }
    }
}
