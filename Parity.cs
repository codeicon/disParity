using System;
using System.Collections.Generic;
using System.Text;
using System.IO;

namespace disParity
{

  class Parity
  {

    const Int32 PARITY_BLOCK_SIZE = 65536; 
    const Int32 BLOCKS_PER_FILE = 16384;
    const Int32 MAX_BLOCK_CACHE = 512;
    const Int32 DEFAULT_MAX_TEMP_BLOCKS = 4096;
    static string parityDir;
    static string tempFileName;
    static FileStream f = null;
    static UInt32 currentParityFile;
    static UInt32 maxTempBlocks = DEFAULT_MAX_TEMP_BLOCKS;
    static SortedList<UInt32, byte[]> blockCache = null;

    /* Temp parity stuff */
    const string TEMP_PARITY_FILENAME = "parity.tmp";
    static Stream temp = null;
    static UInt32 tempStartBlock;
    static UInt32 tempLength;

    public static void Initialize(string dir, string tempDir, 
      bool enableBlockCache)
    {
      if (enableBlockCache)
        blockCache = new SortedList<UInt32, byte[]>();
      parityDir = dir;
      tempFileName = tempDir + TEMP_PARITY_FILENAME;
    }

    public static void DeleteAll()
    {
      DirectoryInfo dirInfo = new DirectoryInfo(parityDir);
      FileInfo[] files = dirInfo.GetFiles();
      foreach (FileInfo f in files)
        if (f.Name.StartsWith("parity") &&
          Path.GetExtension(f.Name) == ".dat")
          File.Delete(f.FullName);
    }

    static string ParityFileName(UInt32 block)
    {
      UInt32 partityFileNum = block / (UInt32)BLOCKS_PER_FILE;
      return parityDir + "parity" + partityFileNum.ToString() + ".dat";
    }

    static bool OpenParityFile(UInt32 block, bool readOnly)
    {
      UInt32 partityFileNum = block / (UInt32)BLOCKS_PER_FILE;
      if (f != null && partityFileNum == currentParityFile) {
        long position = FilePosition(block);
        if (f.Position != position)
          if (readOnly && position > f.Length)
            return false;
          f.Position = position;
        return true;
      }
      if (f != null) {
        FlushBlockCache();
        f.Close();
        f = null;
      }
      string fileName = parityDir + "parity" + partityFileNum.ToString() + 
        ".dat";
      if (readOnly && !File.Exists(ParityFileName(block)))
        return false;
      f = new FileStream(ParityFileName(block), FileMode.OpenOrCreate, 
        FileAccess.ReadWrite);
      currentParityFile = partityFileNum;
      f.Position = FilePosition(block);
      return true;
    }

    public static void FlushBlockCache()
    {
      if (blockCache != null) {
        for (int i = 0; i < blockCache.Count; i++) {
          f.Position = FilePosition(blockCache.Keys[i]);
          f.Write(blockCache.Values[i], 0, PARITY_BLOCK_SIZE);
        }
        blockCache.Clear();
      }
    }

    public static void Close()
    {
      if (f != null) {
        FlushBlockCache();
        f.Close();
        f = null;
      }
    }

    /* NOTE: The temp parity mechanism REQUIRES that blocks are written
     * sequentially by increasing block number; any other write sequence will
     * not work! */
    public static void OpenTempParity(UInt32 startBlock, UInt32 lengthInBlocks)
    {
      if (lengthInBlocks < maxTempBlocks)
        try {
          temp = new MemoryStream((int)(lengthInBlocks * PARITY_BLOCK_SIZE));
        }
        catch {
          /* If the allocation fails for any reason (most likely cause is out
           * of memory) just fall back to the temp file. */
          temp = new FileStream(tempFileName, FileMode.Create,
            FileAccess.ReadWrite);
        }
      else
        temp = new FileStream(tempFileName, FileMode.Create,
          FileAccess.ReadWrite);
      tempStartBlock = startBlock;
      tempLength = lengthInBlocks;
    }

    public static void WriteTempBlock(UInt32 block, byte[] data)
    {
      if (temp != null)
        temp.Write(data, 0, PARITY_BLOCK_SIZE);
    }

    public static void CloseTemp()
    {
      if (temp != null) {
        temp.Close();
        temp.Dispose();
        temp = null;
        /* Force a garbage collection pass.  I don't know why this is necessary,
         * but it seems .NET would rather throw an OutOfMemory exception when
         * allocating the next temp buffer. */
        // I don't think this is necessary anymore now that we're using mmf
        // GC.Collect();
        // if (File.Exists(tempFileName))
        //   File.Delete(tempFileName);
      }
    }

