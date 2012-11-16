﻿//http://wiki.superfamicom.org/snes/show/Backgrounds
//http://board.zsnes.com/phpBB3/viewtopic.php?f=10&t=13029&start=75 yoshis island offset per tile demos. and other demos of advanced modes
//but we wont worry about offset per tile modes here.

//helpful detailed reg list
//http://wiki.superfamicom.org/snes/show/Registers

using System;
using System.Linq;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Collections.Generic;

namespace BizHawk.Emulation.Consoles.Nintendo.SNES
{

	public unsafe class SNESGraphicsDecoder
	{

		public class PaletteSelection
		{
			public PaletteSelection() { }
			public PaletteSelection(int start, int size)
			{
				this.start = start;
				this.size = size;
			}
			public int start, size;
		}

		public struct Dimensions
		{
			public Dimensions(int w, int h) { Width = w; Height = h; }
			public int Width, Height;
			public override string ToString()
			{
				return string.Format("{0}x{1}", Width, Height);
			}
		}

		public enum ScreenSize
		{
			AAAA_32x32 = 0, ABAB_64x32 = 1, AABB_32x64 = 2, ABCD_64x64 = 3,
			Hacky_1x1 = 4,
		}

		public static Dimensions SizeInTilesForBGSize(ScreenSize size)
		{
			if (size == ScreenSize.Hacky_1x1) return new Dimensions(1, 1);
			var ret = SizeInBlocksForBGSize(size);
			ret.Width *= 32;
			ret.Height *= 32;
			return ret;
		}

		public static Dimensions SizeInBlocksForBGSize(ScreenSize size)
		{
			switch (size)
			{
				case ScreenSize.AAAA_32x32: return new Dimensions(1, 1);
				case ScreenSize.ABAB_64x32: return new Dimensions(2, 1);
				case ScreenSize.AABB_32x64: return new Dimensions(1, 2);
				case ScreenSize.ABCD_64x64: return new Dimensions(2, 2);
				default: throw new Exception();
			}
		}

		public class BGInfo
		{
			/// <summary>
			/// Is the layer even enabled?
			/// </summary>
			public bool Enabled { get { return Bpp != 0; } }

			/// <summary>
			/// screen and tiledata register values
			/// </summary>
			public int SCADDR, TDADDR;

			/// <summary>
			/// SCSIZE register
			/// </summary>
			public int SCSIZE;

			/// <summary>
			/// which Mode this BG came from
			/// </summary>
			public int Mode;

			/// <summary>
			/// the address of the screen data
			/// </summary>
			public int ScreenAddr { get { return SCADDR << 9; } }

			/// <summary>
			/// the address of the tile data
			/// </summary>
			public int TiledataAddr { get { return TDADDR << 13; } }

			/// <summary>
			/// Screen size (shape, really.)
			/// </summary>
			public ScreenSize ScreenSize { get { return (ScreenSize)SCSIZE; } }

			/// <summary>
			/// the BPP of the BG, as derived from the current mode
			/// </summary>
			public int Bpp;

			/// <summary>
			/// value of the tilesize register; 1 implies 16x16 tiles
			/// </summary>
			public int TILESIZE;

			/// <summary>
			/// TileSize; 8 or 16
			/// </summary>
			public int TileSize { get { return TILESIZE == 1 ? 16 : 8; } }

			/// <summary>
			/// The size of the layer, in tiles
			/// </summary>
			public Dimensions ScreenSizeInTiles { get { return SizeInTilesForBGSize(ScreenSize); } }

			/// <summary>
			/// The size of the layer, in pixels. This has factored in the selection of 8x8 or 16x16 tiles
			/// </summary>
			public Dimensions ScreenSizeInPixels
			{
				get
				{
					return new Dimensions(ScreenSizeInTiles.Width * TileSize, ScreenSizeInTiles.Height * TileSize);
				}
			}

			/// <summary>
			/// returns information about what colors could possibly be used for this bg
			/// </summary>
			public PaletteSelection PaletteSelection;

		}

		public class BGInfos
		{
			BGInfo[] bgs = new BGInfo[4] { new BGInfo(), new BGInfo(), new BGInfo(), new BGInfo() };
			public BGInfo BG1 { get { return bgs[0]; } }
			public BGInfo BG2 { get { return bgs[1]; } }
			public BGInfo BG3 { get { return bgs[2]; } }
			public BGInfo BG4 { get { return bgs[3]; } }
			public BGInfo this[int index] { get { return bgs[index - 1]; } }
		}

