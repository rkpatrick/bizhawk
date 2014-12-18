﻿using System;
using System.Linq;
using System.Text;
using System.IO;
using System.Collections.Generic;

//http://www.pctechguide.com/iso-9660-data-format-for-cds-cd-roms-cd-rs-and-cd-rws
//http://linux.die.net/man/1/cue2toc

//http://cdemu.sourceforge.net/project.php#sf

//apparently cdrdao is the ultimate linux tool for doing this stuff but it doesnt support DAO96 (or other DAO modes) that would be necessary to extract P-Q subchannels
//(cdrdao only supports R-W)

//here is a featureset list of windows cd burning programs (useful for cuesheet compatibility info)
//http://www.dcsoft.com/cue_mastering_progs.htm

//good
//http://linux-sxs.org/bedtime/cdapi.html
//http://en.wikipedia.org/wiki/Track_%28CD%29
//http://docs.google.com/viewer?a=v&q=cache:imNKye05zIEJ:www.13thmonkey.org/documentation/SCSI/mmc-r10a.pdf+q+subchannel+TOC+format&hl=en&gl=us&pid=bl&srcid=ADGEEShtYqlluBX2lgxTL3pVsXwk6lKMIqSmyuUCX4RJ3DntaNq5vI2pCvtkyze-fumj7vvrmap6g1kOg5uAVC0IxwU_MRhC5FB0c_PQ2BlZQXDD7P3GeNaAjDeomelKaIODrhwOoFNb&sig=AHIEtbRXljAcFjeBn3rMb6tauHWjSNMYrw
//r:\consoles\~docs\yellowbook
//http://digitalx.org/cue-sheet/examples/
//

//"qemu cdrom emulator"
//http://www.koders.com/c/fid7171440DEC7C18B932715D671DEE03743111A95A.aspx
 
//less good
//http://www.cyberciti.biz/faq/getting-volume-information-from-cds-iso-images/
//http://www.cims.nyu.edu/cgi-systems/man.cgi?section=7I&topic=cdio

//ideas:
/*
 * do some stuff asynchronously. for example, decoding mp3 sectors.
 * keep a list of 'blobs' (giant bins or decoded wavs likely) which can reference the disk
 * keep a list of sectors and the blob/offset from which they pull -- also whether the sector is available
 * if it is not available and something requests it then it will have to block while that sector gets generated
 * perhaps the blobs know how to resolve themselves and the requested sector can be immediately resolved (priority boost)
 * mp3 blobs should be hashed and dropped in %TEMP% as a wav decode
*/

//here is an MIT licensed C mp3 decoder
//http://core.fluendo.com/gstreamer/src/gst-fluendo-mp3/

/*information on saturn TOC and session data structures is on pdf page 58 of System Library User's Manual;
 * as seen in yabause, there are 1000 u32s in this format:
 * Ctrl[4bit] Adr[4bit] StartFrameAddressFAD[24bit] (nonexisting tracks are 0xFFFFFFFF)
 * Followed by Fist Track Information, Last Track Information..
 * Ctrl[4bit] Adr[4bit] FirstTrackNumber/LastTrackNumber[8bit] and then some stuff I dont understand
 * ..and Read Out Information:
 * Ctrl[4bit] Adr[4bit] ReadOutStartFrameAddress[24bit]
 * 
 * Also there is some stuff about FAD of sessions.
 * This should be generated by the saturn core, but we need to make sure we pass down enough information to do it
*/

//2048 bytes packed into 2352: 
//12 bytes sync(00 ff ff ff ff ff ff ff ff ff ff 00)
//3 bytes sector address (min+A0),sec,frac //does this correspond to ccd `point` field in the TOC entries?
//sector mode byte (0: silence; 1: 2048Byte mode (EDC,ECC,CIRC), 2: mode2 (could be 2336[vanilla mode2], 2048[xa mode2 form1], 2324[xa mode2 form2])
//cue sheets may use mode1_2048 (and the error coding needs to be regenerated to get accurate raw data) or mode1_2352 (the entire sector is present)
//audio is a different mode, seems to be just 2352 bytes with no sync, header or error correction. i guess the CIRC error correction is still there

namespace BizHawk.Emulation.DiscSystem
{
	public partial class Disc : IDisposable
	{
		/// <summary>
		/// The raw TOC entries found in the lead-in track.
		/// </summary>
		public List<RawTOCEntry> RawTOCEntries = new List<RawTOCEntry>();

