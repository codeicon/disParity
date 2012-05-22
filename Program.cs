using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Security.Cryptography;
using System.Diagnostics;

namespace disParity
{

  public enum Command
  {
    None,
    Create,
    Update,
    Recover,
    Test,
    Verify,
    List,
    Stats,
    HashCheck,
    Monitor,
    Undelete
  }

  class Program
  {
    public static LogFile logFile;
    public static DataPath[] drives;
    public static bool ignoreHidden = false;

    static string[] backupDirs;
    static string parityDir;
    static string tempDir = ".\\";
    static Int32 driveNum = -1;

    public const string VERSION = "0.20";

    static void Main(string[] args)
    {
      string recoverDir = "";
      Command cmd = Command.None;
      bool verbose = false;

      if (args.Length > 0) {
        if (args[0].ToLower() == "create")
          cmd = Command.Create;
        else if (args[0].ToLower() == "update")
          cmd = Command.Update;
        else if (args[0].ToLower() == "verify") {
          cmd = Command.Verify;
        } else if ((args.Length == 3) && args[0].ToLower() == "recover") {
          cmd = Command.Recover;
          if (!ReadDriveNum(args))
            return;
          recoverDir = args[2];
        } else if ((args.Length == 2) && args[0].ToLower() == "test") {
          cmd = Command.Test;
          if (!ReadDriveNum(args))
            return;
        } else if (args[0].ToLower() == "list") {
          cmd = Command.List;
          if (args.Length == 2) {
            if (!ReadDriveNum(args))
              return;
          }
        }
        else if (args[0].ToLower() == "stats")
          cmd = Command.Stats;
        else if (args[0].ToLower() == "hashcheck") {
          cmd = Command.HashCheck;
          if (args.Length == 2) {
            if (!ReadDriveNum(args))
              return;
          }
        } else if (args[0].ToLower() == "monitor") {
          verbose = true; 
          cmd = Command.Monitor;
        } else if (args[0].ToLower() == "undelete") {
          cmd = Command.Undelete;
          if (!ReadDriveNum(args))
            return;
        }

      }

      if (args.Length > 1 && (cmd == Command.Create || cmd == Command.Update ||
        cmd == Command.Verify)) {
        if (args[1].ToLower() == "-v")
          verbose = true;
        else {
          PrintUsage();
          return;
        }
      }

      if (cmd == Command.None) {
        PrintUsage();
        return;
      }

      if (!LoadConfig()) {
        Console.WriteLine("Could not open config.txt");
        return;
      }

      string logFileName = "disParity log " + 
        DateTime.Now.ToString("yy-MM-dd HH.mm.ss");
      logFile = new LogFile(logFileName, verbose);
      logFile.Write("Beginning \"{0}\" command at {1} on {2}\r\n", args[0].ToLower(),
        DateTime.Now.ToShortTimeString(), DateTime.Today.ToLongDateString());

      if (cmd == Command.Update) {
        try {
          ParitySet set = new ParitySet("config.txt");
          set.Update();
        }
        catch (Exception e) {
          LogFile.Log("Fatal error encountered during {0}: {1}",
            args[0].ToLower(), e.Message);
          LogFile.Log("Stack trace: {0}", e.StackTrace);
        }
        finally {
          Usage.Close();
          logFile.Close();
        }
        return;
      }

      /* Make sure at least one drive has been set */
      if (backupDirs.Length == 0) {
        logFile.Write("No drives found in config.txt\r\n");
        logFile.Close();
        return;
      }

      if (driveNum >= backupDirs.Length) {
        logFile.Write("Invalid drive number {0}\r\n", driveNum);
        logFile.Close();
        return;
      }

      /* Make sure all data paths are set and valid */
      for (int i = 0; i < backupDirs.Length; i++) {
        if (backupDirs[i] == null) {
          logFile.Write("Path {0} is not set (check config.txt)\r\n", i + 1);
          logFile.Close();
          return;
        }
        if (backupDirs[i][backupDirs[i].Length - 1] != '\\')
          backupDirs[i] += '\\';
        if (!Path.IsPathRooted(backupDirs[i])) {
          logFile.Write("Path {0} is not valid (must be absolute)\r\n", 
            backupDirs[i]);
          logFile.Close();
          return;
        }
      }

      try {

        if (parityDir[parityDir.Length - 1] != '\\')
          parityDir += '\\';
        if (!Path.IsPathRooted(parityDir)) {
          logFile.Write("{0} is not a valid parity path (must be absolute)\r\n", parityDir);
          logFile.Close();
          return;
        }
        try {
          Directory.CreateDirectory(parityDir);
        }
        catch {
          logFile.Write("Could not create parity folder {0}\r\n", parityDir);
          return;
        }
        if (tempDir[tempDir.Length - 1] != '\\')
          tempDir += '\\';
        Parity.Initialize(parityDir, tempDir, cmd != Command.Create);
        if (cmd == Command.Create)
          Parity.DeleteAll();

        if (cmd != Command.List && cmd != Command.Stats)
          /* Test each drive path to make sure it's readable */
          for (int i = 0; i < backupDirs.Length; i++) {
            if ((cmd == Command.Recover) && (i == driveNum))
              /* don't test the drive to be recovered, obviously */
              continue;
            try {
              DirectoryInfo dir = new DirectoryInfo(backupDirs[i]);
              DirectoryInfo[] subDirs = dir.GetDirectories();
            } catch (Exception e) {
              logFile.Write("Error reading {0}: {1}\r\n", backupDirs[i],
                e.Message);
              return;
            }
          }

        Usage.Log(args[0]);

        drives = new DataPath[backupDirs.Length];
        for (int i = 0; i < backupDirs.Length; i++)
          try {
            /* For the hashcheck command, if a drive number has been specified, 
             * only load the meta data for that one drive. */
            if (cmd == Command.HashCheck && driveNum != -1 && driveNum != i)
              continue;
            drives[i] = new DataPath(i, backupDirs[i], cmd);
          } catch (Exception e) {
            logFile.Write("Fatal error: {0}\r\n", e.Message);
            logFile.Write("Aborting.\r\n");
            return;
          }

          try {
            switch (cmd) {
              case Command.Create:
                Create();
                break;
              case Command.Update:
                Update();
                break;
              case Command.Test:
              case Command.Recover:
                Recover.RecoverDrive(driveNum, recoverDir, cmd == Command.Test);
                break;
              case Command.Verify:
                Verify();
                break;
              case Command.List:
                if (driveNum >= 0)
                  drives[driveNum].DumpFileList();
                else
                  foreach (DataPath p in drives)
                    p.DumpFileList();
                break;
              case Command.Stats:
                PrintStats();
                break;
              case Command.HashCheck:
                CheckHashCodes(driveNum);
                break;
              case Command.Monitor:
                Monitor.Start();
                break;
              case Command.Undelete:
                Undelete(driveNum);
                break;
            }
          }
          catch (Exception e) {
            logFile.Write("Fatal error encountered during {0}: {1}\r\n",
              args[0].ToLower(), e.Message);
            logFile.Write("Stack trace: {0}\r\n", e.StackTrace);
          }
      }
      finally {
        Usage.Close();
        logFile.Close();
      }

    }