		public class ModeInfo
		{
			/// <summary>
			/// the mode number, i.e. Mode 7
			/// </summary>
			public int MODE;
		}

		public class ScreenInfo
		{
			public BGInfos BG = new BGInfos();

			public ModeInfo Mode = new ModeInfo();

			public bool SETINI_Mode7ExtBG { private set; get; }
			public bool SETINI_HiRes { private set; get; }
			public bool SETINI_Overscan { private set; get; }
			public bool SETINI_ObjInterlace { private set; get; }
			public bool SETINI_ScreenInterlace { private set; get; }

			public int CGWSEL_ColorMask { private set; get; }
			public int CGWSEL_ColorSubMask { private set; get; }
			public int CGWSEL_AddSubMode { private set; get; }
			public bool CGWSEL_DirectColor { private set; get; }

			public static ScreenInfo GetScreenInfo()
			{
				var si = new ScreenInfo();

				si.SETINI_Mode7ExtBG = LibsnesDll.snes_peek_logical_register(LibsnesDll.SNES_REG.SETINI_MODE7_EXTBG) == 1;
				si.SETINI_HiRes = LibsnesDll.snes_peek_logical_register(LibsnesDll.SNES_REG.SETINI_HIRES) == 1;
				si.SETINI_Overscan = LibsnesDll.snes_peek_logical_register(LibsnesDll.SNES_REG.SETINI_OVERSCAN) == 1;
				si.SETINI_ObjInterlace = LibsnesDll.snes_peek_logical_register(LibsnesDll.SNES_REG.SETINI_OBJ_INTERLACE) == 1;
				si.SETINI_ScreenInterlace = LibsnesDll.snes_peek_logical_register(LibsnesDll.SNES_REG.SETINI_SCREEN_INTERLACE) == 1;

				si.CGWSEL_ColorMask = LibsnesDll.snes_peek_logical_register(LibsnesDll.SNES_REG.CGWSEL_COLORMASK);
				si.CGWSEL_ColorSubMask = LibsnesDll.snes_peek_logical_register(LibsnesDll.SNES_REG.CGWSEL_COLORSUBMASK);
				si.CGWSEL_AddSubMode = LibsnesDll.snes_peek_logical_register(LibsnesDll.SNES_REG.CGWSEL_ADDSUBMODE);
				si.CGWSEL_DirectColor = LibsnesDll.snes_peek_logical_register(LibsnesDll.SNES_REG.CGWSEL_DIRECTCOLOR) == 1;

				si.Mode.MODE = LibsnesDll.snes_peek_logical_register(LibsnesDll.SNES_REG.BG_MODE);
				si.BG.BG1.Bpp = ModeBpps[si.Mode.MODE, 0];
				si.BG.BG2.Bpp = ModeBpps[si.Mode.MODE, 1];
				si.BG.BG3.Bpp = ModeBpps[si.Mode.MODE, 2];
				si.BG.BG4.Bpp = ModeBpps[si.Mode.MODE, 3];

				if (si.Mode.MODE == 7 && si.SETINI_Mode7ExtBG)
					si.BG.BG2.Bpp = 7;

				si.BG.BG1.TILESIZE = LibsnesDll.snes_peek_logical_register(LibsnesDll.SNES_REG.BG1_TILESIZE);
				si.BG.BG2.TILESIZE = LibsnesDll.snes_peek_logical_register(LibsnesDll.SNES_REG.BG2_TILESIZE);
				si.BG.BG3.TILESIZE = LibsnesDll.snes_peek_logical_register(LibsnesDll.SNES_REG.BG3_TILESIZE);
				si.BG.BG4.TILESIZE = LibsnesDll.snes_peek_logical_register(LibsnesDll.SNES_REG.BG4_TILESIZE);

				si.BG.BG1.SCSIZE = LibsnesDll.snes_peek_logical_register(LibsnesDll.SNES_REG.BG1_SCSIZE);
				si.BG.BG2.SCSIZE = LibsnesDll.snes_peek_logical_register(LibsnesDll.SNES_REG.BG2_SCSIZE);
				si.BG.BG3.SCSIZE = LibsnesDll.snes_peek_logical_register(LibsnesDll.SNES_REG.BG3_SCSIZE);
				si.BG.BG4.SCSIZE = LibsnesDll.snes_peek_logical_register(LibsnesDll.SNES_REG.BG4_SCSIZE);
				si.BG.BG1.SCADDR = LibsnesDll.snes_peek_logical_register(LibsnesDll.SNES_REG.BG1_SCADDR);
				si.BG.BG2.SCADDR = LibsnesDll.snes_peek_logical_register(LibsnesDll.SNES_REG.BG2_SCADDR);
				si.BG.BG3.SCADDR = LibsnesDll.snes_peek_logical_register(LibsnesDll.SNES_REG.BG3_SCADDR);
				si.BG.BG4.SCADDR = LibsnesDll.snes_peek_logical_register(LibsnesDll.SNES_REG.BG4_SCADDR);
				si.BG.BG1.TDADDR = LibsnesDll.snes_peek_logical_register(LibsnesDll.SNES_REG.BG1_TDADDR);
				si.BG.BG2.TDADDR = LibsnesDll.snes_peek_logical_register(LibsnesDll.SNES_REG.BG2_TDADDR);
				si.BG.BG3.TDADDR = LibsnesDll.snes_peek_logical_register(LibsnesDll.SNES_REG.BG3_TDADDR);
				si.BG.BG4.TDADDR = LibsnesDll.snes_peek_logical_register(LibsnesDll.SNES_REG.BG4_TDADDR);

				for (int i = 1; i <= 4; i++)
					si.BG[i].Mode = si.Mode.MODE;

				//determine which colors each BG could use
				switch (si.Mode.MODE)
				{
					case 0:
						si.BG.BG1.PaletteSelection = new PaletteSelection(0, 32);
						si.BG.BG2.PaletteSelection = new PaletteSelection(32, 32);
						si.BG.BG3.PaletteSelection = new PaletteSelection(64, 32);
						si.BG.BG4.PaletteSelection = new PaletteSelection(96, 32);
						break;
					case 1:
						si.BG.BG1.PaletteSelection = new PaletteSelection(0, 128);
						si.BG.BG2.PaletteSelection = new PaletteSelection(0, 128);
						si.BG.BG3.PaletteSelection = new PaletteSelection(0, 32);
						si.BG.BG4.PaletteSelection = new PaletteSelection(0, 0);
						break;
					case 2:
						si.BG.BG1.PaletteSelection = new PaletteSelection(0, 128);
						si.BG.BG2.PaletteSelection = new PaletteSelection(0, 128);
						si.BG.BG3.PaletteSelection = new PaletteSelection(0, 0);
						si.BG.BG4.PaletteSelection = new PaletteSelection(0, 0);
						break;
					case 3:
						si.BG.BG1.PaletteSelection = new PaletteSelection(0, 256);
						si.BG.BG2.PaletteSelection = new PaletteSelection(0, 128);
						si.BG.BG3.PaletteSelection = new PaletteSelection(0, 0);
						si.BG.BG4.PaletteSelection = new PaletteSelection(0, 0);
						break;
					case 4:
						si.BG.BG1.PaletteSelection = new PaletteSelection(0, 256);
						si.BG.BG2.PaletteSelection = new PaletteSelection(0, 32);
						si.BG.BG3.PaletteSelection = new PaletteSelection(0, 0);
						si.BG.BG4.PaletteSelection = new PaletteSelection(0, 0);
						break;
					case 5:
						si.BG.BG1.PaletteSelection = new PaletteSelection(0, 128);
						si.BG.BG2.PaletteSelection = new PaletteSelection(0, 32);
						si.BG.BG3.PaletteSelection = new PaletteSelection(0, 0);
						si.BG.BG4.PaletteSelection = new PaletteSelection(0, 0);
						break;
					case 6:
						si.BG.BG1.PaletteSelection = new PaletteSelection(0, 128);
						si.BG.BG2.PaletteSelection = new PaletteSelection(0, 32);
						si.BG.BG3.PaletteSelection = new PaletteSelection(0, 0);
						si.BG.BG4.PaletteSelection = new PaletteSelection(0, 0);
						break;
					case 7:
						si.BG.BG1.PaletteSelection = new PaletteSelection(0, 256);
						si.BG.BG2.PaletteSelection = new PaletteSelection(0, 128);
						si.BG.BG3.PaletteSelection = new PaletteSelection(0, 0);
						si.BG.BG4.PaletteSelection = new PaletteSelection(0, 0);
						break;

				}

				return si;
			}
		}

