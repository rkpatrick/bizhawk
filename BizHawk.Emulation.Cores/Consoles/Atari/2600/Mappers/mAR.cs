﻿using System;
using BizHawk.Common;
/**
  This is the cartridge class for Arcadia (aka Starpath) Supercharger 
  games.  Christopher Salomon provided most of the technical details 
  used in creating this class.  A good description of the Supercharger
  is provided in the Cuttle Cart's manual.

  The Supercharger has four 2K banks.  There are three banks of RAM 
  and one bank of ROM.  All 6K of the RAM can be read and written.
*/
namespace BizHawk.Emulation.Cores.Atari.Atari2600
{
	internal class mAR : MapperBase
	{
		private int _bank4k;
		private ByteBuffer _ram = new ByteBuffer(6144);

		public mAR()
		{
			throw new NotImplementedException();
		}

		public override bool HasCartRam
		{
			get { return true; }
		}

		public override ByteBuffer CartRam
		{
			get { return _ram; }
		}

		public override void SyncState(Serializer ser)
		{
			base.SyncState(ser);
			ser.Sync("bank4k", ref _bank4k);
			ser.Sync("ram", ref _ram);
		}

		public override void HardReset()
		{
			_bank4k = 0;
			_ram = new ByteBuffer(6144);
			base.HardReset();
		}

		public override void Dispose()
		{
			base.Dispose();
			_ram.Dispose();
		}

		private byte ReadMem(ushort addr, bool peek)
		{
			if (!peek)
			{
				Address(addr);
			}

			if (addr < 0x1000)
			{
				return base.ReadMemory(addr);
			}

			return Core.Rom[(_bank4k << 12) + (addr & 0xFFF)];
		}

		public override byte ReadMemory(ushort addr)
		{
			return ReadMem(addr, false);
		}

		public override byte PeekMemory(ushort addr)
		{
			return ReadMem(addr, true);
		}

		private void Address(ushort addr)
		{
			if (addr == 0x1FF8)
			{
				_bank4k = 0;
			}
			else if (addr == 0x1FF9)
			{
				_bank4k = 1;
			}
		}
	}
}