    static bool ReadDriveNum(string[] args)
    {
      if (args.Length < 2) {
        PrintUsage();
        return false;
      }
      try {
        driveNum = Convert.ToInt32(args[1]) - 1;
        return true;
      }
      catch {
        PrintUsage();
        return false;
      }
    }

    static bool LoadConfig()
    {
      try {
        StreamReader f = new StreamReader("config.txt");
        string s;
        List<string> data = new List<string>();
        while ((s = f.ReadLine()) != null) {
          s = s.Trim();
          if (s.Length == 0 || s[0] == '#')
            continue;
          string[] t = s.Split('=');
          string left = t[0].ToLower();
          if (left == "parity") {
            parityDir = t[1];
            continue;
          } else if (left == "temp") {
            tempDir = t[1];
            continue;
          } else if (left == "tempram") {
            Parity.SetMaxTempRAM(Convert.ToUInt32(t[1]));
            continue;
          } else if (left == "ignorehidden") {
            if (t[1] == "1")
              ignoreHidden = true;
          }
          if (left.Substring(0, 4) != "data")
            continue;
          Int32 num = Convert.ToInt32(left.Substring(4));
          if (backupDirs == null)
            backupDirs = new string[num];
          else if (num > backupDirs.Length)
            Array.Resize<string>(ref backupDirs, num);
          backupDirs[num-1] = t[1];
        }
        return true;
      }
      catch {
        return false;
      }

    }

