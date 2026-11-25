using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

namespace Client.Main.Networking.PacketHandling
{
    /// <summary>
    /// Hand parsers of the instanced event packets (Illusion Temple 0xBF, Doppelganger 0xBF, Empire Guardian 0xF7).
    /// Layouts follow the OpenMU ServerToClientPackets.xml; spans include the 4 byte C1 header with sub code.
    /// </summary>
    public static class InstancedEventPackets
    {
        public readonly record struct IllusionTempleState(int RemainingSeconds, ushort RelicCarrierId, int AlliedPoints, int IllusionPoints, byte MyTeam);
        public readonly record struct IllusionTempleResultRow(string Name, byte Team, uint Experience);
        public readonly record struct IllusionTempleResult(int AlliedPoints, int IllusionPoints, IReadOnlyList<IllusionTempleResultRow> Rows);
        public readonly record struct DoppelgangerPlayInfo(int RemainingSeconds, IReadOnlyList<(ushort PlayerId, int Position)> Players);
        public readonly record struct EmpireGuardianEnter(byte Result, byte Day, byte Zone, byte Weather, uint RemainingMilliseconds);
        public readonly record struct EmpireGuardianTimer(byte Type, uint RemainingMilliseconds, byte MonsterCount);
        public readonly record struct EmpireGuardianResult(byte Result, uint Experience);

        private const int IllusionTempleResultEntrySize = 20;
        private const int DoppelgangerPlayerEntrySize = 4;

        /// <summary>BF/01, fixed part 14 bytes (the team mate list is not used by the HUD).</summary>
        public static bool TryParseIllusionTempleState(ReadOnlySpan<byte> p, out IllusionTempleState state)
        {
            state = default;
            if (p.Length < 14) return false;
            state = new IllusionTempleState(
                BinaryPrimitives.ReadUInt16LittleEndian(p[4..]),
                BinaryPrimitives.ReadUInt16LittleEndian(p[6..]),
                p[10], p[11], p[12]);
            return true;
        }

        /// <summary>BF/04: points of both teams and one 20 byte entry per player.</summary>
        public static bool TryParseIllusionTempleResult(ReadOnlySpan<byte> p, out IllusionTempleResult result)
        {
            result = default;
            if (p.Length < 7) return false;
            int count = Math.Min(p[6], (p.Length - 7) / IllusionTempleResultEntrySize);
            var rows = new List<IllusionTempleResultRow>(count);
            for (int i = 0; i < count; i++)
            {
                var e = p.Slice(7 + i * IllusionTempleResultEntrySize, IllusionTempleResultEntrySize);
                rows.Add(new IllusionTempleResultRow(ReadName(e[..10]), e[11], BinaryPrimitives.ReadUInt32LittleEndian(e[16..])));
            }

            result = new IllusionTempleResult(p[4], p[5], rows);
            return true;
        }

        /// <summary>BF/12: remaining seconds and the path position of each party member (4 byte entries from offset 8).</summary>
        public static bool TryParseDoppelgangerPlayInfo(ReadOnlySpan<byte> p, out DoppelgangerPlayInfo info)
        {
            info = default;
            if (p.Length < 8) return false;
            int count = Math.Min(p[6], (p.Length - 8) / DoppelgangerPlayerEntrySize);
            var players = new List<(ushort, int)>(count);
            for (int i = 0; i < count; i++)
            {
                var e = p.Slice(8 + i * DoppelgangerPlayerEntrySize, DoppelgangerPlayerEntrySize);
                players.Add((BinaryPrimitives.ReadUInt16LittleEndian(e), e[3]));
            }

            info = new DoppelgangerPlayInfo(BinaryPrimitives.ReadUInt16LittleEndian(p[4..]), players);
            return true;
        }

        /// <summary>F7/02, 12 bytes.</summary>
        public static bool TryParseEmpireGuardianEnter(ReadOnlySpan<byte> p, out EmpireGuardianEnter enter)
        {
            enter = default;
            if (p.Length < 12) return false;
            enter = new EmpireGuardianEnter(p[4], p[5], p[6], p[7], BinaryPrimitives.ReadUInt32LittleEndian(p[8..]));
            return true;
        }

        /// <summary>F7/04, 16 bytes (type at 4, milliseconds at 8, monster count at 12).</summary>
        public static bool TryParseEmpireGuardianTimer(ReadOnlySpan<byte> p, out EmpireGuardianTimer timer)
        {
            timer = default;
            if (p.Length < 13) return false;
            timer = new EmpireGuardianTimer(p[4], BinaryPrimitives.ReadUInt32LittleEndian(p[8..]), p[12]);
            return true;
        }

        /// <summary>F7/06, 12 bytes (result at 4, experience at 8).</summary>
        public static bool TryParseEmpireGuardianResult(ReadOnlySpan<byte> p, out EmpireGuardianResult result)
        {
            result = default;
            if (p.Length < 12) return false;
            result = new EmpireGuardianResult(p[4], BinaryPrimitives.ReadUInt32LittleEndian(p[8..]));
            return true;
        }

        private static string ReadName(ReadOnlySpan<byte> field)
        {
            int end = field.IndexOf((byte)0);
            return Encoding.UTF8.GetString(end < 0 ? field : field[..end]);
        }

        /// <summary>BF/08: 2 byte user index and a 10 byte name.</summary>
        public static bool TryParseIllusionTempleRelic(ReadOnlySpan<byte> p, out ushort userIndex, out string name)
        {
            userIndex = 0;
            name = string.Empty;
            if (p.Length < 16) return false;
            userIndex = BinaryPrimitives.ReadUInt16LittleEndian(p[4..]);
            name = ReadName(p.Slice(6, 10));
            return true;
        }
    }
}
