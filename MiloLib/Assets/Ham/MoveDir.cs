﻿using MiloLib.Classes;
using MiloLib.Utils;

namespace MiloLib.Assets.Ham
{
    [Name("MoveDir"), Description("Dir for HamMoves, contains debugging functionality")]
    public class MoveDir : SkeletonDir
    {
        private ushort altRevision;
        private ushort revision;

        public uint unkInt1;
        public uint unkInt2;
        public uint unkInt3;
        public uint unkInt4;

        public MoveDir(ushort revision = 35, ushort altRevision = 0) : base(revision, altRevision)
        {
            revision = revision;
            altRevision = altRevision;
            return;
        }

        public MoveDir Read(EndianReader reader, bool standalone, DirectoryMeta parent, DirectoryMeta.Entry entry)
        {
            uint combinedRevision = reader.ReadUInt32();
            if (BitConverter.IsLittleEndian) (revision, altRevision) = ((ushort)(combinedRevision & 0xFFFF), (ushort)((combinedRevision >> 16) & 0xFFFF));
            else (altRevision, revision) = ((ushort)(combinedRevision & 0xFFFF), (ushort)((combinedRevision >> 16) & 0xFFFF));

            base.Read(reader, false, parent, entry);

            if (entry != null && !entry.isProxy)
            {
                unkInt1 = reader.ReadUInt32();
                unkInt2 = reader.ReadUInt32();
                unkInt3 = reader.ReadUInt32();
                unkInt4 = reader.ReadUInt32();
            }
            else
            {
                // there is prooooobably some fields here but ive never seen them be anything but empty
                reader.ReadBlock(13);

                if (revision == 34)
                {
                    reader.ReadBlock(12);
                }
            }

            if (revision >= 34)
            {
                reader.ReadBlock(5);
            }

            if (standalone)
                if ((reader.Endianness == Endian.BigEndian ? 0xADDEADDE : 0xDEADDEAD) != reader.ReadUInt32()) throw new Exception("Got to end of standalone asset but didn't find the expected end bytes, read likely did not succeed");

            return this;
        }

        public override void Write(EndianWriter writer, bool standalone, DirectoryMeta parent, DirectoryMeta.Entry? entry)
        {
            writer.WriteUInt32(BitConverter.IsLittleEndian ? (uint)((altRevision << 16) | revision) : (uint)((revision << 16) | altRevision));

            base.Write(writer, false, parent, entry);

            if (entry != null && !entry.isProxy)
            {
                writer.WriteUInt32(unkInt1);
                writer.WriteUInt32(unkInt2);
                writer.WriteUInt32(unkInt3);
                writer.WriteUInt32(unkInt4);
            }
            else
            {
                writer.WriteBlock(new byte[13]);

                if (revision == 34)
                {
                    writer.WriteBlock(new byte[12]);
                }
            }

            if (revision >= 34)
            {
                writer.WriteBlock(new byte[5] { 0x4, 0x68, 0x61, 0x6d, 0x32 });
            }



            if (standalone)
                writer.WriteBlock(new byte[4] { 0xAD, 0xDE, 0xAD, 0xDE });
        }

        public override bool IsDirectory()
        {
            return true;
        }

    }
}