    static bool CheckFreeSpace()
    {
      /* Calculate requires disk space for parity */
      long maxSize = 0;
      long metaSize = 0;
      foreach (DataPath d in drives) {
        long size = 0;
        metaSize += 8;
        foreach (FileRecord f in d.newFiles) {
          size += f.LengthInBlocks * Parity.BlockSize;
          metaSize += f.RecordSize;
        }
        if (size > maxSize)
          maxSize = size;
      }
      maxSize += metaSize;
      logFile.Write("Disk space required for parity: {0}\r\n",
        SmartSize(maxSize));

      /* Determine space available on parity drive */
      long available = Parity.FreeSpace;
      if (available == -1) {
        logFile.Write("Warning! Could not determine free space available " +
          "on {0}...assuming enough space is available\r\n", Parity.Dir);
        return true;
      }
      logFile.Write("Disk space available on {0}: {1}\r\n", Parity.Dir,
        SmartSize(available));
      if (available < maxSize) {
        logFile.Write("Insufficient disk space available.\r\n");
        return false;
      }
      return true;
    }

    static void Create()
    {
      UInt32 block = 0;
      long totalBytes = 0;
      DateTime start = DateTime.Now;
      byte[] parityBuf = new byte[Parity.BlockSize];
      byte[] dataBuf = new byte[Parity.BlockSize];

      if (!CheckFreeSpace())
        return;

      for (;;) {
        try {
          Int32 bytesRead = 0;
          Int32 d;
          /* Loop through drives until we get the first block of data */
          for (d = 0; d < drives.Length; d++) {
            bytesRead = drives[d].ReadData(parityBuf, block);
            if (bytesRead > 0) {
              totalBytes += bytesRead;
              d++;
              break;
            }
          }
          if (bytesRead == 0)
            break;
          /* Now read blocks from all other drives and XOR */
          for (; d < drives.Length; d++) {
            bytesRead = drives[d].ReadData(dataBuf, block);
            if (bytesRead > 0) {
              totalBytes += bytesRead;
              Parity.FastXOR(parityBuf, dataBuf);
            }
          }
          Parity.WriteBlock(block, parityBuf);
          block++;
        }
        catch (Exception e) {
          logFile.Write("Fatal error encountered during parity " +
            "generation: {0}\r\n", e.Message);
          logFile.Write("Stack trace: {0}", e.StackTrace);
          return;
        }
      }

      Parity.Close();

      /* Make sure all file meta data has been saved */
      foreach (DataPath d in drives)
        d.SaveFileList();

      /* log some statistics */
      TimeSpan elapsed = DateTime.Now - start;
      double MBperSec = (totalBytes / 1048576) / elapsed.TotalSeconds;
      logFile.Write("{0} protected in {1:N}s ({2:F} MB/sec)\r\n",
        SmartSize(totalBytes), elapsed.TotalSeconds, MBperSec);
    }