		static int[,] ModeBpps = new[,] {
				{2,2,2,2},
				{4,4,2,0},
				{4,4,0,0},
				{8,4,0,0},
				{8,2,0,0},
				{4,2,0,0},
				{4,0,0,0},
				{8,0,0,0},
				{8,7,0,0}
			};


		public ScreenInfo ScanScreenInfo()
		{
			return ScreenInfo.GetScreenInfo();
		}


		//the same basic color table that libsnes uses to convert from snes 555 to rgba32
		public static int[] colortable;
		static int[] directColorTable = new int[256]; //8bpp gfx -> rgb555
		static SNESGraphicsDecoder()
		{
			//make directColorTable
			for (int i = 0; i < 256; i++)
			{
				int r = i & 7;
				int g = (i >> 3) & 7;
				int b = (i >> 6) & 3;
				r <<= 2;
				g <<= 2;
				b <<= 3;
				int color = (b << 10) | (g << 5) | r;
				directColorTable[i] = color;
			}
			colortable = SnesColors.GetLUT(SnesColors.ColorType.Bizhawk);
		}

		byte* vram;
		public ushort* cgram, vram16;
		public SNESGraphicsDecoder()
		{
			IntPtr block = LibsnesDll.snes_get_memory_data(LibsnesDll.SNES_MEMORY.VRAM);
			vram = (byte*)block.ToPointer();
			vram16 = (ushort*)vram;
			block = LibsnesDll.snes_get_memory_data(LibsnesDll.SNES_MEMORY.CGRAM);
			cgram = (ushort*)block.ToPointer();
		}

