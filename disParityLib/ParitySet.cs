using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Security.Cryptography;

namespace disParity
{

  public class ParitySet : ProgressReporter
  {

    private List<DataDrive> drives;
    private Parity parity;
    private byte[] tempBuf = new byte[Parity.BlockSize];
    private bool cancel;

    public event EventHandler<RecoverErrorEventArgs> RecoverError;

    public ParitySet(Config config)
    {
      drives = new List<DataDrive>();
      Config = config;

      if (config.Exists) {

        ValidateConfig();

        if (!String.IsNullOrEmpty(Config.ParityDir)) {
          try {
            Directory.CreateDirectory(Config.ParityDir);
          }
          catch (Exception e) {
            throw new Exception("Could not create parity folder " + Config.ParityDir + ": " + e.Message);
          }

          Empty = true;
          ReloadDrives();

          parity = new Parity(Config);
        }
      }
    }

    public void ReloadDrives()
    {
      drives.Clear();
      foreach (Drive d in Config.Drives) {
        if (File.Exists(Path.Combine(Config.ParityDir, d.Metafile)))
          Empty = false;
        drives.Add(new DataDrive(d.Path, d.Metafile, Config));
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

    /// <summary>
    /// The config file in use by this parity set.
    /// </summary>
    public Config Config { get; private set; }

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
    /// Closes any open parity files (called when parity folder is about to move)
    /// </summary>
    public void CloseParity()
    {
      if (parity != null)
        parity.Close();
    }

    /// <summary>
    /// Close a parity set in preparation for application shutdown
    /// </summary>
    public void Close()
    {
      try {
        if (parity != null)
          parity.Close();
        Config.Save();
      }
      catch {
        // hide any errors saving config on shutdown
      }
    }

    /// <summary>
    /// Resets all data drives to state reflected in meta data
    /// </summary>
    public void Reset()
    {
      foreach (DataDrive d in drives)
        d.Reset();
    }

    /// <summary>
    /// Erase a previously created parity set.  Not currently used.
    /// </summary>
    public void Erase()
    {
      parity.DeleteAll();
      foreach (DataDrive d in drives)
        d.Clear();
      Empty = true;
    }

    // Update progress state.  Also used for RemoveAllFiles.
    private UInt32 currentUpdateBlocks;
    private UInt32 totalUpdateBlocks;

    /// <summary>
    /// Update a parity set to reflect the latest changes
    /// </summary>
    public void Update(bool scanFirst = false)
    {
      cancel = false;
      if (Empty) {
        LogFile.Log("No existing parity data found.  Creating new snapshot.");
        Create();
        return;
      }

      try {
        if (scanFirst)
          // get the current list of files on each drive and compare to old state
          ScanAll();

        if (cancel)
          return;

        // process all moves for all drives first, since that doesn't require changing
        // any parity data, only the meta data
        foreach (DataDrive d in drives)
          d.ProcessMoves();

        if (cancel)
          return;

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
          FileRecord[] deleteList = new FileRecord[d.Deletes.Count];
          d.Deletes.CopyTo(deleteList);
          foreach (FileRecord r in deleteList) {
            if (RemoveFromParity(r)) {
              deleteCount++;
              deleteSize += r.Length;
              d.Deletes.Remove(r);
            }
            if (cancel)
              return;
          }
          d.UpdateStatus();
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
          FileRecord[] addList = new FileRecord[d.Adds.Count];
          d.Adds.CopyTo(addList);
          foreach (FileRecord r in addList) {
            if (AddToParity(r)) {
              addCount++;
              addSize += r.Length;
              d.Adds.Remove(r);
            }
            if (cancel)
              return;
          }
          d.UpdateStatus();
        }
        if (addCount > 0) {
          TimeSpan elapsed = DateTime.Now - start;
          LogFile.Log("{0} file{1} ({2}) added in {3:F2} sec", addCount,
            addCount == 1 ? "" : "s", Utils.SmartSize(addSize), elapsed.TotalSeconds);
        }

      }
      finally {
        if (cancel)
          // make sure all progress bars are reset
          foreach (DataDrive d in drives)
            d.UpdateStatus();
        parity.Close();
      }

    }

    // Caution: Keep this thread safe!
    public void CancelUpdate()
    {
      LogFile.Log("Update cancelled");
      cancel = true;
      // in case we are still doing the pre-update scan
      foreach (DataDrive d in drives)
        d.CancelScan();
    }

    // Caution: Keep this thread safe!
    public void CancelRecover()
    {
      LogFile.Log("Recover cancelled");
      cancel = true;
    }

    public void CancelRemoveAll()
    {
      LogFile.Log("Remove all cancelled");
      cancel = true;
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

    /// <summary>
    /// Remove all files from the given drive from the parity set
    /// </summary>
    public void RemoveAllFiles(DataDrive drive)
    {
      totalUpdateBlocks = 0;
      cancel = false;

      // make a copy of the drive's file table to work on
      FileRecord[] files = drive.Files.ToArray();

      // get total blocks for progress reporting
      foreach (FileRecord r in files)
        totalUpdateBlocks += r.LengthInBlocks;

      currentUpdateBlocks = 0;
      ReportProgress(0);
      drive.ReportProgress(0);
      foreach (FileRecord r in files) {
        RemoveFromParity(r);
        if (cancel)
          break;
      }
      ReportProgress(0);
      drive.ReportProgress(0);
    }

    public void RemoveEmptyDrive(DataDrive drive)
    {
      if (drive.FileCount > 0)
        throw new Exception("Attempt to remove non-empty drive");

      // find the config entry for this drive
      Drive driveConfig = Config.Drives.Single(s => s.Path == drive.Root);

      // delete the meta data file, if any
      string metaFilePath = Path.Combine(Config.ParityDir, driveConfig.Metafile);
      if (File.Exists(metaFilePath))
        File.Delete(Path.Combine(Config.ParityDir, driveConfig.Metafile));

      // remove it from the config and save
      Config.Drives.Remove(driveConfig);
      Config.Save();

      // finally remove the drive from the parity set
      drives.Remove(drive);
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
      cancel = false;
      if (!ValidDrive(drive))
        throw new Exception("Invalid drive passed to Recover");
      successes = 0;
      failures = 0;
      recoverTotalBlocks = 0;
      foreach (FileRecord f in drive.Files)
        recoverTotalBlocks += f.LengthInBlocks;
      recoverBlocks = 0;
      foreach (FileRecord f in drive.Files)
        if (RecoverFile(f, path))
          successes++;
        else {
          if (cancel) {
            drive.ReportProgress(0, "");
            return;
          }
          failures++;
        }
    }

    private bool RecoverFile(FileRecord r, string path)
    {
      string fullPath = Utils.MakeFullPath(path, r.Name);
      LogFile.Log("Recovering {0}...", r.Name);
      r.Drive.ReportProgress(0, "Recovering " + r.Name + " ...");
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
            r.Drive.ReportProgress((double)(block - r.StartBlock) / r.LengthInBlocks, "");
            ReportProgress((double)(recoverBlocks + (block - r.StartBlock)) / recoverTotalBlocks);
            if (cancel) {
              f.Close();
              File.Delete(fullPath);
              r.Drive.UpdateStatus();
              return false;
            }
          }
          hash.TransformFinalBlock(parityBlock.Data, 0, 0);
        }
        r.Drive.ReportProgress(0, "");
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
        ReportProgress((double)recoverBlocks / recoverTotalBlocks);
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
      if (!r.RefreshAttributes()) {
        LogFile.Log("{0} no longer exists.", r.FullPath);
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

        if (!XORFileWithParity(r, false)) {
          LogFile.Log("Could not add {0} to parity.  File will be skipped.", r.FullPath);
          return false;
        }
      }
      r.Drive.AddFile(r);
      return true;
    }

