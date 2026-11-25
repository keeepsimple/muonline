using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Threading.Tasks;
using Client.Main.Controls.UI;
using Client.Main.Controls.UI.Game;
using Client.Main.Core.Client;
using Client.Main.Core.Utilities;
using Microsoft.Xna.Framework;

namespace Client.Main.Networking.PacketHandling.Handlers
{
    /// <summary>
    /// Drives the HUDs and result windows of the instanced events (Illusion Temple, Doppelganger, Empire Guardian).
    /// The packets the client already handled elsewhere (BF/01, BF/04, BF/09, F7/02, sounds and weather) call the
    /// static Apply* methods from <see cref="MiscGamePacketHandler"/>; all other sub codes are registered here.
    /// </summary>
    public class InstancedEventHandler : IGamePacketHandler
    {
        private readonly CharacterState _characterState;

        public InstancedEventHandler(CharacterState characterState)
        {
            _characterState = characterState;
        }

        /// <summary>
        /// Winning team of an Illusion Temple (0 = Allied Forces, 1 = Illusion Forces, -1 = draw). Like the server,
        /// a team needs at least two points and more than the other team.
        /// </summary>
        public static int IllusionTempleWinner(int alliedPoints, int illusionPoints)
        {
            if (alliedPoints >= 2 && alliedPoints > illusionPoints) return 0;
            if (illusionPoints >= 2 && illusionPoints > alliedPoints) return 1;
            return -1;
        }

        // ───────────────────────── Illusion Temple (called by MiscGamePacketHandler) ─────────────────────────

        /// <summary>BF/01 IllusionTempleState.</summary>
        public static void ApplyIllusionTempleState(ReadOnlySpan<byte> packet, ushort ownId)
        {
            if (!InstancedEventPackets.TryParseIllusionTempleState(packet, out var s)) return;
            MuGame.ScheduleOnMainThread(() => InstancedEventHud.Instance.SetIllusionTempleState(
                s.RemainingSeconds, s.AlliedPoints, s.IllusionPoints, s.MyTeam, s.RelicCarrierId, ownId));
        }

        /// <summary>BF/09 IllusionTempleEventState: 1 and 2 open the HUD, 3 closes it.</summary>
        public static void ApplyIllusionTemplePhase(byte state)
        {
            MuGame.ScheduleOnMainThread(() =>
            {
                if (state is 1 or 2) InstancedEventHud.Instance.BeginIllusionTemple();
                else if (state == 3 && InstancedEventHud.Instance.Mode == InstancedEventMode.IllusionTemple) InstancedEventHud.Instance.Hide();
            });
        }

        /// <summary>BF/04 IllusionTempleResult: score board with the points of both teams and the experience of each player.</summary>
        public static void ApplyIllusionTempleResult(ReadOnlySpan<byte> packet, string ownName)
        {
            if (!InstancedEventPackets.TryParseIllusionTempleResult(packet, out var result)) return;

            int winner = IllusionTempleWinner(result.AlliedPoints, result.IllusionPoints);
            int ownTeam = -1;
            var rows = new List<InstancedEventResultWindow.Row>(result.Rows.Count);
            foreach (var row in result.Rows)
            {
                bool own = string.Equals(row.Name, ownName, StringComparison.Ordinal);
                if (own) ownTeam = row.Team;
                rows.Add(new InstancedEventResultWindow.Row(row.Name, TeamName(row.Team), row.Experience.ToString("N0"), own));
            }

            string headline;
            Color color;
            if (winner < 0) { headline = "DRAW"; color = Color.White; }
            else if (ownTeam < 0) { headline = $"{TeamName(winner)} win"; color = Color.White; }
            else if (ownTeam == winner) { headline = "VICTORY"; color = new Color(128, 255, 128); }
            else { headline = "DEFEAT"; color = new Color(255, 128, 128); }

            var details = new[] { $"Allied Forces {result.AlliedPoints}  :  {result.IllusionPoints} Illusion Forces" };
            MuGame.ScheduleOnMainThread(() =>
            {
                InstancedEventHud.Instance.Hide();
                InstancedEventResultWindow.Instance.ShowResult("ILLUSION TEMPLE", headline, color, details, rows, "Team", "Experience");
            });
        }

