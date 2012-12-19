using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Security.Cryptography;
using Microsoft.Win32;

namespace disParity
{

  public class ParitySet : ProgressReporter
  {

    private List<DataDrive> drives;
    private Parity parity;
    private byte[] tempBuf = new byte[Parity.BLOCK_SIZE];
    private bool cancel;
    private HashSet<string> errorFiles = new HashSet<string>(); // files that generated errors during an operation; tracked here to avoid complaining about the same file over and over

    // For a reportable generated during a long-running operation (Recover, Verify, etc.)
    public event EventHandler<ErrorMessageEventArgs> ErrorMessage;

    public ParitySet(Config config)
    {
      drives = new List<DataDrive>();
      Config = config;
      parity = new Parity(Config);
      Empty = true;

      if (config.Exists) {

        ValidateConfig();

        if (!String.IsNullOrEmpty(Config.ParityDir)) {
          try {
            Directory.CreateDirectory(Config.ParityDir);
          }
          catch (Exception e) {
            throw new Exception("Could not create parity folder " + Config.ParityDir + ": " + e.Message);
          }

          ReloadDrives();

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
      // try to record how many drives in the registry
      try {
        Registry.SetValue("HKEY_CURRENT_USER\\Software\\disParity", "dc", Config.Drives.Count,
              RegistryValueKind.DWord);
        Registry.SetValue("HKEY_CURRENT_USER\\Software\\disParity", "mpb", MaxParityBlock(),
              RegistryValueKind.DWord);
      }
      catch { }
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

        LogFile.Log("Beginning update");

        if (cancel)
          return;

        ReportProgress(0);
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
        foreach (DataDrive d in drives) {
          d.UpdateStatus();
          d.Status = "";
        }
        // make sure all progress bars are reset
        foreach (DataDrive d in drives)
          d.UpdateFinished();
        parity.Close();
        LogFile.Log("Update complete");
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

    // Caution: Keep this thread safe!
    public void CancelVerify()
    {
      LogFile.Log("Verify cancelled");
      cancel = true;
      // in case we are still doing the pre-verify scan
      foreach (DataDrive d in drives)
        d.CancelScan();
    }

    public void CancelHashcheck()
    {
      LogFile.Log("Hashcheck cancelled");
      cancel = true;
    }

    private bool ValidDrive(DataDrive drive)
    {
      foreach (DataDrive d in drives)
        if (d == drive)
          return true;
      return false;
    }

    public void HashCheck(DataDrive drive)
    {
      cancel = false;
      int files = 0;
      int failures = 0;
      UInt32 totalBlocks = 0;
      foreach (FileRecord r in drive.Files)
        totalBlocks += r.LengthInBlocks;
      ReportProgress(0);
      UInt32 blocks = 0;
      byte[] buf = new byte[Parity.BLOCK_SIZE];
      try {
        using (MD5 hash = MD5.Create()) {
          foreach (FileRecord r in drive.Files) {
            files++;
            if (r.Length == 0) {
              continue;
            }
            drive.ReportProgress(0);
            drive.Status = "Checking " + r.FullPath;
            hash.Initialize();
            int read = 0;
            int b = 0;
            using (FileStream s = new FileStream(r.FullPath, FileMode.Open, FileAccess.Read)) {
              while (!cancel && ((read = s.Read(buf, 0, Parity.BLOCK_SIZE)) > 0)) {
                hash.TransformBlock(buf, 0, read, buf, 0);
                drive.ReportProgress((double)b++ / r.LengthInBlocks);
                ReportProgress((double)blocks++ / totalBlocks);
              }
            }
            if (cancel)
              return;
            hash.TransformFinalBlock(buf, 0, 0);
            if (!Utils.HashCodesMatch(hash.Hash, r.HashCode)) {
              FireErrorMessage(r.FullPath + " hash check failed");
              failures++;
            }
            Status = String.Format("Hash check in progress.  Files checked: {0} Failures: {1}", files, failures);
          }
          drive.Status = "";
          if (failures == 0)
            Status = "Hash check of " + drive.Root + " complete.  No errors found";
          else
            Status = "Hash check of " + drive.Root + " complete.  Errors: " + failures;
        }
      }
      finally {
        drive.ReportProgress(0);
        drive.Status = "";
      }
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
      errorFiles.Clear();
      ReportProgress(0);
      foreach (FileRecord f in drive.Files)
        recoverTotalBlocks += f.LengthInBlocks;
      recoverBlocks = 0;
      foreach (FileRecord f in drive.Files)
        if (RecoverFile(f, path))
          successes++;
        else {
          if (cancel) {
            drive.ReportProgress(0);
            return;
          }
          failures++;
        }
    }

    private bool RecoverFile(FileRecord r, string path)
    {
      string fullPath = Utils.MakeFullPath(path, r.Name);
      r.Drive.Status = "Recovering " + r.Name + " ...";
      LogFile.Log(r.Drive.Status);
      r.Drive.ReportProgress(0);
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
            int blockSize = leftToWrite > Parity.BLOCK_SIZE ? Parity.BLOCK_SIZE : (int)leftToWrite;
            f.Write(parityBlock.Data, 0, blockSize);
            hash.TransformBlock(parityBlock.Data, 0, blockSize, parityBlock.Data, 0);
            leftToWrite -= Parity.BLOCK_SIZE;
            block++;
            r.Drive.ReportProgress((double)(block - r.StartBlock) / r.LengthInBlocks);
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
        r.Drive.ReportProgress(0);
        File.SetCreationTime(fullPath, r.CreationTime);
        File.SetLastWriteTime(fullPath, r.LastWriteTime);
        File.SetAttributes(fullPath, r.Attributes);
        if (r.Length > 0 && !Utils.HashCodesMatch(hash.Hash, r.HashCode)) {
          FireErrorMessage("Hash verify failed for \"" + fullPath + "\".  Recovered file is probably corrupt.");
          return false;
        }
        else
          return true;
      }
      catch (Exception e) {
        FireErrorMessage("Error recovering \"" + fullPath + "\": " + e.Message);
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
      FileRecord r;
      parity.Load(block);
      foreach (DataDrive d in drives)
        if (d != drive) {
          string error = "";
          try {
            if (d.ReadBlock(block, tempBuf, out r)) {
              parity.Add(tempBuf);
              if (r.Modified)
                error = String.Format("Warning: {0} has been modified.  Some recovered files may be corrupt.", r.FullPath);
            } else if (r != null && !File.Exists(r.FullPath))
              error = String.Format("Warning: {0} could not be found.  Some recovered files may be corrupt.", r.FullPath);
          }
          catch (Exception e) {
            error = e.Message; // ReadBlock should have constructed a nice error message for us
          }
          if (error != "" && errorFiles.Add(error))
            FireErrorMessage(error);
        }
    }

    private bool AddToParity(FileRecord r)
    {
      string fullPath = r.FullPath;
      // file may have been deleted, or attributes may have changed since we scanned, so refresh
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
          long required = (endBlock - MaxParityBlock()) * Parity.BLOCK_SIZE;
          long available = parity.FreeSpace;
          if ((available != -1) && (available < required)) {
            FireErrorMessage(String.Format("Insufficient space available on {0} to process " +
              "{1}.  File will be skipped this update. (Required: {2} " +
              "Available: {3})", Config.ParityDir, fullPath, Utils.SmartSize(required), Utils.SmartSize(available)));
            return false;
          }
        }

        r.StartBlock = startBlock;
        if (LogFile.Verbose)
          LogFile.Log("Adding {0} to blocks {1} to {2}...", fullPath, startBlock, endBlock - 1);
        else
          LogFile.Log("Adding {0}...", fullPath);

        //r.Drive.FireUpdateProgress("Adding  " + fullPath, r.Drive.FileCount, r.Drive.TotalFileSize, 0);
        r.Drive.Status = "Adding " + fullPath;

        if (!XORFileWithParity(r, false)) {
          // assume FireErrorMessage was already called
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

        //r.Drive.FireUpdateProgress("Removing  " + fullPath, r.Drive.FileCount, r.Drive.TotalFileSize, 0);
        r.Drive.Status = "Removing  " + fullPath;

        // Optimization: if the file still exists and is unmodified, we can remove it much faster this way
        if (!r.Modified && XORFileWithParity(r, true)) {
          r.Drive.RemoveFile(r);
          return true;
        }

        UInt32 totalProgresBlocks = r.LengthInBlocks + r.LengthInBlocks / 10;

        // Recalulate parity from scratch for all blocks that contained the deleted file's data.
        using (ParityChange change = new ParityChange(parity, Config, startBlock, r.LengthInBlocks)) 
        try {

          byte[] data = new byte[Parity.BLOCK_SIZE];
          for (UInt32 b = startBlock; b < endBlock; b++) {
            change.Reset(false);
            foreach (DataDrive d in drives) {
              if (d == r.Drive)
                continue;
              // Note it's possible that this file may also have been deleted. That's OK, ReadFileData 
              // returns false and we don't try to add the deleted file to the parity.
              FileRecord f;
              try {
                if (d.ReadBlock(b, data, out f))
                  change.AddData(data);
              }
              catch (Exception e) {
                FireErrorMessage(e.Message);
                return false;
              }
            }
            change.Write();
            currentUpdateBlocks++;
            r.Drive.ReportProgress((double)(b - startBlock) / totalProgresBlocks);
            ReportProgress((double)currentUpdateBlocks / totalUpdateBlocks);
            if (cancel)
              return false;
          }

          FlushTempParity(r.Drive, change);

        } catch (Exception e) {
          FireErrorMessage(String.Format("Error removing {0}: {1}", r.FullPath, e.Message));
          return false;
        }
      }
      r.Drive.RemoveFile(r);
      return true;
    }

    private void FlushTempParity(DataDrive drive, ParityChange change)
    {
      bool saveInProgress = true;
      Task.Factory.StartNew(() =>
      {
        try {
          change.Save();
        }
        catch {
        }
        finally {
          saveInProgress = false;
        }
      });

      while (saveInProgress) {
        Thread.Sleep(20);
        drive.ReportProgress(0.9 + 0.1 * change.SaveProgress);
      }

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
        byte[] data = new byte[Parity.BLOCK_SIZE];
        MD5 hash = MD5.Create();
        hash.Initialize();
        UInt32 endBlock = r.StartBlock + r.LengthInBlocks;
        UInt32 totalProgresBlocks = r.LengthInBlocks + r.LengthInBlocks / 10;

        FileStream f;
        try {
          f = new FileStream(r.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        }
        catch (Exception e) {
          FireErrorMessage(String.Format("Error opening {0}: {1}", r.FullPath, e.Message));
          return false;
        }
        try {
          for (UInt32 b = r.StartBlock; b < endBlock; b++) {
            Int32 bytesRead;
            try {
              bytesRead = f.Read(data, 0, Parity.BLOCK_SIZE);
            }
            catch (Exception e) {
              FireErrorMessage(String.Format("Error reading {0}: {1}", r.FullPath, e.Message));
              return false;
            }
            if (b == (endBlock - 1))
              hash.TransformFinalBlock(data, 0, bytesRead);
            else
              hash.TransformBlock(data, 0, bytesRead, data, 0);
            while (bytesRead < Parity.BLOCK_SIZE)
              data[bytesRead++] = 0;
            change.Reset(true);
            change.AddData(data);
            change.Write();
            currentUpdateBlocks++;
            r.Drive.ReportProgress((double)(b - r.StartBlock) / totalProgresBlocks);
            ReportProgress((double)currentUpdateBlocks / totalUpdateBlocks);
            if (cancel)
              return false;
          }
        }
        catch (Exception e) {
          FireErrorMessage(String.Format("Unexpected error while processing {0}: {1}", r.FullPath, e.Message));
          return false;
        }
        finally {
          f.Dispose();
        }

        if (checkHash) {
          if (!Utils.HashCodesMatch(hash.Hash, r.HashCode)) {
            LogFile.Log("Tried to remove existing file but hash codes don't match.");
            return false;
          }
        }
        else
          r.HashCode = hash.Hash;

        FlushTempParity(r.Drive, change); // commit the parity change to disk
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
        UInt32 scanBlocks = d.TotalScanBlocks;
        if (scanBlocks > totalBlocks)
          totalBlocks = scanBlocks;
      }

      try {
        ParityBlock parityBlock = new ParityBlock(parity);
        byte[] dataBuf = new byte[Parity.BLOCK_SIZE];
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
            foreach (DataDrive d in drives)
              d.Clear();
            return;
          }

        }
      }
      finally {
        foreach (DataDrive d in drives)
          d.EndFileEnum();
        parity.Close();
        if (!cancel)
          Empty = false;
      }

    }