    static void Update()
    {
      /* Save file meta data now for any drives that had moves */
      foreach (DataPath d in drives)
        d.SaveFileList();

      int deleteCount = 0;
      foreach (DataPath d in drives)
        deleteCount += d.deletedFiles.Count;

      if (deleteCount > 0) {
        long totalSize = 0;
        DateTime start = DateTime.Now;
        logFile.Write("Processing deletes...\r\n");

        foreach (DataPath d in drives)
          for (int j = 0; j < d.deletedFiles.Count; j++) {
            FileRecord deletedFile = d.deletedFiles[j];
            if (RemoveFileFromParity(d, deletedFile))
              totalSize += deletedFile.length;
            else {
              deleteCount--;
              /* If this file was an edit and we can't remove the old version,
               * don't try to add the new version either. */
              if (deletedFile.replacement != null)
                d.newFiles.Remove(deletedFile.replacement);
            }
          }

        foreach (DataPath d in drives)
          d.CloseFile();
        TimeSpan elapsed = DateTime.Now - start;
        logFile.Write("{0} file{1} ({2}) removed in {3:F2} sec\r\n",
          deleteCount, deleteCount == 1 ? "" : "s", SmartSize(totalSize), 
          elapsed.TotalSeconds);
      }

      int addCount = 0;
      foreach (DataPath d in drives)
        addCount += d.newFiles.Count;

      if (addCount > 0) {
        long totalSize = 0;
        DateTime start = DateTime.Now;
        logFile.Write("Processing adds...\r\n");

        long available = Parity.FreeSpace;
        if (available == -1)
          logFile.Write("Warning! Could not determine free space available " +
            "on {0}...assuming enough space is available for adds.\r\n", Parity.Dir);
        else
          logFile.Write("Disk space available on {0}: {1}\r\n", Parity.Dir,
            SmartSize(available));

        foreach (DataPath d in drives)
          foreach (FileRecord info in d.newFiles) {
            if (AddFileToParity(d, info))
              totalSize += info.length;
            else
              addCount--;
          }
            
        TimeSpan elapsed = DateTime.Now - start;
        logFile.Write("{0} file{1} ({2}) added in {3:F2} sec\r\n",
          addCount, addCount == 1 ? "" : "s", SmartSize(totalSize),
          elapsed.TotalSeconds);
      }

      Parity.Close();
    }

    static UInt32 MaxParityBlock()

    {
      UInt32 maxBlock = 0;
      foreach (DataPath d in drives)
        if (d.maxBlock > maxBlock)
          maxBlock = d.maxBlock;
      return maxBlock;
    }

    public static bool AddFileToParity(DataPath d, FileRecord f)   
    {
      string fullPath = MakeFullPath(d.root, f.name);
      /* File may have been changed since the update begain, so refresh
       * its attributes. */
      if (!f.Refresh(fullPath)) {
        logFile.Write("{0} no longer exists.\r\n", fullPath);
        return false;
      }
      if (f.length > 0) {
        FileStream fStream;
        try {
          fStream = new FileStream(fullPath, FileMode.Open, FileAccess.Read,
            FileShare.Read);
        }
        catch (Exception e) {
          logFile.Write("Error opening {0}: {1}\r\n", fullPath, e.Message);
          logFile.Write("File will be skipped this update.\r\n");
          return false;
        }

        try {
          /* See if we can find an empty chunk in the parity we can re-use.
           * We don't want just any empty spot, we want the smallest one 
           * that is large enough to contain the file, to minimize 
           * fragmentation.  A chunk that is exactly the same size is ideal.*/
          List<FreeNode> freeList = d.GetFreeList();
          UInt32 blocksNeeded = f.LengthInBlocks;
          UInt32 startBlock = FreeNode.FindBest(freeList, blocksNeeded);
          
          /* Now make sure there is enough room on parity drive to add it. */
          if (startBlock == FreeNode.INVALID_BLOCK)
            startBlock = d.maxBlock;
          UInt32 endBlock = startBlock + blocksNeeded;
          UInt32 maxBlock = MaxParityBlock();
          if (endBlock > maxBlock) {
            /* File is going on the end, so make sure there is enough space 
             * left on the parity drive to actually add this file. */
            /* FIXME: This check should also be sure there is enough space
             * left for the new file table! */
            long required = (endBlock - maxBlock) * Parity.BlockSize;
            long available = Parity.FreeSpace;
            if ((available != -1) && (available < required)) {
              logFile.Write("Insufficient space available on {0} to process " +
                "{1}.  File will be skipped this update. (Required: {2} " +
                "Available: {3})\r\n", Parity.Dir, fullPath,
                SmartSize(required), SmartSize(available));
              return false;
            }
          }

          /* Figure out how many blocks this file needs, then read each
           * block and add to parity. */
          f.startBlock = startBlock;
          if (logFile.Verbose)
            logFile.Write("Adding {0} to blocks {1} to {2}...\r\n", fullPath,
              startBlock, endBlock - 1);
          else
            logFile.Write("Adding {0}...\r\n", fullPath);

          byte[] parity = new byte[Parity.BlockSize];
          byte[] data = new byte[Parity.BlockSize];
          MD5 hash = MD5.Create();
          hash.Initialize();
          Parity.OpenTempParity(startBlock, f.LengthInBlocks);

          for (UInt32 b = startBlock; b < endBlock; b++) {
            Int32 bytesRead;

            try {
              bytesRead = fStream.Read(data, 0, Parity.BlockSize);
            }
            catch (Exception e) {
              logFile.Write("Error reading {0}: {1}\r\n", fullPath, e.Message);
              logFile.Write("File will be skipped.\r\n");
              return false;
            }

            if (b == (endBlock - 1))
              hash.TransformFinalBlock(data, 0, bytesRead);
            else
              hash.TransformBlock(data, 0, bytesRead, data, 0);
            while (bytesRead < Parity.BlockSize)
              data[bytesRead++] = 0;

            if (!Parity.ReadBlock(b, parity))
              // parity read failed: should not happen!
              // FIXME: don't just return false, the whole update should be aborted
              return false;

            Parity.FastXOR(parity, data);

            try {
              Parity.WriteTempBlock(b, parity);
            }
            catch (Exception e) {
              logFile.Write("Error writing parity data: {0}\r\n", e.Message);
              logFile.Write("File will be skipped.\r\n");
              return false;
            }

          }

          Parity.FlushTemp();

          f.hashCode = hash.Hash;
          if (endBlock > d.maxBlock)
            d.maxBlock = endBlock;
        }
        finally {
          fStream.Close();
          Parity.CloseTemp();
        }

      }
      d.AddFile(f);
      // FIXME: Detect errors saving the new file list and abort!
      d.SaveFileList();
      return true;
    }

