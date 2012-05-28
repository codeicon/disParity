using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Security.Cryptography;

namespace disParity
{

  public class ParitySet
  {

    private Config config;
    private List<DataDrive> drives;
    private Parity parity;

    public ParitySet(string configFilePath)
    {
      if (!Directory.Exists(configFilePath))
        throw new Exception(configFilePath + " not found");

      string configPath = Path.Combine(configFilePath, "disparity.xml");
      config = new Config(configPath);
      try {
        string oldConfigPath = Path.Combine(configFilePath, "config.txt");
        if (File.Exists(oldConfigPath)) {
          OldConfig oldConfig = new OldConfig(oldConfigPath);
          config.ParityDir = oldConfig.ParityDir;
          config.TempDir = oldConfig.TempDir;
          config.MaxTempRAM = oldConfig.MaxTempRAM;
          config.IgnoreHidden = oldConfig.IgnoreHidden;
          config.Drives = new List<string>();
          foreach (string d in oldConfig.BackupDirs)
            config.Drives.Add(d);
          config.Ignores = new List<string>();
          foreach (string i in oldConfig.Ignores)
            config.Ignores.Add(i);
          config.Save();
          File.Move(oldConfigPath, oldConfigPath + ".old");
        }
        else
          config.Load();
      }
      catch (Exception e) {
        throw new Exception("Could not load config file: " + e.Message);
      }

      ValidateConfig();

      try {
        Directory.CreateDirectory(config.ParityDir);
      }
      catch (Exception e) {
        throw new Exception("Could not create parity folder " + config.ParityDir + ": " + e.Message);
      }

      Empty = true;
      drives = new List<DataDrive>(config.Drives.Count);
      for (int i = 0; i < config.Drives.Count; i++) {
        string metaFile = Path.Combine(config.ParityDir, String.Format("files{0}.dat", i));
        if (File.Exists(metaFile))
          Empty = false;
        drives.Add(new DataDrive(config.Drives[i], metaFile, config.IgnoreHidden, config.Ignores));
      }

      parity = new Parity(config.ParityDir, config.TempDir);
    }

    /// <summary>
    /// Returns whether or not there is any parity data generated yet for this parity set
    /// </summary>
    public bool Empty { get; private set; }

    /// <summary>
    /// List of zero or more regular expressions defining files to be ignored
    /// </summary>
    public List<string> Ignore { get; private set; }

    /// <summary>
    /// Returns the location of the parity data
    /// </summary>
    public string ParityPath
    {
      get
      {
        return config.ParityDir;
      }
    }

    /// <summary>
    /// Returns a copy of the master list of drives in this ParitySet.
    /// </summary>
    public DataDrive[] Drives
    {
      get
      {
        return drives.ToArray();
      }
    }

    /// <summary>
    /// Erase a previously created parity set
    /// </summary>
    public void Erase()
    {
      parity.DeleteAll();
      foreach (DataDrive d in drives)
        d.Clear();
      Empty = true;
    }

    /// <summary>
    /// Update a parity set to reflect the latest changes
    /// </summary>
    public void Update()
    {
      if (Empty) {
        LogFile.Log("No existing parity data found.  Creating new snapshot.");
        Create();
        return;
      }

      // get the current list of files on each drive and compare to old state
      ScanAll();

      // process all moves for all drives first, since that doesn't require changing
      // any parity data, only the meta data
      foreach (DataDrive d in drives)
        d.ProcessMoves();

      // now process deletes
      int deleteCount = 0;
      long deleteSize = 0;
      DateTime start = DateTime.Now;
      foreach (DataDrive d in drives) {
        foreach (FileRecord r in d.Deletes)
          if (RemoveFromParity(r)) {
            deleteCount++;
            deleteSize += r.Length;
          }
        foreach (FileRecord r in d.Edits)
          if (RemoveFromParity(r)) {
            deleteCount++;
            deleteSize += r.Length;
          }
      }
      if (deleteCount > 0) {
        TimeSpan elapsed = DateTime.Now - start;
        LogFile.Log("{0} file{1} ({2}) removed in {3:F2} sec", deleteCount,
          deleteCount == 1 ? "" : "s", Utils.SmartSize(deleteSize), elapsed.TotalSeconds);
      }

      // now process adds
      int addCount = 0;
      long addSize = 0;
      start = DateTime.Now;
      foreach (DataDrive d in drives) {
        foreach (FileRecord r in d.Adds)
          if (AddToParity(r)) {
            addCount++;
            addSize += r.Length;
          }
        foreach (FileRecord r in d.Edits)
          if (AddToParity(r)) {
            addCount++;
            addSize += r.Length;
          }
      }
      if (addCount > 0) {
        TimeSpan elapsed = DateTime.Now - start;
        LogFile.Log("{0} file{1} ({2}) added in {3:F2} sec", addCount,
          addCount == 1 ? "" : "s", Utils.SmartSize(addSize), elapsed.TotalSeconds);
      }

      parity.Close();

      foreach (DataDrive d in drives) {
        Console.WriteLine("Block mask for {0}:", d.Root);
        PrintBlockMask(d);
      }

    }