		/// <summary>
		/// The DiscTOCRaw corresponding to the RawTOCEntries
		/// </summary>
		public DiscTOCRaw TOCRaw;

		/// <summary>
		/// The DiscStructure corresponding the the TOCRaw
		/// </summary>
		public DiscStructure Structure;

		/// <summary>
		/// The blobs mounted by this disc for supplying binary content
		/// </summary>
		public List<IBlob> Blobs = new List<IBlob>();

		/// <summary>
		/// The sectors on the disc
		/// </summary>
		public List<SectorEntry> Sectors = new List<SectorEntry>();

		public Disc()
		{
		}

		public void Dispose()
		{
			foreach (var blob in Blobs)
			{
				blob.Dispose();
			}
		}

		void FromIsoPathInternal(string isoPath)
		{
			//make a fake cue file to represent this iso file
			const string isoCueWrapper = @"
FILE ""xarp.barp.marp.farp"" BINARY
  TRACK 01 MODE1/2048
    INDEX 01 00:00:00
";

			string cueDir = String.Empty;
			var cue = new Cue();
			CueFileResolver["xarp.barp.marp.farp"] = isoPath;
			cue.LoadFromString(isoCueWrapper);
			FromCueInternal(cue, cueDir, new CueBinPrefs());
		}

		
		public CueBin DumpCueBin(string baseName, CueBinPrefs prefs)
		{
			if (Structure.Sessions.Count > 1)
				throw new NotSupportedException("can't dump cue+bin with more than 1 session yet");

			CueBin ret = new CueBin();
			ret.baseName = baseName;
			ret.disc = this;

			if (!prefs.OneBlobPerTrack)
			{
				//this is the preferred mode of dumping things. we will always write full sectors.
				string cue = new CUE_Format().GenerateCUE_OneBin(Structure,prefs);
				var bfd = new CueBin.BinFileDescriptor {name = baseName + ".bin"};
				ret.cue = string.Format("FILE \"{0}\" BINARY\n", bfd.name) + cue;
				ret.bins.Add(bfd);
				bfd.SectorSize = 2352;

				//skip the mandatory track 1 pregap! cue+bin files do not contain it
				for (int i = 150; i < Structure.LengthInSectors; i++)
				{
					bfd.abas.Add(i);
					bfd.aba_zeros.Add(false);
				}
			}
			else
			{
				//we build our own cue here (unlike above) because we need to build the cue and the output data at the same time
				StringBuilder sbCue = new StringBuilder();
				
				for (int i = 0; i < Structure.Sessions[0].Tracks.Count; i++)
				{
					var track = Structure.Sessions[0].Tracks[i];
					var bfd = new CueBin.BinFileDescriptor
						{
							name = baseName + string.Format(" (Track {0:D2}).bin", track.Number),
							SectorSize = Cue.BINSectorSizeForTrackType(track.TrackType)
						};
					ret.bins.Add(bfd);
					int aba = 0;

					//skip the mandatory track 1 pregap! cue+bin files do not contain it
					if (i == 0) aba = 150;

					for (; aba < track.LengthInSectors; aba++)
					{
						int thisaba = track.Indexes[0].aba + aba;
						bfd.abas.Add(thisaba);
						bfd.aba_zeros.Add(false);
					}
					sbCue.AppendFormat("FILE \"{0}\" BINARY\n", bfd.name);

					sbCue.AppendFormat("  TRACK {0:D2} {1}\n", track.Number, Cue.TrackTypeStringForTrackType(track.TrackType));
					foreach (var index in track.Indexes)
					{
						int x = index.aba - track.Indexes[0].aba;
						if (index.Number == 0 && index.aba == track.Indexes[1].aba)
						{
						    //dont emit index 0 when it is the same as index 1, it is illegal for some reason
						}
						//else if (i==0 && index.num == 0)
						//{
						//    //don't generate the first index, it is illogical
						//}
						else
						{
							//track 1 included the lead-in at the beginning of it. sneak past that.
							//if (i == 0) x -= 150;
							sbCue.AppendFormat("    INDEX {0:D2} {1}\n", index.Number, new Timestamp(x).Value);
						}
					}
				}

				ret.cue = sbCue.ToString();
			}

			return ret;
		}


