using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.IO;
using System.Text;

namespace disParity
{

  public class Recover
  {
    static UInt32 failures = 0;
    static MD5 hash;
    static byte[] data;
    static byte[] fileData;

    static bool initialized;
    static long totalSize;

    static void Initialize()
    {
      if (!initialized) {
        hash = MD5.Create();
        data = new byte[Parity.BlockSize];
        fileData = new byte[Parity.BlockSize];
        initialized = true;
      }
    }

    public static void RecoverDrive(Int32 drive, string dir, bool testOnly)
    {
      DateTime start = DateTime.Now;
      Initialize();
      totalSize = 0;
      failures = 0;
      List<FileRecord> files = Program.drives[drive].fileList;
      foreach (FileRecord f in files)
        RecoverFile(f, dir, testOnly);
      TimeSpan elapsed = DateTime.Now - start;
      Program.logFile.Write("{0} {1} file{2} ({3}) in {4:F2} sec. Failures: {5}\r\n",
        testOnly ? "Tested" : "Recovered", files.Count, files.Count == 1 ?
        "" : "s", Program.SmartSize(totalSize), elapsed.TotalSeconds, failures);
    }

    public static void RecoverFile(FileRecord f, string dir, bool testOnly)
    {
      FileStream recoveryFile = null;
      Initialize();
      string fullPath = Program.MakeFullPath(dir, f.name);
      if (testOnly)
        Program.logFile.Write("Testing {0}...", f.name);
      else {
        Program.logFile.Write("Recovering {0}...", f.name);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
        recoveryFile = new FileStream(fullPath, FileMode.Create,
          FileAccess.ReadWrite);
      }
      long leftToWrite = f.length;
      UInt32 block = f.startBlock;
      hash.Initialize();
      totalSize += leftToWrite;
      while (leftToWrite > 0) {
        RecoverBlock(block, f);
        Int32 blockSize = leftToWrite > Parity.BlockSize ?
          Parity.BlockSize : (int)leftToWrite;
        if (!testOnly)
          recoveryFile.Write(data, 0, blockSize);
        hash.TransformBlock(data, 0, blockSize, data, 0);
        leftToWrite -= Parity.BlockSize;
        block++;
      }
      hash.TransformFinalBlock(data, 0, 0);
      if (f.length > 0 &&
        DataPath.HashCodesMatch(hash.Hash, f.hashCode)) {
        if (testOnly) {
          /* Now check against the actual file */
          string filename = Program.drives[f.drive].root + "\\" + f.name;
          recoveryFile = new FileStream(filename, FileMode.Open,
            FileAccess.Read);
          hash.Initialize();
          if (DataPath.HashCodesMatch(hash.ComputeHash(recoveryFile),
            f.hashCode))
            Program.logFile.Write("Hash verified\r\n");
          else {
            Program.logFile.Write("Verify FAILED!\r\n");
            failures++;
          }
          recoveryFile.Close();
        } else
          Program.logFile.Write("Hash verified\r\n");
      } else {
        Program.logFile.Write("Verify FAILED!\r\n");
        failures++;
      }
      if (!testOnly) {
        recoveryFile.Close();
        File.SetCreationTime(fullPath, f.creationTime);
        File.SetLastWriteTime(fullPath, f.lastWriteTime);
        File.SetAttributes(fullPath, f.attributes);
      }

    }

    static void RecoverBlock(UInt32 block, FileRecord f)
    {
      Parity.ReadBlock(block, data);
      for (int d = 0; d < Program.drives.Length; d++) {
        if (d == f.drive)
          continue;
        if (Program.drives[d].ReadFileData(block, fileData))
          Parity.FastXOR(data, fileData);
      }
    }


  }

}
