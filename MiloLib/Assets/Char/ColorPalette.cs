﻿using MiloLib.Classes;
using MiloLib.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MiloLib.Assets
{
    [Name("ColorPalette"), Description("List of primary/secondary colors for OutfitConfig")]
    public class ColorPalette : Object
    {
        private Dictionary<Game.MiloGame, uint> gameRevisions = new Dictionary<Game.MiloGame, uint>
        {
            { Game.MiloGame.RockBand2, 1 },
            { Game.MiloGame.LegoRockBand, 1 },
            { Game.MiloGame.TheBeatlesRockBand, 1 },
            { Game.MiloGame.GreenDayRockBand, 1 },
            { Game.MiloGame.RockBand3, 1 },
            { Game.MiloGame.DanceCentral, 1 },

            // assumption on this TODO: double check
            { Game.MiloGame.DanceCentral2, 1 },
            { Game.MiloGame.RockBandBlitz, 1 },
            { Game.MiloGame.DanceCentral3, 1 }
        };

        private ushort altRevision;
        private ushort revision;

        private uint colorCount;

        [Name("Colors"), Description("Color for materials")]
        public List<HmxColor4> colors = new();

        public ColorPalette Read(EndianReader reader, bool standalone, DirectoryMeta parent, DirectoryMeta.Entry entry)
        {
            uint combinedRevision = reader.ReadUInt32();
            if (BitConverter.IsLittleEndian) (revision, altRevision) = ((ushort)(combinedRevision & 0xFFFF), (ushort)((combinedRevision >> 16) & 0xFFFF));
            else (altRevision, revision) = ((ushort)(combinedRevision & 0xFFFF), (ushort)((combinedRevision >> 16) & 0xFFFF));

            if (revision != 1)
            {
                throw new UnsupportedAssetRevisionException("ColorPalette", revision);
            }

            base.Read(reader, false, parent, entry);

            colorCount = reader.ReadUInt32();
            // sanity check on color count
            if (colorCount > 0x100)
            {
                throw new InvalidDataException("Color count is too high, ColorPalette is likely invalid");
            }

            for (int i = 0; i < colorCount; i++)
            {
                colors.Add(new HmxColor4().Read(reader));
            }

            if (standalone)
                if ((reader.Endianness == Endian.BigEndian ? 0xADDEADDE : 0xDEADDEAD) != reader.ReadUInt32()) throw new Exception("Got to end of standalone asset but didn't find the expected end bytes, read likely did not succeed");

            return this;
        }

        public override void Write(EndianWriter writer, bool standalone, DirectoryMeta parent, DirectoryMeta.Entry? entry)
        {
            writer.WriteUInt32(BitConverter.IsLittleEndian ? (uint)((altRevision << 16) | revision) : (uint)((revision << 16) | altRevision));

            base.Write(writer, false, parent, entry);

            writer.WriteUInt32((uint)colors.Count);
            foreach (var color in colors)
            {
                color.Write(writer);
            }

            if (standalone)
            {
                writer.WriteBlock(new byte[4] { 0xAD, 0xDE, 0xAD, 0xDE });
            }
        }
    }
}