		public static Disc FromCuePath(string cuePath, CueBinPrefs prefs)
		{
			var ret = new Disc();
			ret.FromCuePathInternal(cuePath, prefs);
			ret.Structure.Synthesize_TOCPointsFromSessions();
			ret.Synthesize_SubcodeFromStructure();
			ret.Synthesize_TOCRawFromStructure();
			return ret;
		}

		public static Disc FromCCDPath(string ccdPath)
		{
			CCD_Format ccdLoader = new CCD_Format();
			return ccdLoader.LoadCCDToDisc(ccdPath);
		}

		/// <summary>
		/// THIS HASNT BEEN TESTED IN A LONG TIME. DOES IT WORK?
		/// </summary>
		public static Disc FromIsoPath(string isoPath)
		{
			var ret = new Disc();
			ret.FromIsoPathInternal(isoPath);
			ret.Structure.Synthesize_TOCPointsFromSessions();
			ret.Synthesize_SubcodeFromStructure();
			return ret;
		}

		/// <summary>
		/// Synthesizes a crudely estimated TOCRaw from the disc structure.
		/// </summary>
		public void Synthesize_TOCRawFromStructure()
		{
			TOCRaw = new DiscTOCRaw();
			TOCRaw.FirstRecordedTrackNumber = 1;
			TOCRaw.LastRecordedTrackNumber = Structure.Sessions[0].Tracks.Count;
			int lastEnd = 0;
			for (int i = 0; i < Structure.Sessions[0].Tracks.Count; i++)
			{
				var track = Structure.Sessions[0].Tracks[i];
				TOCRaw.TOCItems[i + 1].Control = track.Control;
				TOCRaw.TOCItems[i + 1].Exists = true;
				//TOCRaw.TOCItems[i + 1].LBATimestamp = new Timestamp(track.Start_ABA - 150); //AUGH. see comment in Start_ABA
				//TOCRaw.TOCItems[i + 1].LBATimestamp = new Timestamp(track.Indexes[1].LBA);  //ZOUNDS!
				TOCRaw.TOCItems[i + 1].LBATimestamp = new Timestamp(track.Indexes[1].LBA + 150); //WHATEVER, I DONT KNOW. MAKES IT MATCH THE CCD, BUT THERES MORE PROBLEMS
				lastEnd = track.LengthInSectors + track.Indexes[1].LBA;
			}

			TOCRaw.LeadoutTimestamp = new Timestamp(lastEnd);
		}

		/// <summary>
		/// Creates the subcode (really, just subchannel Q) for this disc from its current TOC.
		/// Depends on the TOCPoints existing in the structure
		/// TODO - do we need a fully 0xFF P-subchannel for PSX?
		/// </summary>
		void Synthesize_SubcodeFromStructure()
		{
			int aba = 0;
			int dpIndex = 0;

			//NOTE: discs may have subcode which is nonsense or possibly not recoverable from a sensible disc structure.
			//but this function does what it says.

			//SO: heres the main idea of how this works.
			//we have the Structure.Points (whose name we dont like) which is a list of sectors where the tno/index changes.
			//So for each sector, we see if we've advanced to the next point.
			//TODO - check if this is synthesized correctly when producing a structure from a TOCRaw
			while (aba < Sectors.Count)
			{
				if (dpIndex < Structure.Points.Count - 1)
				{
					if (aba >= Structure.Points[dpIndex + 1].ABA)
					{
						dpIndex++;
					}
				}
				var dp = Structure.Points[dpIndex];

				var se = Sectors[aba];

				EControlQ control = dp.Track.Control;
				bool pause = true;
				if (dp.Num != 0)
					pause = false;
				if ((dp.Track.Control & EControlQ.DataUninterrupted)!=0)
					pause = false;
				
				//we always use ADR=1 (mode-1 q block)
				//this could be more sophisticated but it is almost useless for emulation (only useful for catalog/ISRC numbers)
				int adr = 1;

				SubchannelQ sq = new SubchannelQ();
				sq.q_status = SubchannelQ.ComputeStatus(adr, control);
				sq.q_tno = (byte)dp.TrackNum;
				sq.q_index = (byte)dp.IndexNum;

				int track_relative_aba = aba - dp.Track.Indexes[1].aba;
				track_relative_aba = Math.Abs(track_relative_aba);
				Timestamp track_relative_timestamp = new Timestamp(track_relative_aba);
				sq.min = BCD2.FromDecimal(track_relative_timestamp.MIN);
				sq.sec = BCD2.FromDecimal(track_relative_timestamp.SEC);
				sq.frame = BCD2.FromDecimal(track_relative_timestamp.FRAC);
				sq.zero = 0;
				Timestamp absolute_timestamp = new Timestamp(aba);
				sq.ap_min = BCD2.FromDecimal(absolute_timestamp.MIN);
				sq.ap_sec = BCD2.FromDecimal(absolute_timestamp.SEC);
				sq.ap_frame = BCD2.FromDecimal(absolute_timestamp.FRAC);

				var bss = new BufferedSubcodeSector();
				bss.Synthesize_SubchannelQ(ref sq, true);

				//TEST: need this for psx?
				if(pause) bss.Synthesize_SubchannelP(true);

				se.SubcodeSector = bss;

				aba++;
			}
		}

