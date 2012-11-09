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

    private FileStream f = null;
    private UInt32 currentParityFile;
    private Config config;

    public Parity(Config config)
    {
      this.config = config;
    }

    public static Int32 BlockSize { get { return PARITY_BLOCK_SIZE; } }

    public void DeleteAll()
    {
      DirectoryInfo dirInfo = new DirectoryInfo(config.ParityDir);
      FileInfo[] files = dirInfo.GetFiles();
      foreach (FileInfo f in files)
        if (f.Name.StartsWith("parity") && Path.GetExtension(f.Name) == ".dat")
          File.Delete(f.FullName);
    }

    private string ParityFileName(UInt32 block)
    {
      UInt32 partityFileNum = block / (UInt32)BLOCKS_PER_FILE;
      return Path.Combine(config.ParityDir, "parity" + partityFileNum.ToString() + ".dat");
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
      string fileName = ParityFileName(block);
      if (readOnly && !File.Exists(fileName))
        return false;
      f = new FileStream(fileName, FileMode.OpenOrCreate,  FileAccess.ReadWrite);
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

    public static UInt32 LengthInBlocks(long lengthInBytes)
    {
      UInt32 result = (UInt32)(lengthInBytes / PARITY_BLOCK_SIZE);
      if (result * PARITY_BLOCK_SIZE < lengthInBytes)
        result++;
      return result;
    }

    /// <summary>
    /// Returns free space available on parity drive, or -1 if it cannot be determined.
    /// </summary>
    public long FreeSpace
    {
      get
      {
        string parityDrive = Path.GetPathRoot(config.ParityDir);
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