        private static string TeamName(int team) => team == 0 ? "Allied Forces" : "Illusion Forces";

        /// <summary>Illusion Temple special skills (OpenMU IllusionTempleContext).</summary>
        public static string IllusionTempleSkillName(ushort skill) => skill switch
        {
            210 => "Order of Protection",
            211 => "Order of Restraint",
            212 => "Order of Tracking",
            213 => "Order of Weaken",
            _ => $"Skill {skill}",
        };

        /// <summary>
        /// BF/00 result text. The server explains most refusals itself and then sends 1, which gets no client text;
        /// 3, 4 and 7 have texts of their own. Null = nothing to show.
        /// </summary>
        public static string DescribeIllusionTempleEnter(byte result) => result switch
        {
            0 or 1 => null,
            3 => "The admission and scroll levels do not match.",
            4 => "The Illusion Temple is full.",
            7 => "Player killers cannot enter the Illusion Temple.",
            _ => $"You cannot enter the Illusion Temple ({result}).",
        };

        [PacketHandler(0xBF, 0x00)] // IllusionTempleEnterResult (S2C); on success the server warps the player
        public Task HandleIllusionTempleEnterResultAsync(Memory<byte> packet)
        {
            if (packet.Length < 5) return Task.CompletedTask;
            byte result = packet.Span[4];
            string text = DescribeIllusionTempleEnter(result);
            if (result == 0) MuGame.ScheduleOnMainThread(() => CursedTempleEnterControl.Instance.Hide());
            else if (text != null) MuGame.ScheduleOnMainThread(() => RequestDialog.ShowInfo(text));
            return Task.CompletedTask;
        }

        [PacketHandler(0xBF, 0x02)] // IllusionTempleSkillUsageResult (S2C): answer to the own skill request
        public Task HandleIllusionTempleSkillUsageAsync(Memory<byte> packet)
        {
            if (packet.Length < 12) return Task.CompletedTask;
            var span = packet.Span;
            bool success = span[4] == 1;
            ushort skill = BinaryPrimitives.ReadUInt16BigEndian(span[5..]);
            ushort source = (ushort)(BinaryPrimitives.ReadUInt16LittleEndian(span[8..]) & 0x7FFF);
            if (source != _characterState.Id) return Task.CompletedTask;
            EventZoneHandler.ShowSystemMessage(success
                ? $"{IllusionTempleSkillName(skill)} activated."
                : $"{IllusionTempleSkillName(skill)} could not be used.");
            return Task.CompletedTask;
        }

        [PacketHandler(0xBF, 0x03)] // IllusionTempleUserCount (S2C): players per temple, for the entrance dialog (OpenMU does not send it yet)
        public Task HandleIllusionTempleUserCountAsync(Memory<byte> packet)
        {
            if (packet.Length < 10) return Task.CompletedTask;
            byte[] counts = packet.Span.Slice(4, 6).ToArray();
            MuGame.ScheduleOnMainThread(() => CursedTempleEnterControl.Instance.SetUserCounts(counts));
            return Task.CompletedTask;
        }

        [PacketHandler(0xBF, 0x07)] // IllusionTempleSkillEnded (S2C): a special skill effect wore off on an object
        public Task HandleIllusionTempleSkillEndedAsync(Memory<byte> packet)
        {
            if (packet.Length < 8) return Task.CompletedTask;
            ushort skill = BinaryPrimitives.ReadUInt16BigEndian(packet.Span[4..]);
            ushort target = (ushort)(BinaryPrimitives.ReadUInt16LittleEndian(packet.Span[6..]) & 0x7FFF);
            if (target == _characterState.Id)
                EventZoneHandler.ShowSystemMessage($"{IllusionTempleSkillName(skill)} has worn off.");
            return Task.CompletedTask;
        }