		static byte IntToBCD(int n)
		{
			int ones;
			int tens = Math.DivRem(n,10,out ones);
			return (byte)((tens<<4)|ones);
		}
	}

	/// <summary>
	/// encapsulates a 2 digit BCD number as used various places in the CD specs
	/// </summary>
	public struct BCD2
	{
		/// <summary>
		/// The raw BCD value. you can't do math on this number! but you may be asked to supply it to a game program.
		/// The largest number it can logically contain is 99
		/// </summary>
		public byte BCDValue;

		/// <summary>
		/// The derived decimal value. you can do math on this! the largest number it can logically contain is 99.
		/// </summary>
		public int DecimalValue
		{
			get { return (BCDValue & 0xF) + ((BCDValue >> 4) & 0xF) * 10; }
			set { BCDValue = IntToBCD(value); }
		}

		/// <summary>
		/// makes a BCD2 from a decimal number. don't supply a number > 99 or you might not like the results
		/// </summary>
		public static BCD2 FromDecimal(int d)
		{
			return new BCD2 {DecimalValue = d};
		}


		static byte IntToBCD(int n)
		{
			int ones;
			int tens = Math.DivRem(n, 10, out ones);
			return (byte)((tens << 4) | ones);
		}
	}

	public struct Timestamp
	{
		/// <summary>
		/// creates a timestamp from a string in the form mm:ss:ff
		/// </summary>
		public Timestamp(string value)
		{
			//TODO - could be performance-improved
			MIN = int.Parse(value.Substring(0, 2));
			SEC = int.Parse(value.Substring(3, 2));
			FRAC = int.Parse(value.Substring(6, 2));
			Sector = MIN * 60 * 75 + SEC * 75 + FRAC;
			_value = null;
		}
		public readonly int MIN, SEC, FRAC, Sector;

		public string Value
		{ 
			get
			{
				if (_value != null) return _value;
				return _value = string.Format("{0:D2}:{1:D2}:{2:D2}", MIN, SEC, FRAC);
			}
		}

		string _value;

		/// <summary>
		/// creates timestamp from supplies MSF
		/// </summary>
		public Timestamp(int m, int s, int f)
		{
			MIN = m;
			SEC = s;
			FRAC = f;
			Sector = MIN * 60 * 75 + SEC * 75 + FRAC;
			_value = null;
		}

		/// <summary>
		/// creates timestamp from supplied SectorNumber
		/// </summary>
		public Timestamp(int SectorNumber)
		{
			this.Sector = SectorNumber;
			MIN = SectorNumber / (60 * 75);
			SEC = (SectorNumber / 75) % 60;
			FRAC = SectorNumber % 75;
			_value = null;
		}
	}

	/// <summary>
	/// The type of a Track, not strictly (for now) adhering to the realistic values, but also including information for ourselves about what source the data came from. 
	/// We should make that not the case.
	/// TODO - let CUE have its own "track type" enum, since cue tracktypes arent strictly corresponding to "real" track types, whatever those are.
	/// </summary>
	public enum ETrackType
	{
		/// <summary>
		/// The track type isn't always known.. it can take this value til its populated
		/// </summary>
		Unknown,

		/// <summary>
		/// CD-ROM (yellowbook) specification - it is a Mode1 track, and we have all 2352 bytes for the sector
		/// </summary>
		Mode1_2352,

