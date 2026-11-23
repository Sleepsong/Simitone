using Simitone.Client.UI.Screens;
using System;
using System.Collections.Generic;
using System.IO;

namespace Simitone.Client.Network.Coop
{
    /// <summary>
    /// The family being put together in Create-a-Family, as shared between the players of a co-op session:
    /// its surname and the Sims made so far. A Sim here is only a description (name, looks, personality);
    /// nothing is written to anyone's game until the host accepts the family.
    ///
    /// One player at a time holds control and edits it; every change they make is sent as a whole new copy.
    /// </summary>
    public class CoopCasState
    {
        public string Surname = "";
        public List<CASFamilyMember> Members = new List<CASFamilyMember>();

        /// <summary>The Sims 1 allows eight Sims in a family.</summary>
        public const int MaxMembers = 8;
        private const int MaxName = 64;
        private const int MaxBio = 2048;
        private const int MaxAssetName = 128;

        public byte[] Pack()
        {
            using (var stream = new MemoryStream())
            {
                using (var writer = new BinaryWriter(stream))
                {
                    writer.Write(Surname ?? "");
                    writer.Write(Members.Count);
                    foreach (var member in Members)
                    {
                        writer.Write(member.Name ?? "");
                        writer.Write(member.Bio ?? "");
                        writer.Write(member.Gender);
                        writer.Write(member.SkinColor ?? "");
                        writer.Write(member.Head ?? "");
                        writer.Write(member.HeadTex ?? "");
                        writer.Write(member.Body ?? "");
                        writer.Write(member.BodyTex ?? "");
                        writer.Write(member.HandgroupTex ?? "");
                        for (int i = 0; i < 5; i++) writer.Write((member.Personality != null && member.Personality.Length > i) ? member.Personality[i] : (short)0);
                    }
                }
                return stream.ToArray();
            }
        }

        /// <summary>
        /// Reads a family another player sent. Throws if it is not one this game could have made.
        /// </summary>
        public static CoopCasState Unpack(byte[] data)
        {
            var state = new CoopCasState();
            using (var reader = new BinaryReader(new MemoryStream(data)))
            {
                state.Surname = Limited(reader.ReadString(), MaxName);
                var count = reader.ReadInt32();
                if (count < 0 || count > MaxMembers) throw new InvalidDataException("Bad family size " + count);
                for (int i = 0; i < count; i++)
                {
                    var member = new CASFamilyMember();
                    member.Name = Limited(reader.ReadString(), MaxName);
                    member.Bio = Limited(reader.ReadString(), MaxBio);
                    member.Gender = reader.ReadInt16();
                    if (member.Gender < 0 || member.Gender > 3) throw new InvalidDataException("Bad Sim type " + member.Gender);
                    member.SkinColor = Asset(reader.ReadString());
                    member.Head = Asset(reader.ReadString());
                    member.HeadTex = Asset(reader.ReadString());
                    member.Body = Asset(reader.ReadString());
                    member.BodyTex = Asset(reader.ReadString());
                    member.HandgroupTex = Asset(reader.ReadString());
                    var total = 0;
                    for (int j = 0; j < 5; j++)
                    {
                        var points = reader.ReadInt16();
                        if (points < 0 || points > 1000) throw new InvalidDataException("Bad personality " + points);
                        member.Personality[j] = points;
                        total += points;
                    }
                    if (total > 2500) throw new InvalidDataException("Too many personality points");
                    state.Members.Add(member);
                }
            }
            return state;
        }

        private static string Limited(string text, int max)
        {
            if (text.Length > max) throw new InvalidDataException("Text too long");
            return text;
        }

        /// <summary>
        /// Heads, bodies and textures are named after game files, and the names end up in the new Sim's own file.
        /// The host also checks that it has each of them before accepting a Sim.
        /// </summary>
        private static string Asset(string name)
        {
            if (name.Length > MaxAssetName) throw new InvalidDataException("Name too long");
            foreach (var c in name)
            {
                //the Sim's file lists these as "name,KEY=texture", so the separators (and anything path-like) are out
                if (c < 32 || c == 127 || ",=/\\:\"<>|*?".IndexOf(c) >= 0) throw new InvalidDataException("Unexpected character in " + name);
            }
            return name;
        }

        public static bool SameMember(CASFamilyMember a, CASFamilyMember b)
        {
            if (a == null || b == null) return a == b;
            if ((a.Name ?? "") != (b.Name ?? "") || (a.Bio ?? "") != (b.Bio ?? "") || a.Gender != b.Gender) return false;
            if ((a.SkinColor ?? "") != (b.SkinColor ?? "") || (a.Head ?? "") != (b.Head ?? "") || (a.HeadTex ?? "") != (b.HeadTex ?? "")) return false;
            if ((a.Body ?? "") != (b.Body ?? "") || (a.BodyTex ?? "") != (b.BodyTex ?? "") || (a.HandgroupTex ?? "") != (b.HandgroupTex ?? "")) return false;
            for (int i = 0; i < 5; i++)
            {
                if (a.Personality[i] != b.Personality[i]) return false;
            }
            return true;
        }
    }
}