        [PacketHandler(0xBF, 0x06)] // IllusionTempleSkillPointUpdate (S2C)
        public Task HandleIllusionTempleSkillPointsAsync(Memory<byte> packet)
        {
            if (packet.Length < 5) return Task.CompletedTask;
            int points = packet.Span[4];
            MuGame.ScheduleOnMainThread(() => InstancedEventHud.Instance.SetIllusionTempleSkillPoints(points));
            return Task.CompletedTask;
        }

        [PacketHandler(0xBF, 0x08)] // IllusionTempleHolyItemRelics (S2C): a player picked up the relic
        public Task HandleIllusionTempleRelicAsync(Memory<byte> packet)
        {
            if (!InstancedEventPackets.TryParseIllusionTempleRelic(packet.Span, out ushort userIndex, out string name))
                return Task.CompletedTask;

            MuGame.ScheduleOnMainThread(() => InstancedEventHud.Instance.SetIllusionTempleRelic(userIndex, name));
            return Task.CompletedTask;
        }

        // ───────────────────────── Doppelganger ─────────────────────────

        /// <summary>BF/0E result text; 0 (entered) and 4 (entrance available, unlocks the entry dialog) show nothing.</summary>
        public static string DescribeDoppelgangerEnter(byte result) => result switch
        {
            0 or 4 => null,
            2 => "Battle has already commenced. You cannot enter.",
            3 => "You cannot enter if you are a 1st Stage Outlaw.",
            _ => "You cannot enter the Doppelganger event.",
        };

        [PacketHandler(0xBF, 0x0E)] // DoppelgangerEnterResult (S2C)
        public Task HandleDoppelgangerEnterResultAsync(Memory<byte> packet)
        {
            if (packet.Length < 5) return Task.CompletedTask;
            byte result = packet.Span[4];
            string text = DescribeDoppelgangerEnter(result);
            MuGame.ScheduleOnMainThread(() =>
            {
                DoppelgangerEnterControl.Instance.ApplyEnterResult(result);
                if (text != null) RequestDialog.ShowInfo(text);
            });
            return Task.CompletedTask;
        }

        [PacketHandler(0xBF, 0x0F)] // DoppelgangerMonsterPosition (S2C)
        public Task HandleDoppelgangerMonsterPositionAsync(Memory<byte> packet)
        {
            if (packet.Length < 5) return Task.CompletedTask;
            int position = packet.Span[4];
            MuGame.ScheduleOnMainThread(() => InstancedEventHud.Instance.SetDoppelgangerMonsterPosition(position));
            return Task.CompletedTask;
        }

        [PacketHandler(0xBF, 0x10)] // DoppelgangerStateUpdate (S2C): 2 = Playing opens the frame, 3 = Ended closes it
        public Task HandleDoppelgangerStateAsync(Memory<byte> packet)
        {
            if (packet.Length < 5) return Task.CompletedTask;
            byte state = packet.Span[4];
            MuGame.ScheduleOnMainThread(() =>
            {
                if (state == 2) InstancedEventHud.Instance.BeginDoppelganger();
                else if (state == 3 && InstancedEventHud.Instance.Mode == InstancedEventMode.Doppelganger) InstancedEventHud.Instance.Hide();
            });
            return Task.CompletedTask;
        }

        [PacketHandler(0xBF, 0x11)] // DoppelgangerIceWalkerState (S2C)
        public Task HandleDoppelgangerIceWalkerAsync(Memory<byte> packet)
        {
            if (packet.Length < 6) return Task.CompletedTask;
            bool appeared = packet.Span[4] == 0;
            int position = packet.Span[5];
            MuGame.ScheduleOnMainThread(() => InstancedEventHud.Instance.SetDoppelgangerIceWalker(appeared, position));
            return Task.CompletedTask;
        }