		public struct TileEntry
		{
			public ushort tilenum;
			public byte palette;
			public TileEntryFlags flags;
		}

		public enum TileEntryFlags : byte
		{
			Priority = 1, Horz = 2, Vert = 4,
		}

		/// <summary>
		/// decodes a mode7 BG. youll still need to paletteize and colorize it.
		/// </summary>
		public void DecodeMode7BG(int* screen, int stride, bool extBg)
		{
			int[] tileCache = _tileCache[extBg?17:7];
			for (int ty = 0, tidx = 0; ty < 128; ty++)
			{
				for (int tx = 0; tx < 128; tx++, tidx++)
				{
					int tileEntry = vram[tidx * 2];
					int src = tileEntry * 64;
					for (int py = 0, pix=src; py < 8; py++)
					{
						for (int px = 0; px < 8; px++, pix++)
						{
							int dst = (ty * 8 + py) * stride + (tx * 8 + px);
							int srcData = tileCache[pix];
							screen[dst] = srcData;
						}
					}
				}
			}
		}

		/// <summary>
		/// returns a tilemap which might be resized into 8x8 physical tiles if the 16x16 logical tilesize is specified
		/// </summary>
		//TileEntry[] AdaptTilemap(TileEntry[] map8x8, int tilesWide, int tilesTall, int tilesize)
		//{
		//  if (tilesize == 8) return map8x8;
		//  int numTiles = tilesWide * tilesTall;
		//  var ret = new TileEntry[numTiles * 4];
		//  for(int y=0;y<tilesTall;y++)
		//  {
		//    for (int x = 0; x < tilesWide; x++)
		//    {
		//      int si = tilesWide * y + x;
		//      int di = tilesHigh 
		//      for (int tx = 0; tx < 2; tx++)
		//      {
		//        for (int ty = 0; ty < 2; ty++)
		//        {
		//        }
		//      }
		//    }
		//  }
		//}

		/// <summary>
		/// decodes a BG. youll still need to paletteize and colorize it.
		/// someone else has to take care of calculating the starting color from the mode and layer number.
		/// </summary>
		public void DecodeBG(int* screen, int stride, TileEntry[] map, int tiledataBaseAddr, ScreenSize size, int bpp, int tilesize, int paletteStart)
		{
			int ncolors = 1 << bpp;

			int[] tileBuf = new int[16*16];
			var dims = SizeInTilesForBGSize(size);
			int count8x8 = tilesize / 8;
			int tileSizeBytes = 8 * bpp;
			int baseTileNum = tiledataBaseAddr / tileSizeBytes;
			int[] tileCache = _tileCache[bpp];
			int tileCacheMask = tileCache.Length - 1;

			int screenWidth = dims.Width * count8x8 * 8;

			for (int mty = 0; mty < dims.Height; mty++)
			{
				for (int mtx = 0; mtx < dims.Width; mtx++)
				{
					for (int tx = 0; tx < count8x8; tx++)
					{
						for (int ty = 0; ty < count8x8; ty++)
						{
							int mapIndex = mty * dims.Width + mtx;
							var te = map[mapIndex];
							int tileNum = te.tilenum + tx + ty * 16 + baseTileNum;
							int srcOfs = tileNum * 64;
							for (int i = 0, y = 0; y < 8; y++)
							{
								for (int x = 0; x < 8; x++, i++)
								{
									int px = x;
									int py = y;
									if ((te.flags & TileEntryFlags.Horz) != 0) px = 7 - x;
									if ((te.flags & TileEntryFlags.Vert) != 0) py = 7 - y;
									int dstX = (mtx * count8x8 + tx) * 8 + px;
									int dstY = (mty * count8x8 + ty) * 8 + py;
									int dstOfs = dstY * stride + dstX;
									int color = tileCache[srcOfs & tileCacheMask];
									srcOfs++;
									color += te.palette * ncolors;
									color += paletteStart;
									screen[dstOfs] = color;
								}
							}
						}
					}
				}
			}
		}