    public static void FlushTemp()
    {
      if (temp is MemoryStream) {
        byte[] buf = (temp as MemoryStream).GetBuffer();
        int offset = 0;
        UInt32 endBlock = tempStartBlock + tempLength;
        for (UInt32 b = tempStartBlock; b < endBlock; b++) {
          OpenParityFile(b, false);
          f.Write(buf, offset, PARITY_BLOCK_SIZE);
          offset += PARITY_BLOCK_SIZE;
        }
      } else {
        temp.Seek(0, SeekOrigin.Begin);
        byte[] data = new byte[PARITY_BLOCK_SIZE];
        UInt32 endBlock = tempStartBlock + tempLength;
        for (UInt32 b = tempStartBlock; b < endBlock; b++) {
          temp.Read(data, 0, PARITY_BLOCK_SIZE);
          OpenParityFile(b, false);
          f.Write(data, 0, PARITY_BLOCK_SIZE);
        }
      }
      Close(); // closes the actual parity file
    }

    public static bool ReadBlock(UInt32 block, byte[] data)
    {
      try {
        if (!OpenParityFile(block, true))
          Array.Clear(data, 0, PARITY_BLOCK_SIZE);
        else
          f.Read(data, 0, PARITY_BLOCK_SIZE);
      }
      catch (Exception e) {
        Program.logFile.Write("FATAL ERROR: {0}\r\n", e.Message);
        Program.logFile.Write("WARNING: parity data appears to be damaged. " +
          "It is strongly advised that you regenerate the snapshot using the " +
          "\"create\" command.\r\n");
        return false;
      }
      return true;
    }

    /* WriteBlock is currently called during creates. */
    public static void WriteBlock(UInt32 block, byte[] data)
    {
      OpenParityFile(block, false);
      if (blockCache == null) {
        f.Write(data, 0, PARITY_BLOCK_SIZE);
        return;
      }
      if (blockCache.Count == MAX_BLOCK_CACHE)
        FlushBlockCache();
      byte[] cachedBlock = new byte[PARITY_BLOCK_SIZE];
      Array.Copy(data, cachedBlock, PARITY_BLOCK_SIZE);
      /* By using [] instead of Add, we guarantee that in the unlikely case
       * that the block is already in the cache, it gets replaced instead of
       * throwing an exception. */
      blockCache[block] = cachedBlock;
    }

    /* AddFileData is currently not used. */
    public static void AddFileData(UInt32 block, byte[] data)
    {
      OpenParityFile(block, false);
      byte[] parity;
      if (blockCache == null) {
        parity = new byte[PARITY_BLOCK_SIZE];
        f.Read(parity, 0, PARITY_BLOCK_SIZE);
        FastXOR(parity, data);
        f.Position = FilePosition(block);
        f.Write(parity, 0, PARITY_BLOCK_SIZE);
        return;
      }
      bool alreadyInCache = false;
      if (!blockCache.TryGetValue(block, out parity)) {
        parity = new byte[PARITY_BLOCK_SIZE];
        /* This read may fail if we are at the end of the parity file, but
         * that's ok, parity will then just be zeroes which is what we want. */
        f.Read(parity, 0, PARITY_BLOCK_SIZE);
      } else
        alreadyInCache = true; // unlikely, but theoretically possible
      FastXOR(parity, data);
      if (!alreadyInCache) {
        if (blockCache.Count == MAX_BLOCK_CACHE)
          FlushBlockCache();
        blockCache.Add(block, parity);
      }
    }

    public static unsafe void FastXOR(byte[] buf1, byte[] buf2)
    {
      fixed (byte* p1 = buf1)
      fixed (byte* p2 = buf2) {
        long* lp1 = (long*)p1;
        long* lp2 = (long*)p2;
        for (int i = 0; i < (PARITY_BLOCK_SIZE / 8); i++) {
          *lp1 ^= *lp2;
          lp1++;
          lp2++;
        }
      }
    }

    static long FilePosition(UInt32 block)
    {
      UInt32 partityFileNum = block / (UInt32)BLOCKS_PER_FILE;
      return (block - (partityFileNum * BLOCKS_PER_FILE)) * PARITY_BLOCK_SIZE;
    }

    public static void SetMaxTempRAM(UInt32 mb)
    {
      const int MAX_TEMP_RAM = 2047;
      if (mb < 0)
        return;
      if (mb > MAX_TEMP_RAM)
        mb = MAX_TEMP_RAM;
      maxTempBlocks = ((mb * 1024 * 1024) / PARITY_BLOCK_SIZE) + 1;
    }

    public static string Dir
    {
      get { return parityDir; }
    }

    public static Int32 BlockSize
    {
      get { return PARITY_BLOCK_SIZE; }
    }

    /* Returns free space available on parity drive, or -1 if it cannot be
     * determined. */
    public static long FreeSpace
    {
      get
      {
        string parityDrive = Path.GetPathRoot(parityDir);
        if (parityDrive != "") {
          DriveInfo driveInfo;
          try {
            driveInfo = new DriveInfo(parityDrive);
          }
          catch {
            return -1;
          }
          return driveInfo.AvailableFreeSpace;
        } else
          return -1;
      }
    }

  }

}