    public static bool RemoveFileFromParity(DataPath d, FileRecord f)
    {
      if (f.length > 0) {
        UInt32 startBlock = f.startBlock;
        UInt32 endBlock = startBlock + f.LengthInBlocks;
        string fullPath = MakeFullPath(d.root, f.name);
        if (logFile.Verbose)
          logFile.Write("Removing {0} from blocks {1} to {2}...\r\n",
            fullPath, startBlock, endBlock - 1);
        else
          logFile.Write("Removing {0}...\r\n", fullPath);

        /* Recalulate parity from scratch for all blocks that contained
         * the deleted file's data. */
        byte[] parity = new byte[Parity.BlockSize];
        byte[] data = new byte[Parity.BlockSize];
        Parity.OpenTempParity(startBlock, f.LengthInBlocks);
        for (UInt32 b = startBlock; b < endBlock; b++) {
          Array.Clear(parity, 0, Parity.BlockSize);
          foreach (DataPath d1 in drives) {
            if (d1 == d)
              continue;
            /* Note it's possible that this file may also have been deleted.
             * That's OK, ReadFileData returns false and we don't try to
             * add the deleted file to the parity. */
            try {
              if (d1.ReadFileData(b, data))
                Parity.FastXOR(parity, data);
            }
            catch (Exception e) {
              logFile.Write("Error: {0}\r\n", e.Message);
              logFile.Write("Unable to remove {0}, file will be skipped this update\r\n",
                fullPath);
              Parity.CloseTemp();
              return false;
            }
          }
          Parity.WriteTempBlock(b, parity);
        }
        Parity.FlushTemp();
        Parity.CloseTemp();
      }
      d.RemoveFile(f);
      d.SaveFileList();
      return true;
    }

#if false
    static void Recover(Int32 drive, string dir, bool testOnly)
    {
      byte[] data = new byte[Parity.BlockSize];
      byte[] fileData = new byte[Parity.BlockSize];
      MD5 hash = MD5.Create();
      FileStream recoveryFile = null;
      UInt32 failures = 0;
      DateTime start = DateTime.Now;
      long totalSize = 0;

      List<FileRecord> files = drives[drive].fileList;
      for (int i = 0; i < files.Count; i++) {
        FileRecord f = files[i];
        string fullPath = MakeFullPath(dir, f.name);
        if (testOnly)
          logFile.Write("Testing {0}...", f.name);
        else {
          logFile.Write("Recovering {0}...", f.name);
          Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
          recoveryFile = new FileStream(fullPath, FileMode.Create,
            FileAccess.ReadWrite);
        }
        long leftToWrite = f.length;
        UInt32 block = f.startBlock;
        hash.Initialize();
        totalSize += leftToWrite;
        while (leftToWrite > 0) {
          Int32 blockSize = leftToWrite > Parity.BlockSize ?
            Parity.BlockSize : (int)leftToWrite;
          Parity.ReadBlock(block, data);
          for (int d = 0; d < backupDirs.Length; d++) {
            if (d == drive)
              continue;
            if (drives[d].ReadFileData(block, fileData))
              for (int j = 0; j < blockSize; j++)
                data[j] ^= fileData[j];
          }
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
            string filename = drives[drive].root + "\\" + f.name;
            recoveryFile = new FileStream(filename, FileMode.Open,
              FileAccess.Read);
            hash.Initialize();
            if (DataPath.HashCodesMatch(hash.ComputeHash(recoveryFile),
              f.hashCode))
              logFile.Write("Hash verified\r\n");
            else {
              logFile.Write("Verify FAILED!\r\n");
              failures++;
            } 
            recoveryFile.Close();
          } else
          logFile.Write("Hash verified\r\n");
        }  else {
          logFile.Write("Verify FAILED!\r\n");
          failures++;
        }
        if (!testOnly) {
          recoveryFile.Close();
          File.SetCreationTime(fullPath, f.creationTime);
          File.SetLastWriteTime(fullPath, f.lastWriteTime);
          File.SetAttributes(fullPath, f.attributes);
        }
      }
    }
#endif