    private bool ValidDrive(DataDrive drive)
    {
      foreach (DataDrive d in drives)
        if (d == drive)
          return true;
      return false;
    }

    /// <summary>
    /// Recover all files from the given drive to the given location
    /// </summary>
    public void Recover(DataDrive drive, string path)
    {
      if (!ValidDrive(drive))
        return;
      foreach (FileRecord f in drive.Files)
        RecoverFile(f, path);
    }

    public void HashCheck(DataDrive drive = null)
    {
      int failures = 0;
      if (drive != null) {
        if (!ValidDrive(drive))
          return;
        else
          failures = drive.HashCheck();
      }
      else
        foreach (DataDrive d in drives)
          failures += d.HashCheck();
      LogFile.Log("Hash failure(s): {0}", failures);
    }

    /// <summary>
    /// Adds a new drive to the parity set to be protected
    /// </summary>
    public DataDrive AddDrive(string path)
    {
      string metaFile = Path.Combine(config.ParityDir, String.Format("files{0}.dat", drives.Count));
      DataDrive newDrive = new DataDrive(path, metaFile, config.IgnoreHidden, config.Ignores);
      drives.Add(newDrive);
      // update config and save
      config.Drives.Add(path);
      config.Save();
      return newDrive;
    }

    private void RecoverFile(FileRecord r, string path)
    {
      string fullPath = Utils.MakeFullPath(path, r.Name);
      LogFile.Log("Recovering {0}...", r.Name);
      // make sure the destination directory exists
      Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
      MD5 hash = MD5.Create();
      hash.Initialize();
      using (FileStream f = new FileStream(fullPath, FileMode.Create, FileAccess.Write)) {
        ParityBlock parityBlock = new ParityBlock(parity);
        long leftToWrite = r.Length;
        UInt32 block = r.StartBlock;
        while (leftToWrite > 0) {
          RecoverBlock(r.Drive, block, parityBlock);
          int blockSize = leftToWrite > Parity.BlockSize ? Parity.BlockSize : (int)leftToWrite;
          f.Write(parityBlock.Data, 0, blockSize);
          hash.TransformBlock(parityBlock.Data, 0, blockSize, parityBlock.Data, 0);
          leftToWrite -= Parity.BlockSize;
          block++;
        }
        hash.TransformFinalBlock(parityBlock.Data, 0, 0);
      }
      if (r.Length > 0 && !Utils.HashCodesMatch(hash.Hash, r.HashCode))
        LogFile.Log("ERROR: hash verify FAILED for {0}", fullPath);
      File.SetCreationTime(fullPath, r.CreationTime);
      File.SetLastWriteTime(fullPath, r.LastWriteTime);
      File.SetAttributes(fullPath, r.Attributes);
    }

    private byte[] tempBuf = new byte[Parity.BlockSize];

    private void RecoverBlock(DataDrive drive, UInt32 block, ParityBlock parity)
    {
      parity.Load(block);
      foreach (DataDrive d in drives)
        if (d != drive)
          if (d.ReadBlock(block, tempBuf))
            parity.Add(tempBuf);
    }