        [PacketHandler(0xBF, 0x12)] // DoppelgangerPlayInfo (S2C), once per second
        public Task HandleDoppelgangerPlayInfoAsync(Memory<byte> packet)
        {
            if (!InstancedEventPackets.TryParseDoppelgangerPlayInfo(packet.Span, out var info)) return Task.CompletedTask;
            ushort ownId = _characterState.Id;
            MuGame.ScheduleOnMainThread(() => InstancedEventHud.Instance.SetDoppelgangerPlayInfo(info.RemainingSeconds, info.Players, ownId));
            return Task.CompletedTask;
        }

        [PacketHandler(0xBF, 0x13)] // DoppelgangerResult (S2C); the reward experience is not shown (the server sends 0)
        public Task HandleDoppelgangerResultAsync(Memory<byte> packet)
        {
            if (packet.Length < 5) return Task.CompletedTask;
            byte result = packet.Span[4];
            (string headline, string detail, Color color) = result switch
            {
                0 => ("SUCCESS", "The magic circle was defended.", new Color(128, 255, 128)),
                2 => ("FAILED", "Too many monsters reached the magic circle.", new Color(255, 128, 128)),
                _ => ("FAILED", "You failed to defend the magic circle.", new Color(255, 128, 128)),
            };
            MuGame.ScheduleOnMainThread(() =>
            {
                InstancedEventHud.Instance.Hide();
                InstancedEventResultWindow.Instance.ShowResult("DOPPELGANGER", headline, color, new[] { detail });
            });
            return Task.CompletedTask;
        }

        [PacketHandler(0xBF, 0x14)] // DoppelgangerMonsterGoal (S2C)
        public Task HandleDoppelgangerGoalAsync(Memory<byte> packet)
        {
            if (packet.Length < 6) return Task.CompletedTask;
            int max = packet.Span[4];
            int count = packet.Span[5];
            MuGame.ScheduleOnMainThread(() => InstancedEventHud.Instance.SetDoppelgangerGoal(count, max));
            return Task.CompletedTask;
        }

        // ───────────────────────── Empire Guardian ─────────────────────────

        /// <summary>F7/02 ImperialGuardianEnterResult: a successful entry (also of the next zone) opens the timer HUD.</summary>
        public static void ApplyEmpireGuardianEnter(ReadOnlySpan<byte> packet)
        {
            if (!InstancedEventPackets.TryParseEmpireGuardianEnter(packet, out var enter) || enter.Result != 0) return;
            MuGame.ScheduleOnMainThread(() => InstancedEventHud.Instance.BeginEmpireGuardian(enter.Day, enter.Zone));
        }

        [PacketHandler(0xF7, 0x04)] // ImperialGuardianTimer (S2C), once per second
        public Task HandleEmpireGuardianTimerAsync(Memory<byte> packet)
        {
            if (!InstancedEventPackets.TryParseEmpireGuardianTimer(packet.Span, out var timer)) return Task.CompletedTask;
            MuGame.ScheduleOnMainThread(() => InstancedEventHud.Instance.SetEmpireGuardianTimer(timer.Type, timer.RemainingMilliseconds, timer.MonsterCount));
            return Task.CompletedTask;
        }

        [PacketHandler(0xF7, 0x06)] // ImperialGuardianResult (S2C): 0 failed, 1 zone cleared, 2 completed
        public Task HandleEmpireGuardianResultAsync(Memory<byte> packet)
        {
            if (!InstancedEventPackets.TryParseEmpireGuardianResult(packet.Span, out var result)) return Task.CompletedTask;
            (string headline, Color color) = result.Result switch
            {
                1 => ("ZONE CLEARED", new Color(128, 255, 128)),
                2 => ("EVENT COMPLETED", new Color(128, 255, 128)),
                _ => ("EVENT FAILED", new Color(255, 128, 128)),
            };
            var details = result.Result == 2 ? new[] { $"Experience Gained: {result.Experience:N0}" } : Array.Empty<string>();
            MuGame.ScheduleOnMainThread(() =>
            {
                InstancedEventHud.Instance.Hide();
                InstancedEventResultWindow.Instance.ShowResult("EMPIRE GUARDIAN", headline, color, details);
            });
            return Task.CompletedTask;
        }
    }
}