    public void Verify()
    {
      cancel = false;
      MD5 hash = MD5.Create();
      UInt32 maxBlock = MaxParityBlock();
      UInt32 errors = 0;
      List<FileRecord> suspectFiles = new List<FileRecord>();
      DateTime lastStatus = DateTime.Now;
      TimeSpan minTimeDelta = TimeSpan.FromMilliseconds(100); // don't update status more than 10x per second

      ReportProgress(0);

      FileRecord r;
      ParityBlock parityBlock = new ParityBlock(parity);
      ParityBlock calculatedParityBlock = new ParityBlock(parity);
      byte[] buf = new byte[Parity.BLOCK_SIZE];
      for (UInt32 block = 0; block < maxBlock; block++) {
        parityBlock.Load(block);
        bool firstRead = true;
        foreach (DataDrive d in drives)
          try {
            if (firstRead) {
              if (d.ReadBlock(block, calculatedParityBlock.Data, out r))
                firstRead = false;
            }
            else if (d.ReadBlock(block, buf, out r))
              calculatedParityBlock.Add(buf);
          }
          catch (Exception e) {
            FireErrorMessage(e.Message);
          }
        if (firstRead)
          // no blocks were read, this block should be empty
          calculatedParityBlock.Clear();
        if (!calculatedParityBlock.Equals(parityBlock)) {
          FireErrorMessage(String.Format("Block {0} does not match", block));
          errors++;
          bool reported = false;
          foreach (DataDrive dr in drives) {
            FileRecord f = dr.FileFromBlock(block);
            if (f != null && !suspectFiles.Contains(f)) {
              suspectFiles.Add(f);
              if (!reported) {
                FireErrorMessage("Block " + block + " contains data from the following file or files (each file will only be reported once per verify pass):");
                reported = true;
              }
              FireErrorMessage(f.FullPath);
            }
          }
        }
        if ((DateTime.Now - lastStatus) > minTimeDelta) {
          Status = String.Format("{0} of {1} parity blocks verified. Errors found: {2}", block, maxBlock, errors);
          lastStatus = DateTime.Now;
        }
        ReportProgress((double)block / maxBlock);
        if (cancel)
          break;
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

    private void FireErrorMessage(string message)
    {
      LogFile.Log(message);
      if (ErrorMessage != null)
        ErrorMessage(this, new ErrorMessageEventArgs(message));
    }

    #region Properties

    private string status;
    public string Status
    {
      get
      {
        return status;
      }
      set
      {
        SetProperty(ref status, "Status", value);
      }
    }

    #endregion
  }

  /// <summary>
  /// For a reportable generated during a long-running operation (Recover, Verify, etc.)
  /// </summary>
  public class ErrorMessageEventArgs : EventArgs
  {
    public ErrorMessageEventArgs(string msg)
    {
      Message = msg;
    }

    public string Message { get; private set; }
  }

}