    private bool AddToParity(FileRecord r)
    {
      string fullPath = r.FullPath;
      // attributes may have changed since we started, refresh just in case
      if (!r.Refresh(fullPath)) {
        LogFile.Log("{0} no longer exists.", fullPath);
        return false;
      }
      if (r.Length > 0) {
        // See if we can find an empty chunk in the parity we can re-use.
        // We don't want just any empty spot, we want the smallest one 
        // that is large enough to contain the file, to minimize 
        // fragmentation.  A chunk that is exactly the same size is ideal.
        List<FreeNode> freeList = r.Drive.GetFreeList();
        UInt32 startBlock = FreeNode.FindBest(freeList, r.LengthInBlocks);
        if (startBlock == FreeNode.INVALID_BLOCK)
          startBlock = r.Drive.MaxBlock;
        UInt32 endBlock = startBlock + r.LengthInBlocks;
        if (endBlock > MaxParityBlock()) {
          // File is going on the end, so make sure there is enough space 
          // left on the parity drive to actually add this file.
          // FIXME: This check should also be sure there is enough space left for the new file table
          long required = (endBlock - MaxParityBlock()) * Parity.BlockSize;
          long available = parity.FreeSpace;
          if ((available != -1) && (available < required)) {
            LogFile.Log("Insufficient space available on {0} to process " +
              "{1}.  File will be skipped this update. (Required: {2} " +
              "Available: {3})", parity.Dir, fullPath, Utils.SmartSize(required), Utils.SmartSize(available));
            return false;
          }
        }

        r.StartBlock = startBlock;
        if (LogFile.Verbose)
          LogFile.Log("Adding {0} to blocks {1} to {2}...", fullPath, startBlock, endBlock - 1);
        else
          LogFile.Log("Adding {0}...", fullPath);

        byte[] data = new byte[Parity.BlockSize];
        MD5 hash = MD5.Create();
        hash.Initialize();
        using (ParityChange change = new ParityChange(parity, startBlock, r.LengthInBlocks)) {
          using (FileStream f = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            for (UInt32 b = startBlock; b < endBlock; b++) {
              Int32 bytesRead;
              try {
                bytesRead = f.Read(data, 0, Parity.BlockSize);
              }
              catch (Exception e) {
                LogFile.Log("Error reading {0}: {1}", fullPath, e.Message);
                LogFile.Log("File will be skipped.");
                return false;
              }
              if (b == (endBlock - 1))
                hash.TransformFinalBlock(data, 0, bytesRead);
              else
                hash.TransformBlock(data, 0, bytesRead, data, 0);
              while (bytesRead < Parity.BlockSize)
                data[bytesRead++] = 0;
              change.Reset(true);
              change.AddData(data);
              change.Write();
            }
          change.Save();
        }
        r.HashCode = hash.Hash;
      }
      r.Drive.AddFile(r);
      return true;
    }

    private bool RemoveFromParity(FileRecord r)
    {
      if (r.Length > 0) {
        UInt32 startBlock = r.StartBlock;
        UInt32 endBlock = startBlock + r.LengthInBlocks;
        string fullPath = r.FullPath;
        if (LogFile.Verbose)
          LogFile.Log("Removing {0} from blocks {1} to {2}...", fullPath, startBlock, endBlock - 1);
        else
          LogFile.Log("Removing {0}...", fullPath);

        // Recalulate parity from scratch for all blocks that contained the deleted file's data.
        using (ParityChange change = new ParityChange(parity, startBlock, r.LengthInBlocks)) {
          byte[] data = new byte[Parity.BlockSize];
          for (UInt32 b = startBlock; b < endBlock; b++) {
            change.Reset(false);
            foreach (DataDrive d in drives) {
              if (d == r.Drive)
                continue;
              // Note it's possible that this file may also have been deleted. That's OK, ReadFileData 
              // returns false and we don't try to add the deleted file to the parity.
              try {
                if (d.ReadBlock(b, data))
                  change.AddData(data);
              }
              catch (Exception e) {
                LogFile.Log("Error: {0}", e.Message);
                LogFile.Log("Unable to remove {0}, file will be skipped this update", fullPath);
                parity.CloseTemp();
                return false;
              }
            }
            change.Write();
          }
          change.Save();
        }
      }
      r.Drive.RemoveFile(r);
      return true;
    }

    private void PrintBlockMask(DataDrive d)
    {
      BitArray blockMask = d.BlockMask;
      foreach (bool b in blockMask)
        Console.Write("{0}", b ? 'X' : '.');
      Console.WriteLine();
    }

    /// <summary>
    /// Calculates the highest used parity block across all drives
    /// </summary>
    private UInt32 MaxParityBlock()
    {
      UInt32 maxBlock = 0;
      foreach (DataDrive d in drives)
        if (d.MaxBlock > maxBlock)
          maxBlock = d.MaxBlock;
      return maxBlock;
    }

    private void ScanAll()
    {
      foreach (DataDrive d in drives)
        d.Scan();
    }

    /// <summary>
    /// Create a new snapshot from scratch
    /// </summary>
    private void Create()
    {
      DateTime start = DateTime.Now;

      // generate the list of all files to be protected from all drives
      ScanAll();

      // TO DO: check free space on parity drive here

      foreach (DataDrive d in drives)
        d.BeginFileEnum();

      ParityBlock parityBlock = new ParityBlock(parity);
      byte[] dataBuf = new byte[Parity.BlockSize];
      UInt32 block = 0;

      bool done = false;
      while (!done) {
        done = true;
        foreach (DataDrive d in drives)
          if (d.GetNextBlock(done ? parityBlock.Data : dataBuf))
            if (done)
              done = false;
            else
              parityBlock.Add(dataBuf);
        if (!done)
          parityBlock.Write(block);
        block++;
      }
      parity.Close();

    }

    private void ValidateConfig()
    {
      if (config.Drives.Count == 0)
        throw new Exception("No drives found in " + config.Filename);

      // Make sure all data paths are set and valid
      for (int i = 0; i < config.Drives.Count; i++) {
        if (config.Drives[i] == null)
          throw new Exception(String.Format("Path {0} is not set (check {1})", i + 1, config.Filename));
        if (!Path.IsPathRooted(config.Drives[i]))
          throw new Exception(String.Format("Path {0} is not valid (must be absolute)", config.Drives[i]));
      }

      if (!Path.IsPathRooted(config.ParityDir))
        throw new Exception(String.Format("{0} is not a valid parity path (must be absolute)", config.ParityDir));

    }

  }

}