		/// <summary>
		/// CD-ROM (yellowbook) specification - it is a Mode1 track, but originally we only had 2048 bytes for the sector. 
		/// This means, for various purposes, we need to synthesize additional data
		/// </summary>
		Mode1_2048,

		/// <summary>
		/// CD-ROM (yellowbook) specification - it is a Mode2 track.
		/// </summary>
		Mode2_2352,

		/// <summary>
		/// CD-DA (redbook) specification.. nominally. In fact, it's just 2352 raw PCM bytes per sector, and that concept isnt directly spelled out in redbook.
		/// </summary>
		Audio
	}

	/// <summary>
	/// TODO - this is garbage. It's half input related, and half output related. This needs to be split up.
	/// </summary>
	public class CueBinPrefs
	{
		/// <summary>
		/// Controls general operations: should the output be split into several blobs, or just use one?
		/// </summary>
		public bool OneBlobPerTrack;

		/// <summary>
		/// NOT SUPPORTED YET (just here as a reminder) If choosing OneBinPerTrack, you may wish to write wave files for audio tracks.
		/// </summary>
		//public bool DumpWaveFiles;

		/// <summary>
		/// turn this on to dump bins instead of just cues
		/// </summary>
		public bool ReallyDumpBin;

		/// <summary>
		/// Dump bins to bitbucket instead of disk
		/// </summary>
		public bool DumpToBitbucket;

		/// <summary>
		/// dump a .sub.q along with bins. one day we'll want to dump the entire subcode but really Q is all thats important for debugging most things
		/// </summary>
		public bool DumpSubchannelQ;

		/// <summary>
		/// generate remarks and other annotations to help humans understand whats going on, but which will confuse many cue parsers
		/// </summary>
		public bool AnnotateCue;

		/// <summary>
		/// EVIL: in theory this would attempt to generate pregap commands to save disc space, but I think this is a bad idea.
		/// it would also be useful for OneBinPerTrack mode in making wave files.
		/// HOWEVER - by the time we've loaded things up into our canonical format, we don't know which 'pregaps' are safe for turning back into pregaps
		/// Because they might sometimes contain data (gapless audio discs). So we would have to inspect a series of sectors to look for silence.
		/// And even still, the ECC information might be important. So, forget it.
		/// NEVER USE OR IMPLEMENT THIS
		/// </summary>
		//public bool PreferPregapCommand = false;

		/// <summary>
		/// some cue parsers cant handle sessions. better not emit a session command then. multi-session discs will then be broken
		/// </summary>
		public bool SingleSession;

		/// <summary>
		/// enables various extension-aware behaviours.
		/// enables auto-search for files with the same name but differing extension.
		/// enables auto-detection of situations where cue blobfiles are indicating the wrong type in the cuefile
		/// </summary>
		public bool ExtensionAware = false;

		/// <summary>
		/// whenever we have a choice, use case sensitivity in searching for files
		/// </summary>
		public bool CaseSensitive = false;

		/// <summary>
		/// DO NOT CHANGE THIS! All sectors will be written with ECM data. It's a waste of space, but it is exact. (not completely supported yet)
		/// </summary>
		public bool DumpECM = true;
	}

	/// <summary>
	/// Encapsulates an in-memory cue+bin (complete cuesheet and a little registry of files)
	/// it will be based on a disc (fro mwhich it can read sectors to avoid burning through extra memory)
	/// TODO - we must merge this with whatever reads in cue+bin
	/// </summary>
	public class CueBin
	{
		public string cue;
		public string baseName;
		public Disc disc;

		public class BinFileDescriptor
		{
			public string name;
			public List<int> abas = new List<int>();

			//todo - do we really need this? i dont think so...
			public List<bool> aba_zeros = new List<bool>();
			public int SectorSize;
		}

		public List<BinFileDescriptor> bins = new List<BinFileDescriptor>();

		//NOT SUPPORTED RIGHT NOW
		//public string CreateRedumpReport()
		//{
		//    if (disc.TOC.Sessions[0].Tracks.Count != bins.Count)
		//        throw new InvalidOperationException("Cannot generate redump report on CueBin lacking OneBinPerTrack property");
		//    StringBuilder sb = new StringBuilder();
		//    for (int i = 0; i < disc.TOC.Sessions[0].Tracks.Count; i++)
		//    {
		//        var track = disc.TOC.Sessions[0].Tracks[i];
		//        var bfd = bins[i];
				