    static void VerifyBlock(UInt32 block)
    {
      byte[] parity = new byte[Parity.BlockSize];
      byte[] calcParity = new byte[Parity.BlockSize];
      byte[] buf = new byte[Parity.BlockSize];
      Parity.ReadBlock(block, parity);
      int d;
      int driveCount = 0;
      for (d = 0; d < drives.Length; d++)
        if (drives[d].ReadFileData(block, calcParity)) {
          d++;
          driveCount++;
          break;
        }
      for (; d < drives.Length; d++)
        if (drives[d].ReadFileData(block, buf)) {
          Parity.FastXOR(calcParity, buf);
          driveCount++;
        }
      /* Now compare parity blocks */
      int b = 0;
      if (driveCount == 0) {
        /* No drive has data for this block, parity should be all zeroes */
        for (; b < Parity.BlockSize; b++)
          if (parity[b] != 0)
            break;
      } else {
        for (; b < Parity.BlockSize; b++)
          if (parity[b] != calcParity[b])
            break;
      }
      if (b != Parity.BlockSize)
        logFile.Write("Verify error: block {0} byte {1}\r\n", block, b);
    }

    static void Verify()
    {

      /* Don't allow a verify to procede if there have been any changes */
      foreach (DataPath d in drives)
        if (d.movedCount > 0 || d.deletedFiles.Count > 0) {
          logFile.Write("Changes detected on {0} that will cause verify " +
            "to fail. Run update first.\r\n", d.root);
          return;
        }

      MD5 hash = MD5.Create();
      UInt32 maxBlock = 0;
      foreach (DataPath d in drives)
        if (d.maxBlock > maxBlock)
          maxBlock = d.maxBlock;
      logFile.Write("Starting verify process.  Parity blocks to verify: {0}\r\n", 
        maxBlock);
      byte[] parity = new byte[Parity.BlockSize];
      byte[] buf = new byte[Parity.BlockSize];
      byte[] calcParity = new byte[Parity.BlockSize];
      UInt32 totalErrors = 0;
      for (UInt32 block = 0; block < maxBlock; ) {
        UInt32 errors = 0;
        long totalBytes = 0;
        int maxDrives = 0;
        UInt32 endBlock = block + 10000;
        DateTime timeStart = DateTime.Now; 
        if (endBlock > maxBlock)
          endBlock = maxBlock;
        if (logFile.Verbose)
          logFile.Write("Verifying blocks {0} through {1}...", block, endBlock - 1);
        for (; block < endBlock; block++) {
          Parity.ReadBlock(block, parity);
          totalBytes += Parity.BlockSize;
          int driveCount = 0;
          int d;
          for (d = 0; d < drives.Length; d++)
            if (drives[d].ReadFileData(block, calcParity)) {
              d++;
              driveCount++;
              totalBytes += Parity.BlockSize;
              break;
            }
          for (; d < drives.Length; d++)
            if (drives[d].ReadFileData(block, buf)) {
              totalBytes += Parity.BlockSize;
              driveCount++;
              Parity.FastXOR(calcParity, buf);
            }
          /* Now compare parity blocks */
          int b = 0;
          if (driveCount == 0) {
            /* No drive has data for this block, parity should be all zeroes */
            for (; b < Parity.BlockSize; b++)
              if (parity[b] != 0)
                break;
          } else {
            for (; b < Parity.BlockSize; b++)
              if (parity[b] != calcParity[b])
                break;
          }
          if (b != Parity.BlockSize) {
            errors++;
            if (errors == 1)
              logFile.Write("\r\n");
            logFile.Write("Verify error: block {0} byte {1}\r\n", block, b);
            if (driveCount > 0) {
              logFile.Write("Block {0} contains data from the following " +
                "file(s):\r\n", block);
              foreach (DataPath p in drives) {
                Int32 index = p.FileFromBlock(block);
                if (index != -1) {
                  FileRecord r = p.fileList[index];
                  string filename = MakeFullPath(p.root, r.name);
                  if (r.status == FileStatus.HashVerified)
                    logFile.Write("{0}...(hash verified)\r\n", filename);
                  else if (r.status == FileStatus.HashFailed)
                    logFile.Write("{0}...(hash FAILED)\r\n", filename);
                  else {
                    logFile.Write("{0}...checking hash...", filename);
                    byte[] hashCode;
                    try {
                      FileStream f = new FileStream(filename, FileMode.Open,
                        FileAccess.Read);
                      hash.Initialize();
                      hashCode = hash.ComputeHash(f);
                      f.Close();
                      if (DataPath.HashCodesMatch(r.hashCode, hashCode)) {
                        logFile.Write("verified\r\n");
                        r.status = FileStatus.HashVerified;
                      } else {
                        logFile.Write("FAILED!\r\n");
                        r.status = FileStatus.HashFailed;
                      }
                    }
                    catch {
                      logFile.Write("Could not open file!\r\n");
                    }
                  }
                }
              }
            }
          }
          if (driveCount > maxDrives)
            maxDrives = driveCount;
        }
        TimeSpan elapsed = DateTime.Now - timeStart;
        totalBytes /= 1024;
        totalBytes /= 1024;
        if (errors == 0) {
          if (logFile.Verbose)
            logFile.Write("OK ({0:F2} sec, {1:F2} MB/s, {2} drive{3})\r\n",
              elapsed.TotalSeconds, (double)totalBytes / elapsed.TotalSeconds,
              maxDrives, maxDrives == 1 ? "" : "s");
        } 
        else
          totalErrors += errors;
      }
      logFile.Write("Verify complete.  Errors: {0}\r\n", totalErrors);

    }