    private bool RemoveFromParity(FileRecord r)
    {
      if (r.Length > 0) {
        string fullPath = r.FullPath;
        UInt32 startBlock = r.StartBlock;
        UInt32 endBlock = startBlock + r.LengthInBlocks;
        if (LogFile.Verbose)
          LogFile.Log("Removing {0} from blocks {1} to {2}...", fullPath, startBlock, endBlock - 1);
        else
          LogFile.Log("Removing {0}...", fullPath);

        r.Drive.FireUpdateProgress("Removing  " + fullPath, r.Drive.FileCount, r.Drive.TotalFileSize, 0);

        // Optimization: if the file still exists and is unmodified, we can remove it much faster this way
        if (!r.Modified && XORFileWithParity(r, true)) {
          r.Drive.RemoveFile(r);
          return true;
        }

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
            r.Drive.FireUpdateProgress("", r.Drive.FileCount, r.Drive.TotalFileSize,
              (double)(b - startBlock) / (double)(endBlock - startBlock));
            ReportProgress((double)currentUpdateBlocks / totalUpdateBlocks);
            if (cancel)
              return false;
          }
          change.Save();
        }
      }
      r.Drive.RemoveFile(r);
      return true;
    }

    /// <summary>
    /// XORs the data from the given file with the parity data.  This either adds the file to 
    /// parity or removes it from parity if it was already there.  If checkHash is true,
    /// it verifies the file's hash matches the hash on record before commiting the parity.
    /// If false, it updates the file's hash on record.
    /// </summary>
    private bool XORFileWithParity(FileRecord r, bool checkHash)
    {
      if (!File.Exists(r.FullPath))
        return false;
      if (r.Length == 0)
        return true;

      using (ParityChange change = new ParityChange(parity, Config, r.StartBlock, r.LengthInBlocks)) {
        byte[] data = new byte[Parity.BlockSize];
        MD5 hash = MD5.Create();
        hash.Initialize();
        UInt32 endBlock = r.StartBlock + r.LengthInBlocks;
        using (FileStream f = new FileStream(r.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read))
          for (UInt32 b = r.StartBlock; b < endBlock; b++) {
            Int32 bytesRead;
            try {
              bytesRead = f.Read(data, 0, Parity.BlockSize);
            }
            catch (Exception e) {
              LogFile.Log("Error reading {0}: {1}", r.FullPath, e.Message);
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
            r.Drive.FireUpdateProgress("", r.Drive.FileCount, r.Drive.TotalFileSize, (double)(b - r.StartBlock) / (double)(endBlock - r.StartBlock));
            ReportProgress((double)currentUpdateBlocks / totalUpdateBlocks);
            if (cancel)
              return false;
          }

        if (checkHash) {
          if (!Utils.HashCodesMatch(hash.Hash, r.HashCode)) {
            LogFile.Log("Tried to remove existing file but hash codes don't match.");
            return false;
          }
        }
        else
          r.HashCode = hash.Hash;

        change.Save(); // commit the parity change to disk
      }
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

    /// <summary>
    /// Scan all the drives.  Only called right before an update.
    /// </summary>
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

      try {
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
          ReportProgress((double)block / totalBlocks);
          block++;

          if (cancel) {
            // we can't salvage an initial update that was cancelled so we'll have to start again from scratch next time.
            LogFile.Log("Initial update cancelled.  Resetting parity to empty.");
            parity.Close();
            parity.DeleteAll();
            foreach (DataDrive d in drives) {
              d.Clear();
              d.UpdateStatus();
            }
            return;
          }

        }
      }
      finally {
        foreach (DataDrive d in drives)
          d.EndFileEnum();
        parity.Close();
      }

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

    private void FireRecoverError(string message)
    {
      if (RecoverError != null)
        RecoverError(this, new RecoverErrorEventArgs(message));
    }

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
