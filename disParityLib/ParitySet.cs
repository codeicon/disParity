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

    private List<DataDrive> drives;
    private Parity parity;
    private bool busy;
    private byte[] tempBuf = new byte[Parity.BlockSize];

    public event EventHandler<RecoverProgressEventArgs> RecoverProgress;
    public event EventHandler<UpdateProgressEventArgs> UpdateProgress;
    public event EventHandler<RecoverErrorEventArgs> RecoverError;

    public ParitySet(string ConfigFilePath)
    {
      busy = true;
      string ConfigPath = Path.Combine(ConfigFilePath, "Config.xml");
      try {
        Config = new Config(ConfigPath);
        Config.Load();
      }
      catch (Exception e) {
        throw new Exception("Could not load Config file: " + e.Message);
      }

      drives = new List<DataDrive>();

      if (Config.Exists) {

        ValidateConfig();

        if (!String.IsNullOrEmpty(Config.ParityDir)) {
          try {
            Directory.CreateDirectory(Config.ParityDir);
          }
          catch (Exception e) {
            throw new Exception("Could not create parity folder " + Config.ParityDir + ": " + e.Message);
          }

          Empty = true;
          foreach (Drive d in Config.Drives) {
            if (File.Exists(Path.Combine(Config.ParityDir, d.Metafile)))
              Empty = false;
            drives.Add(new DataDrive(d.Path, d.Metafile, Config));
          }

          parity = new Parity(Config);
        }
        busy = false;
      }
    }

    /// <summary>
    /// Returns whether or not there is any parity data generated yet for this parity set
    /// </summary>
    public bool Empty { get; private set; }

    /// <summary>
    /// List of zero or more regular expressions defining files to be ignored
    /// </summary>
    public List<string> Ignore { get; private set; }

    public Config Config { get; private set; }

    /// <summary>
    /// Returns the location of the parity data
    /// </summary>
    public string ParityPath
    {
      get
      {
        return Config.ParityDir;
      }
    }

    /// <summary>
    /// Returns whether or not one or more drives are busy doing a parity operation
    /// </summary>
    public bool Busy
    {
      get
      {
        if (busy)
          return true;
        foreach (DataDrive d in drives)
          if (d.Busy)
            return true;
        return false;
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
    /// Close a parity set in preparation for application shutdown
    /// </summary>
    public void Close()
    {
      try {
        Config.Save();
      }
      catch {
        // hide any errors saving config on shutdown
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

    // update progress state
    private UInt32 currentUpdateBlocks;
    private UInt32 totalUpdateBlocks;

    /// <summary>
    /// Update a parity set to reflect the latest changes
    /// </summary>
    public void Update(bool scanFirst = false)
    {
      if (Empty) {
        LogFile.Log("No existing parity data found.  Creating new snapshot.");
        Create();
        return;
      }

      busy = true;
      try {
        if (scanFirst)
          // get the current list of files on each drive and compare to old state
          ScanAll();

        // process all moves for all drives first, since that doesn't require changing
        // any parity data, only the meta data
        foreach (DataDrive d in drives)
          d.ProcessMoves();

        // count total blocks for this update, for progress reporting
        currentUpdateBlocks = 0;
        totalUpdateBlocks = 0;
        foreach (DataDrive d in drives) {
          foreach (FileRecord r in d.Adds)
            totalUpdateBlocks += r.LengthInBlocks;
          foreach (FileRecord r in d.Deletes)
            totalUpdateBlocks += r.LengthInBlocks;
        }

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
          d.ClearDeletes();
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
          d.ClearAdds();
        }
        if (addCount > 0) {
          TimeSpan elapsed = DateTime.Now - start;
          LogFile.Log("{0} file{1} ({2}) added in {3:F2} sec", addCount,
            addCount == 1 ? "" : "s", Utils.SmartSize(addSize), elapsed.TotalSeconds);
        }

        parity.Close();
      }
      finally {
        busy = false;
      }

    }

    private bool ValidDrive(DataDrive drive)
    {
      foreach (DataDrive d in drives)
        if (d == drive)
          return true;
      return false;
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
      // to do: Check here that this drive is not alredy in the list!
      string metaFile = FindAvailableMetafileName();

      // make sure there isn't already a file there with this name, if there is, rename it
      string fullPath = Path.Combine(Config.ParityDir, metaFile);
      if (File.Exists(fullPath))
        File.Move(fullPath, Path.ChangeExtension(fullPath, ".old"));

      DataDrive newDrive = new DataDrive(path, metaFile, Config);
      drives.Add(newDrive);

      // update Config and save
      Config.Drives.Add(new Drive(path, metaFile));
      Config.Save();

      return newDrive;
    }

    private string FindAvailableMetafileName()
    {
      int fileNo = 0;
      bool found = true;
      string metaFile = "";
      while (found) {
        fileNo++;
        metaFile = String.Format("files{0}.dat", fileNo);
        found = false;
        foreach (Drive d in Config.Drives)
          if (d.Metafile == metaFile) {
            found = true;
            break;
          }
      }
      return metaFile;
    }

    // Recover state variables
    private UInt32 recoverTotalBlocks;
    private UInt32 recoverBlocks;

    /// <summary>
    /// Recover all files from the given drive to the given location
    /// </summary>
    public void Recover(DataDrive drive, string path, out int successes, out int failures)
    {
      if (!ValidDrive(drive))
        throw new Exception("Invalid drive passed to Recover");
      successes = 0;
      failures = 0;
      busy = true;
      recoverTotalBlocks = 0;
      foreach (FileRecord f in drive.Files)
        recoverTotalBlocks += f.LengthInBlocks;
      recoverBlocks = 0;
      try {
        foreach (FileRecord f in drive.Files)
          if (RecoverFile(f, path))
            successes++;
          else
            failures++;
      }
      finally {
        busy = false;
      }
    }

    private bool RecoverFile(FileRecord r, string path)
    {
      string fullPath = Utils.MakeFullPath(path, r.Name);
      LogFile.Log("Recovering {0}...", r.Name);
      r.Drive.FireProgressReport("Recovering " + r.Name + " ...", 0);
      try {
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
            if ((block % 10) == 0) { // report progress every 10 blocks
              r.Drive.FireProgressReport("", (double)(block - r.StartBlock) / r.LengthInBlocks);
              FireRecoverProgress("", (double)(recoverBlocks + (block - r.StartBlock)) / recoverTotalBlocks);
            }
          }
          hash.TransformFinalBlock(parityBlock.Data, 0, 0);
        }
        r.Drive.FireProgressReport("", 0);
        File.SetCreationTime(fullPath, r.CreationTime);
        File.SetLastWriteTime(fullPath, r.LastWriteTime);
        File.SetAttributes(fullPath, r.Attributes);
        if (r.Length > 0 && !Utils.HashCodesMatch(hash.Hash, r.HashCode)) {
          LogFile.Log("ERROR: hash verify FAILED for {0}", fullPath);
          FireRecoverError("Hash verify failed for \"" + fullPath + "\".  Recovered file is probably corrupt.");
          return false;
        }
        else
          return true;
      }
      catch (Exception e) {
        LogFile.Log("ERROR recovering {0} : {1}", fullPath, e.Message);
        FireRecoverError("Error recovering \"" + fullPath + "\": " + e.Message);
        return false;
      }
      finally {
        // no matter what happens, keep the progress bar advancing by the right amount
        recoverBlocks += r.LengthInBlocks;
        FireRecoverProgress("", (double)recoverBlocks / recoverTotalBlocks);
      }
    }

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
              "Available: {3})", Config.ParityDir, fullPath, Utils.SmartSize(required), Utils.SmartSize(available));
            return false;
          }
        }

        r.StartBlock = startBlock;
        if (LogFile.Verbose)
          LogFile.Log("Adding {0} to blocks {1} to {2}...", fullPath, startBlock, endBlock - 1);
        else
          LogFile.Log("Adding {0}...", fullPath);

        r.Drive.FireUpdateProgress("Adding  " + fullPath, r.Drive.FileCount, r.Drive.TotalFileSize, 0);

        byte[] data = new byte[Parity.BlockSize];
        MD5 hash = MD5.Create();
        hash.Initialize();
        using (ParityChange change = new ParityChange(parity, Config, startBlock, r.LengthInBlocks)) {
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
              currentUpdateBlocks++;
              // only report once every 10 blocks
              if ((currentUpdateBlocks % 10) == 0) {
                r.Drive.FireUpdateProgress("", r.Drive.FileCount, r.Drive.TotalFileSize, (double)(b - startBlock) / (double)(endBlock - startBlock));
                FireUpdateProgress((double)currentUpdateBlocks / totalUpdateBlocks);
              }
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

        r.Drive.FireUpdateProgress("Removing  " + fullPath, r.Drive.FileCount, r.Drive.TotalFileSize, 0);

        // Recalulate parity from scratch for all blocks that contained the deleted file's data.
        using (ParityChange change = new ParityChange(parity, Config, startBlock, r.LengthInBlocks)) {
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
                return false;
              }
            }
            change.Write();
            currentUpdateBlocks++;
            // only report once every 10 blocks
            if ((currentUpdateBlocks % 10) == 0) {
              r.Drive.FireUpdateProgress("", r.Drive.FileCount, r.Drive.TotalFileSize,
                (double)(b - startBlock) / (double)(endBlock - startBlock));
              FireUpdateProgress((double)currentUpdateBlocks / totalUpdateBlocks);
            }

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
      // TO DO: check free space on parity drive here?

      UInt32 totalBlocks = 1; // make it one so no chance of divide-by-zero below
      foreach (DataDrive d in drives) {
        d.BeginFileEnum();
        UInt32 scanBlocks = d.TotalBlocks;
        if (scanBlocks > totalBlocks)
          totalBlocks = scanBlocks;
      }

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
        // only report once every 10 blocks
        if ((block % 10) == 0)
          FireUpdateProgress((double)block / totalBlocks);
        block++;
      }
      parity.Close();

    }

    private void ValidateConfig()
    {
      // this is now a valid condition
      //if (Config.Drives.Count == 0)
      //  throw new Exception("No drives found in " + Config.Filename);

      // Make sure all data paths are set and valid
      for (int i = 0; i < Config.Drives.Count; i++) {
        if (Config.Drives[i] == null)
          throw new Exception(String.Format("Path {0} is not set (check {1})", i + 1, Config.Filename));
        if (!Path.IsPathRooted(Config.Drives[i].Path))
          throw new Exception(String.Format("Path {0} is not valid (must be absolute)", Config.Drives[i]));
      }

      if (!String.IsNullOrEmpty(Config.ParityDir) && !Path.IsPathRooted(Config.ParityDir))
        throw new Exception(String.Format("{0} is not a valid parity path (must be absolute)", Config.ParityDir));
    }

    private void FireRecoverProgress(string filename, double progress)
    {
      if (RecoverProgress != null)
        RecoverProgress(this, new RecoverProgressEventArgs(filename, progress));
    }

    private void FireRecoverError(string message)
    {
      if (RecoverError != null)
        RecoverError(this, new RecoverErrorEventArgs(message));
    }

    private void FireUpdateProgress(double progress)
    {
      if (UpdateProgress != null)
        UpdateProgress(this, new UpdateProgressEventArgs("", 0, 0, progress));
    }

  }

  public class RecoverProgressEventArgs : EventArgs
  {
    public RecoverProgressEventArgs(string filename, double progress)
    {
      Filename = filename;
      Progress = progress;
    }

    public string Filename { get; private set; }
    public double Progress { get; private set; }

  }

  public class RecoverErrorEventArgs : EventArgs
  {
    public RecoverErrorEventArgs(string msg)
    {
      Message = msg;
    }

    public string Message { get; private set; }
  }

}
