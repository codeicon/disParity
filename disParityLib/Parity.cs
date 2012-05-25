using System;
using System.Collections.Generic;
using System.Text;
using System.IO;

namespace disParity
{

  internal class Parity
  {

    const Int32 PARITY_BLOCK_SIZE = 65536; 
    const Int32 BLOCKS_PER_FILE = 16384;
    const Int32 MAX_BLOCK_CACHE = 512;
    const Int32 DEFAULT_MAX_TEMP_BLOCKS = 4096;

    private string parityDir;
    private string tempFileName;
    private FileStream f = null;
    private UInt32 currentParityFile;
    private UInt32 maxTempBlocks = DEFAULT_MAX_TEMP_BLOCKS;

    /* Temp parity stuff */
    const string TEMP_PARITY_FILENAME = "parity.tmp";
    private Stream temp = null;
    private UInt32 tempStartBlock;
    private UInt32 tempLength;

    public Parity(string dir, string tempDir)
    {
      parityDir = dir;
      tempFileName = tempDir + TEMP_PARITY_FILENAME;
    }

    public string Dir { get { return parityDir; } }

    public static Int32 BlockSize { get { return PARITY_BLOCK_SIZE; } }

    public void DeleteAll()
    {
      DirectoryInfo dirInfo = new DirectoryInfo(parityDir);
      FileInfo[] files = dirInfo.GetFiles();
      foreach (FileInfo f in files)
        if (f.Name.StartsWith("parity") &&
          Path.GetExtension(f.Name) == ".dat")
          File.Delete(f.FullName);
    }

    private string ParityFileName(UInt32 block)
    {
      UInt32 partityFileNum = block / (UInt32)BLOCKS_PER_FILE;
      return parityDir + "parity" + partityFileNum.ToString() + ".dat";
    }

    private bool OpenParityFile(UInt32 block, bool readOnly)
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
      Close();
      string fileName = parityDir + "parity" + partityFileNum.ToString() + ".dat";
      if (readOnly && !File.Exists(ParityFileName(block)))
        return false;
      f = new FileStream(ParityFileName(block), FileMode.OpenOrCreate,  FileAccess.ReadWrite);
      currentParityFile = partityFileNum;
      f.Position = FilePosition(block);
      return true;
    }

    public void Close()
    {
      if (f != null) {
        f.Close();
        f.Dispose();
        f = null;
      }
    }

    /* NOTE: The temp parity mechanism REQUIRES that blocks are written
     * sequentially by increasing block number; any other write sequence will
     * not work! */
    public void OpenTempParity(UInt32 startBlock, UInt32 lengthInBlocks)
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

    public void WriteTempBlock(UInt32 block, byte[] data)
    {
      if (temp != null)
        temp.Write(data, 0, PARITY_BLOCK_SIZE);
    }

    public void CloseTemp()
    {
      if (temp != null) {
        temp.Close();
        temp.Dispose();
        temp = null;
      }
    }

    public void FlushTemp()
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

    public bool ReadBlock(UInt32 block, byte[] data)
    {
      try {
        if (!OpenParityFile(block, true))
          Array.Clear(data, 0, PARITY_BLOCK_SIZE);
        else
          f.Read(data, 0, PARITY_BLOCK_SIZE);
      }
      catch (Exception e) {
        LogFile.Log("FATAL ERROR: {0}", e.Message);
        LogFile.Log("WARNING: parity data appears to be damaged. " +
          "It is strongly advised that you regenerate the snapshot using the " +
          "\"create\" command.");
        return false;
      }
      return true;
    }

    /* WriteBlock is currently called during creates. */
    public void WriteBlock(UInt32 block, byte[] data)
    {
      OpenParityFile(block, false);
      f.Write(data, 0, PARITY_BLOCK_SIZE);
    }

    private static long FilePosition(UInt32 block)
    {
      UInt32 partityFileNum = block / (UInt32)BLOCKS_PER_FILE;
      return (block - (partityFileNum * BLOCKS_PER_FILE)) * PARITY_BLOCK_SIZE;
    }

    public void SetMaxTempRAM(UInt32 mb)
    {
      const int MAX_TEMP_RAM = 2047;
      if (mb < 0)
        return;
      if (mb > MAX_TEMP_RAM)
        mb = MAX_TEMP_RAM;
      maxTempBlocks = ((mb * 1024 * 1024) / PARITY_BLOCK_SIZE) + 1;
    }

    /// <summary>
    /// Returns free space available on parity drive, or -1 if it cannot be determined.
    /// </summary>
    public long FreeSpace
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