		/// <summary>
		/// fetches a tilemap. this is simple; apparently only the screen size (shape) is a factor (not the tile size)
		/// </summary>
		public TileEntry[] FetchTilemap(int addr, ScreenSize size)
		{
			var blockDims = SizeInBlocksForBGSize(size);
			int blocksw = blockDims.Width;
			int blocksh = blockDims.Height;
			int width = blockDims.Width * 32;
			int height = blockDims.Height * 32;
			TileEntry[] buf = new TileEntry[width*height];

			for (int by = 0; by < blocksh; by++)
			{
				for (int bx = 0; bx < blocksw; bx++)
				{
					for (int y = 0; y < 32; y++)
					{
						for (int x = 0; x < 32; x++)
						{
							int idx = (by * 32 + y) * width + bx * 32 + x;
							ushort entry = *(ushort*)(vram + addr);
							buf[idx].tilenum = (ushort)(entry & 0x3FF);
							buf[idx].palette = (byte)((entry >> 10) & 7);
							buf[idx].flags = (TileEntryFlags)((entry >> 13) & 7);
							addr += 2;
						}
					}
				}
			}

			return buf;
		}

		//TODO - paletteize and colorize could be in one step, for more speed
		public void Paletteize(int* buf, int offset, int startcolor, int numpixels)
		{
			for (int i = 0; i < numpixels; i++)
			{
				buf[offset + i] = cgram[startcolor + buf[offset + i]] & 0x7FFF; //unfortunate that we have to mask this here.. maybe do it in a more optimal spot when we port it to c++
			}
		}
		public void Colorize(int* buf, int offset, int numpixels)
		{
			for (int i = 0; i < numpixels; i++)
			{
				buf[offset + i] = colortable[491520 + buf[offset + i]];
			}
		}

		int[][] _tileCache = new int[18][];

		/// <summary>
		/// Caches all tiles at the 2bpp, 4bpp, and 8bpp decoded states.
		/// we COULD defer this til we need it, you know. sort of a cool idea, not too hard
		/// </summary>
		public void CacheTiles()
		{
			//generate 2bpp tiles
			int numtiles = 8192;
			int[] tiles = new int[8 * 8 * numtiles];
			_tileCache[2] = tiles;
			for (int i = 0; i < numtiles; i++)
			{
				Decode8x8x2bpp(tiles, i * 64, 16 * i, 8);
			}

			//merge 2bpp tiles into 4bpp and 8bpp
			CacheTiles_Merge(2);
			CacheTiles_Merge(4);
			CacheTilesMode7();
			CacheTilesMode7ExtBg();
		}

		public void CacheTilesMode7()
		{
			int numtiles = 256;
			int[] tiles = new int[8 * 8 * numtiles];
			_tileCache[7] = tiles;
			for (int i = 0, j=0; i < numtiles; i++)
			{
				for (int y = 0; y < 8; y++)
					for (int x = 0; x < 8; x++, j++)
						tiles[j] = vram[j * 2 + 1];
			}
		}

		//not being used.. do we need it?
		public int[] GetCachedTile(int bpp, int tilenum)
		{
			int[] ret = new int[8 * 8];
			int idx = tilenum * 64;
			for (int i = 0; i < 64; i++)
				ret[i] = _tileCache[bpp][idx + i];
			return ret;
		}

		void CacheTilesMode7ExtBg()
		{
			int numtiles = 256;
			int[] tiles = new int[8 * 8 * numtiles];
			_tileCache[17] = tiles;
			int[] mode7tiles = _tileCache[7];
			int numPixels = numtiles*8*8;
			for (int i = 0; i < numPixels; i++)
				tiles[i] = mode7tiles[i] & 0x7F;
		}