		//        //dump the track
		//        byte[] dump = new byte[track.length_aba * 2352];
		//        //TODO ????????? post-ABA unknown
		//        //for (int aba = 0; aba < track.length_aba; aba++)
		//        //    disc.ReadLBA_2352(bfd.lbas[lba],dump,lba*2352);
		//        string crc32 = string.Format("{0:X8}", CRC32.Calculate(dump));
		//        string md5 = Util.Hash_MD5(dump, 0, dump.Length);
		//        string sha1 = Util.Hash_SHA1(dump, 0, dump.Length);

		//        int pregap = track.Indexes[1].lba - track.Indexes[0].lba;
		//        Timestamp pregap_ts = new Timestamp(pregap);
		//        Timestamp len_ts = new Timestamp(track.length_lba);
		//        sb.AppendFormat("{0}\t{1}\t{2}\t{3}\t{4}\t{5}\t{6}\t{7}\t{8}\n", 
		//            i, 
		//            Cue.RedumpTypeStringForTrackType(track.TrackType),
		//            pregap_ts.Value,
		//            len_ts.Value,
		//            track.length_lba,
		//            track.length_lba*Cue.BINSectorSizeForTrackType(track.TrackType),
		//            crc32,
		//            md5,
		//            sha1
		//            );
		//    }
		//    return sb.ToString();
		//}

		public void Dump(string directory, CueBinPrefs prefs)
		{
			ProgressReport pr = new ProgressReport();
			Dump(directory, prefs, pr);
		}

		public void Dump(string directory, CueBinPrefs prefs, ProgressReport progress)
		{
			byte[] subcodeTemp = new byte[96];
			progress.TaskCount = 2;

			progress.Message = "Generating Cue";
			progress.ProgressEstimate = 1;
			progress.ProgressCurrent = 0;
			progress.InfoPresent = true;
			string cuePath = Path.Combine(directory, baseName + ".cue");
			if (prefs.DumpToBitbucket) { }
			else File.WriteAllText(cuePath, cue);

			progress.Message = "Writing bin(s)";
			progress.TaskCurrent = 1;
			progress.ProgressEstimate = bins.Sum(bfd => bfd.abas.Count);
			progress.ProgressCurrent = 0;
			if(!prefs.ReallyDumpBin) return;

			foreach (var bfd in bins)
			{
				int sectorSize = bfd.SectorSize;
				byte[] temp = new byte[2352];
				byte[] empty = new byte[2352];
				string trackBinFile = bfd.name;
				string trackBinPath = Path.Combine(directory, trackBinFile);
				string subQPath = Path.ChangeExtension(trackBinPath, ".sub.q");
				Stream fsSubQ = null;
				Stream fs;
				if(prefs.DumpToBitbucket)
					fs = Stream.Null;
				else fs = new FileStream(trackBinPath, FileMode.Create, FileAccess.Write, FileShare.None);
				try
				{
					if (prefs.DumpSubchannelQ)
						if (prefs.DumpToBitbucket)
							fsSubQ = Stream.Null;
						else fsSubQ = new FileStream(subQPath, FileMode.Create, FileAccess.Write, FileShare.None);

					for (int i = 0; i < bfd.abas.Count; i++)
					{
						if (progress.CancelSignal) return;

						progress.ProgressCurrent++;
						int aba = bfd.abas[i];
						if (bfd.aba_zeros[i])
						{
							fs.Write(empty, 0, sectorSize);
						}
						else
						{
							if (sectorSize == 2352)
								disc.ReadABA_2352(aba, temp, 0);
							else if (sectorSize == 2048) disc.ReadABA_2048(aba, temp, 0);
							else throw new InvalidOperationException();
							fs.Write(temp, 0, sectorSize);

							//write subQ if necessary
							if (fsSubQ != null)
							{
								disc.Sectors[aba].SubcodeSector.ReadSubcodeDeinterleaved(subcodeTemp, 0);
								fsSubQ.Write(subcodeTemp, 12, 12);
							}
						}
					}
				}
				finally
				{
					fs.Dispose();
					if (fsSubQ != null) fsSubQ.Dispose();
				}
			}
		}
	}
}