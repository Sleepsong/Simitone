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
    /// That copy also says what the editor is doing right now - which part of the screen they are on and, while
    /// they are making a Sim, the Sim as it looks so far - so that everyone else's screen can show the same.
    /// </summary>
    public class CoopCasState
    {
        public string Surname = "";
        public List<CASFamilyMember> Members = new List<CASFamilyMember>();

        /// <summary>Where the editor is: 0 the list of families (host only), 1 the family, 2 making or changing a Sim.</summary>
        public byte Mode = ModeFamily;
        /// <summary>While making a Sim: the tab of the editor that is open (0 looks, 1 personality, 2 bio).</summary>
        public byte Tab;
        /// <summary>While making a Sim: the Sim as it stands. Not part of the family until it is accepted.</summary>
        public CASFamilyMember Draft;

        public const byte ModeFamilies = 0;
        public const byte ModeFamily = 1;
        public const byte ModeSim = 2;

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
                    foreach (var member in Members) WriteMember(writer, member);
                    writer.Write(Mode);
                    writer.Write(Tab);
                    writer.Write(Draft != null);
                    if (Draft != null) WriteMember(writer, Draft);
                }
                return stream.ToArray();
            }
        }

        private static void WriteMember(BinaryWriter writer, CASFamilyMember member)
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
                for (int i = 0; i < count; i++) state.Members.Add(ReadMember(reader));
                state.Mode = reader.ReadByte();
                if (state.Mode > ModeSim) throw new InvalidDataException("Bad mode " + state.Mode);
                state.Tab = reader.ReadByte();
                if (state.Tab > 2) throw new InvalidDataException("Bad tab " + state.Tab);
                if (reader.ReadBoolean()) state.Draft = ReadMember(reader);
            }
            return state;
        }

        private static CASFamilyMember ReadMember(BinaryReader reader)
        {
            var member = new CASFamilyMember();
            member.Name = Limited(reader.ReadString(), MaxName);
            member.Bio = Limited(reader.ReadString(), MaxBio);
            member.Gender = reader.ReadInt16();
            if (member.Gender < 0 || member.Gender > 3) throw new InvalidDataException("Bad Sim type " + member.Gender);
            member.SkinColor = reader.ReadString();
            //the editor lists heads and bodies by skin tone, and drops the ones it finds no texture for
            if (member.SkinColor != "lgt" && member.SkinColor != "med" && member.SkinColor != "drk") throw new InvalidDataException("Bad skin tone");
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
            return member;
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