		/// <summary>
		/// merges one type of tiles with another to create the higher-order bitdepth.
		/// TODO - templateize this when we change it to c++
		/// </summary>
		void CacheTiles_Merge(int fromBpp)
		{
			int toBpp = fromBpp * 2;
			int shift = fromBpp;
			int numtiles = 8192 / toBpp;
			int[] tilesDst = new int[8 * 8 * numtiles];
			_tileCache[toBpp] = tilesDst;
			int[] tilesSrc = _tileCache[fromBpp];

			for (int i = 0; i < numtiles; i++)
			{
				int srcAddr = i * 128;
				int dstAddr = i * 64;
				for (int p = 0; p < 64; p++)
				{
					int tileA = tilesSrc[srcAddr + p];
					int tileB = tilesSrc[srcAddr + p + 64];
					tilesDst[dstAddr + p] = tileA | (tileB << shift);
				}
			}
		}

		/// <summary>
		/// decodes an 8x8 tile to a linear framebuffer type thing. fundamental unit of tile decoding.
		/// </summary>
		public void Decode8x8x2bpp(int[] buf, int offset, int addr, int stride=8)
		{
			for (int y = 0; y < 8; y++)
			{
				byte val = vram[addr + 1];
				for (int x = 0; x < 8; x++) buf[offset + y * stride + x] = val >> (7 - x) & 1;
				val = vram[addr + 0];
				for (int x = 0; x < 8; x++) buf[offset + y * stride + x] = (buf[offset + y * stride + x] << 1) | (val >> (7 - x) & 1);
				addr += 2;
			}
		}

		/// <summary>
		/// renders the mode7 tiles to a screen with the predefined size.
		/// </summary>
		public void RenderMode7TilesToScreen(int* screen, int stride, bool ext, bool directColor)
		{
			int numTiles = 256;
			int tilesWide = 16;
			int[] tilebuf = _tileCache[ext?17:7];
			for (int i = 0; i < numTiles; i++)
			{
				int ty = i / tilesWide;
				int tx = i % tilesWide;
				int dstOfs = (ty * 8) * stride + tx * 8;
				int srcOfs = i * 64;
				for (int y = 0, p = 0; y < 8; y++)
				{
					for (int x = 0; x < 8; x++, p++)
					{
						screen[dstOfs + y * stride + x] = tilebuf[srcOfs + p];
					}
				}
			}

			int numPixels = numTiles * 8 * 8;
			if (directColor) DirectColorify(screen, numPixels);
			else Paletteize(screen, 0, 0, numPixels);
			Colorize(screen, 0, numPixels);
		}

		/// <summary>
		/// renders the tiles to a screen of the crudely specified size.
		/// we might need 16x16 unscrambling and some other perks here eventually.
		/// provide a start color to use as the basis for the palette
		/// </summary>
		public void RenderTilesToScreen(int* screen, int tilesWide, int tilesTall, int stride, int bpp, int startcolor, int startTile = 0, int numTiles = -1)
		{
			if(numTiles == -1)
				numTiles = 8192 / bpp;
			int[] tilebuf = _tileCache[bpp];
			for (int i = 0; i < numTiles; i++)
			{
				int tnum = startTile + i;
				int ty = i / tilesWide;
				int tx = i % tilesWide;
				int dstOfs = (ty * 8) * stride + tx * 8;
				int srcOfs = tnum * 64;
				for (int y = 0,p=0; y < 8; y++)
					for (int x = 0; x < 8; x++,p++)
					{
						screen[dstOfs+y*stride+x] = tilebuf[srcOfs + p];
					}
			}

			int numPixels = numTiles * 8 * 8;
			Paletteize(screen, 0, startcolor, numPixels);
			Colorize(screen, 0, numPixels);
		}

		public int Colorize(int rgb555)
		{
			return colortable[491520 + rgb555];
		}

		/// <summary>
		/// returns the current palette, transformed into an int array, for more convenience
		/// </summary>
		public int[] GetPalette()
		{
			var ret = new int[256];
			for (int i = 0; i < 256; i++)
				ret[i] = cgram[i] & 0x7FFF;
			return ret;
		}

		public void DirectColorify(int* screen, int numPixels)
		{
			for (int i = 0; i < numPixels; i++)
				screen[i] = directColorTable[screen[i]];

		}
	
	
	} //class SNESGraphicsDecoder
} //namespace