    static void CheckHashCodes(Int32 driveNum)
    {
      Int32 failures = 0;
      if (driveNum == -1)
        foreach (DataPath d in drives)
          foreach (FileRecord r in d.fileList) {
            VerifyHash(d.root, r);
            if (r.status != FileStatus.HashVerified)
              failures++;
          }
      else
        foreach (FileRecord r in drives[driveNum].fileList) {
          VerifyHash(drives[driveNum].root, r);
          if (r.status != FileStatus.HashVerified)
            failures++;
        }
      logFile.Write("Hash failure(s): {0}\r\n", failures);
    }

    static void VerifyHash(string root, FileRecord r)
    {
      string filename = MakeFullPath(root, r.name);
      if (r.length == 0)
        /* Zero-length files can't fail a hash check */
        r.status = FileStatus.HashVerified;
      if (r.status == FileStatus.HashVerified)
        logFile.Write("{0}...(hash verified)\r\n", filename);
      else if (r.status == FileStatus.HashFailed)
        logFile.Write("{0}...(hash FAILED)\r\n", filename);
      else {
        logFile.Write("{0}...checking hash...", filename);
        byte[] hashCode;
        try {
          FileStream f = new FileStream(filename, FileMode.Open,
            FileAccess.Read);
          MD5 hash = MD5.Create();
          hash.Initialize();
          hashCode = hash.ComputeHash(f);
          f.Close();
          if (DataPath.HashCodesMatch(r.hashCode, hashCode)) {
            logFile.Write("verified\r\n");
            r.status = FileStatus.HashVerified;
          }
          else {
            logFile.Write("FAILED!\r\n");
            r.status = FileStatus.HashFailed;
          }
        }
        catch {
          logFile.Write("{0}: Could not open file!\r\n", filename);
        }
      }
    }

