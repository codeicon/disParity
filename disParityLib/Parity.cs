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
    const Int32 DEFAULT_MAX_TEMP_BLOCKS = 4096;

    private string parityDir;
    private string tempDir;
    private FileStream f = null;
    private UInt32 currentParityFile;
    private UInt32 maxTempBlocks = DEFAULT_MAX_TEMP_BLOCKS;

    public Parity(string dir, string tempDir, UInt32 maxTempRAM)
    {
      parityDir = dir;
      this.tempDir = tempDir;
      SetMaxTempRAM(maxTempRAM);
    }

    public static Int32 BlockSize { get { return PARITY_BLOCK_SIZE; } }

    public string Dir { get { return parityDir; } }

    public string TempDir { get { return tempDir; } }

    public UInt32 MaxTempBlocks { get { return maxTempBlocks; } }

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

    private void SetMaxTempRAM(UInt32 mb)
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
