using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Diagnostics;

namespace disParity
{

  internal class Parity
  {

    const Int32 BLOCKS_PER_FILE = 16384;

    private FileStream f = null;
    private UInt32 currentParityFile;
    private Config config;
    private UInt32 maxBlock; // the highest allocated block so far (i.e. exists on disk, not necessarily in use by the app

    public Parity(Config config)
    {
      this.config = config;
      ComputeMaxBlock();
    }

    public const Int32 BLOCK_SIZE = 65536;

    /// <summary>
    /// Figures out what the highest allocated block is by examining the parityX.dat files on disk
    /// </summary>
    private void ComputeMaxBlock()
    {
      maxBlock = 0;
      try {
        DirectoryInfo dirInfo = new DirectoryInfo(config.ParityDir);
        FileInfo[] files = dirInfo.GetFiles();
        int maxFileNum = 0;
        FileInfo maxFile = null;
        foreach (FileInfo f in files)
          if (f.Name.StartsWith("parity") && Path.GetExtension(f.Name) == ".dat") {
            string name = f.Name.Replace("parity", "");
            name = name.Replace(".dat", "");
            // all that should be left now is the file number
            int fileNum = Convert.ToInt32(name);
            if (fileNum > maxFileNum) {
              maxFileNum = fileNum;
              maxFile = f;
            }
          }
        if (maxFile != null)
          maxBlock = (UInt32)(maxFileNum * BLOCKS_PER_FILE) + ((UInt32)maxFile.Length / BLOCK_SIZE);
      }
      catch {
      }
    }

    public void DeleteAll()
    {
      Close();
      DirectoryInfo dirInfo = new DirectoryInfo(config.ParityDir);
      FileInfo[] files = dirInfo.GetFiles();
      foreach (FileInfo f in files)
        if (f.Name.StartsWith("parity") && Path.GetExtension(f.Name) == ".dat")
          File.Delete(f.FullName);
      maxBlock = 0;
    }

    private string ParityFileName(UInt32 block)
    {
      UInt32 partityFileNum = block / (UInt32)BLOCKS_PER_FILE;
      return Path.Combine(config.ParityDir, "parity" + partityFileNum.ToString() + ".dat");
    }

    private bool OpenParityFile(UInt32 block, bool readOnly)
    {
      UInt32 partityFileNum = block / (UInt32)BLOCKS_PER_FILE;
      long position = FilePosition(block);
      if (f != null && partityFileNum == currentParityFile) {
        if (readOnly && position >= f.Length) {
          LogFile.Error("ERROR: Attempt to read past end of " + ParityFileName(block));
          return false;
        }  
        f.Position = position;
        return true;
      }
      Close();
      string fileName = ParityFileName(block);
      if (readOnly && !File.Exists(fileName)) {
        LogFile.Error("ERROR: Attempt to open non-existant parity file " + fileName);
        return false;
      }
      try {
        f = new FileStream(fileName, FileMode.OpenOrCreate, FileAccess.ReadWrite);
      }
      catch (Exception e) {
        LogFile.Error("ERROR opening parity file " + fileName + ": " + e.Message);
        return false;
      }
      if (readOnly && position >= f.Length) {
        Close();
        LogFile.Error("ERROR: Attempt to read past end of " + fileName);
        return false;
      }
      currentParityFile = partityFileNum;
      f.Position = position;
      return true;
    }

    /// <summary>
    /// Trims parity back down to the given block.  Useful for reclaiming parity 
    /// disk space after files have been deleted off the end.
    /// </summary>
    public void Trim(UInt32 block)
    {
      if (block >= maxBlock)
        return;
      // truncate the parity file corresponding to block
      if (!OpenParityFile(block, true))
        return;
      f.SetLength(FilePosition(block));
      Close();
      // now delete all parity files after this one
      UInt32 partityFileNum = (block / (UInt32)BLOCKS_PER_FILE) + 1;
      for (; ; ) {
        string fileName = Path.Combine(config.ParityDir, "parity" + partityFileNum.ToString() + ".dat");
        if (!File.Exists(fileName))
          break;
        File.Delete(fileName);
        partityFileNum++;
      }
      maxBlock = block;
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
        if (!OpenParityFile(block, true)) {
          // OpenParityFile returns false if parityX.dat does not exist
          // FIX ME: WHEN is this a valid case exactly?  It's definitely an error in a lot of cases.
          // It's valid when adding new files to the end of parity.  The non-existent parity block is first read
          // in before being XOR'd with new file data to be written back out.
          // Debug.Assert(false);
          //Array.Clear(data, 0, BLOCK_SIZE);
          // Now that we are pre-allocating parity before adds this is definitely an error
          return false;
        } 
        else {
          int bytesRead = f.Read(data, 0, BLOCK_SIZE);
          Debug.Assert(bytesRead == BLOCK_SIZE);
        }
      }
      catch (Exception e) {
        LogFile.Error("FATAL ERROR: {0}", e.Message);
        LogFile.Error("WARNING: parity data appears to be damaged.  It is strongly advised that you recreate the snapshot from scratch.");
        return false;
      }
      return true;
    }

    public bool WriteBlock(UInt32 block, byte[] data)
    {
      Debug.Assert(block <= maxBlock);
      if (!OpenParityFile(block, false))
        return false;
      try {
        f.Write(data, 0, BLOCK_SIZE);
      }
      catch (Exception e) {
        LogFile.Error("ERROR writing to parity file: " + e.Message);
        LogFile.Error("Attempting to truncate " + ParityFileName(block) + " to previous block boundary...");
        try {
          f.SetLength(FilePosition(block));
        }
        catch (Exception e2) {
          LogFile.Error("Truncate failed: " + e2.Message);
          return false;
        }
        LogFile.Log("Truncate succeeded.");
        return false;
      }
      if (maxBlock <= block)
        maxBlock = block + 1;
      return true;
    }

    private static long FilePosition(UInt32 block)
    {
      UInt32 partityFileNum = block / (UInt32)BLOCKS_PER_FILE;
      return (block - (partityFileNum * BLOCKS_PER_FILE)) * BLOCK_SIZE;
    }

    public static UInt32 LengthInBlocks(long lengthInBytes)
    {
      UInt32 result = (UInt32)(lengthInBytes / BLOCK_SIZE);
      if ((long)result * BLOCK_SIZE < lengthInBytes)
        result++;
      return result;
    }

    public UInt32 MaxBlock { get { return maxBlock; } }

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