    static void PrintStats()
    {
      Int64 totalSize = 0;
      Int32 totalFiles = 0;
      logFile.Write("\r\n");
      foreach (DataPath d in drives) {
        Int64 driveTotal = 0;
        foreach (FileRecord r in d.fileList)
          driveTotal += r.length;
        logFile.Write("{0}: {1} files, {2}\r\n", d.root, d.fileList.Count,
          SmartSize(driveTotal));
        totalFiles += d.fileList.Count;
        totalSize += driveTotal;
      }
      logFile.Write("\r\nTotal: {0} files, {1}\r\n", totalFiles,
        SmartSize(totalSize));
    }

    static void Undelete(Int32 drive)
    {
      DataPath d = drives[drive];
      foreach (FileRecord f in d.deletedFiles) {
        string fullPath = MakeFullPath(d.root, f.name);
        if (File.Exists(fullPath))
          /* Can happen for edits; don't undelete in this case, obviously! */
          continue;
        try {
          Recover.RecoverFile(f, d.root, false);
        }
        catch (Exception e) {
          logFile.Write("Unable to recover {0}: {1}\r\n", fullPath, e.Message);
        }
      }
    }

    public static string SmartSize(long size)
    {
      const long KB = 1024;
      const long MB = KB * 1024;
      const long GB = MB * 1024;
      const long TB = GB * 1024;

      string units;
      double result;

      if (size < KB) {
        if (size == 1)
          return "1 byte";
        else
          return size.ToString() + " bytes";
      } else if (size < MB) {
        result = (double)size / KB;
        units = "KB";
      } else if (size < GB) {
        result = (double)size / MB;
        units = "MB";
      } else if (size < TB) {
        result = (double)size / GB;
        units = "GB";
      } else {
        result = (double)size / TB;
        units = "TB";
      }
      return String.Format("{0:F2} {1}", result, units);
    }

    public static string MakeFullPath(string path, string name)
    {
      if (path == "")
        return name;
      if (path[path.Length - 1] == '\\')
        return path + name;
      else
        return path + "\\" + name;
    }

    static void PrintUsage()
    {
      Console.WriteLine("disParity Snapshot Parity Utility Version " + VERSION +
        "\r\n\r\n" +
        "Usage:\r\n\r\n" +
        "  disparity update [-v]          Create or update parity to reflect latest file data\r\n" +
        "                                 since the last snapshot\r\n " +
        "  disparity recover [num] [dir]  Recover drive [num] to directory [dir]\r\n" +
        "  disparity test [num]           Simulate a recovery of drive [num]\r\n" +
        "  disparity verify [-v]          Verify that all file data matches the\r\n" +
        "                                 parity data in the current snapshot\r\n" +
        "  disparity list [num]           Output a list of all files currently\r\n" +
        "                                 protected.  Specify optional drive [num]\r\n" +
        "                                 to restrict output to a single drive.\r\n" +
        "  disparity stats                List file counts and total data size\r\n" +
        "  disparity hashcheck [num]      Check hash of every file on drive [num]\r\n" +
        "  disparity undelete [num]       Restores any deleted files on drive [num]\r\n" +
        "\r\nSpecify optional -v to enable verbose logging.");
    }

  }


}
