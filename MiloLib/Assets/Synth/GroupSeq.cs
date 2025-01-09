using System.Reflection.Metadata;
using MiloLib.Classes;
using MiloLib.Utils;

namespace MiloLib.Assets.Synth
{
    [Name("GroupSeq"), Description("A sequence which plays other sequences.  Abstract base class.")]
    public class GroupSeq : Object
    {
        public ushort altRevision;
        public ushort revision;

        public Sfx.Sequence seq = new();

        private uint childrenCount;

        [Name("Children"), Description("The children of this sequence")]
        public List<Symbol> children = new();

        public GroupSeq Read(EndianReader reader, bool standalone, DirectoryMeta parent, DirectoryMeta.Entry entry)
        {
            uint combinedRevision = reader.ReadUInt32();
            if (BitConverter.IsLittleEndian) (revision, altRevision) = ((ushort)(combinedRevision & 0xFFFF), (ushort)(combinedRevision >> 16 & 0xFFFF));
            else (altRevision, revision) = ((ushort)(combinedRevision & 0xFFFF), (ushort)(combinedRevision >> 16 & 0xFFFF));

            if (1 < revision)
            {
                seq.Read(reader, parent, entry);

                childrenCount = reader.ReadUInt32();
                for (int i = 0; i < childrenCount; i++)
                {
                    children.Add(Symbol.Read(reader));
                }
            }

            if (standalone)
            {
                if ((reader.Endianness == Endian.BigEndian ? 0xADDEADDE : 0xDEADDEAD) != reader.ReadUInt32()) throw new Exception("Got to end of standalone asset but didn't find the expected end bytes, read likely did not succeed");
            }

            return this;
        }

        public override void Write(EndianWriter writer, bool standalone, DirectoryMeta parent, DirectoryMeta.Entry? entry)
        {
            writer.WriteUInt32(BitConverter.IsLittleEndian ? (uint)(altRevision << 16 | revision) : (uint)(revision << 16 | altRevision));

            if (1 < revision)
            {
                seq.Write(writer, false, parent, entry);

                writer.WriteUInt32((uint)children.Count);
                foreach (var child in children)
                {
                    Symbol.Write(writer, child);
                }
            }

            if (standalone)
            {
                writer.WriteBlock(new byte[4] { 0xAD, 0xDE, 0xAD, 0xDE });
            }
        }
    }
}