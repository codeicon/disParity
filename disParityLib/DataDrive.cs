using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.IO;
using System.Diagnostics;
using System.Timers;

namespace disParity
{

  public enum DriveStatus
  {
    ScanRequired,
    UpdateRequired,
    UpToDate,
    AccessError,
    ReadingFile,
    Scanning
  }

  public class DataDrive : ProgressReporter
  {

    private string root;
    private string metaFile;
    private Dictionary<string, FileRecord> files; // Master list of protected files; should reflect what is currently in files.dat at all times
    private List<FileRecord> scanFiles = new List<FileRecord>(); // List of current files on the drive as seen this scan, non-empty only during a scan
    private Dictionary<string, FileRecord> seenFileNames = new Dictionary<string, FileRecord>(); // for fast lookups, also only non-empty during a scan
    private MD5 hash;
    private Config config;
    private ProgressEstimator scanProgress;
    private bool cancelScan;
    private Timer fileCloseTimer;
    private object fileCloseLock = new object();

    const UInt32 META_FILE_VERSION = 1;

    public event EventHandler<EventArgs> ScanCompleted;

    public DataDrive(string root, string metaFile, Config config)
    {
      this.root = root;
      this.metaFile = metaFile;
      this.config = config;

      if (!root.StartsWith(@"\\")) {
        string drive = Path.GetPathRoot(root);
        if (!String.IsNullOrEmpty(drive))
          try {
            DriveInfo driveInfo = new DriveInfo(drive);
            DriveType = driveInfo.DriveType;
            VolumeLabel = driveInfo.VolumeLabel;
            TotalSpace = driveInfo.TotalSize;
          }
          catch {
            DriveType = DriveType.Unknown;
          }
      }
      else
        DriveType = DriveType.Network;

      hash = MD5.Create();
      files = new Dictionary<string, FileRecord>();
      Reset();

      fileCloseTimer = new Timer(1000); // keep cached read files open for at most one second
      fileCloseTimer.AutoReset = false;
      fileCloseTimer.Elapsed += HandleFileCloseTimer;
    }

    private string MetaFilePath
    {
      get { return Path.Combine(config.ParityDir, metaFile); }
    }

    public string Root { get { return root; } }

    public UInt32 MaxBlock { get; private set; }

    public IEnumerable<FileRecord> Files { get {  return files.Values; } }

    public string LastError { get; private set; }

    public DriveType DriveType { get; private set; }

    public string VolumeLabel { get; private set; }

    public long FreeSpace 
    {
      get
      {
        try {
          DriveInfo driveInfo = new DriveInfo(Path.GetPathRoot(root));
          return driveInfo.TotalFreeSpace;
        }
        catch {
          return 0;
        }
      }
    }

    public long TotalSpace { get; private set; }


    /// <summary>
    /// Returns the total size of all files in the adds list, in blocks.
    /// Currently only used for progress reporting during creates.
    /// </summary>
    public UInt32 TotalScanBlocks
    {
      get
      {
        UInt32 result = 0;
        foreach (FileRecord r in adds)
          result += r.LengthInBlocks;
        return result;
      }
    }

    /// <summary>
    /// Clears all state of the DataDrive, resetting to empty (deletes on-disk meta data as well.)
    /// </summary>
    public void Clear()
    {
      if (File.Exists(MetaFilePath))
        File.Delete(MetaFilePath);
      files.Clear();
      FileCount = 0;
      TotalFileSize = 0;
      DriveStatus = DriveStatus.ScanRequired;
    }

    /// <summary>
    /// Resets state to on-disk meta data
    /// </summary>
    public void Reset()
    {
      files.Clear();
      FileCount = 0;
      MaxBlock = 0;
      if (File.Exists(MetaFilePath))
        LoadFileList();
      DriveStatus = DriveStatus.ScanRequired;
    }

    /// <summary>
    /// Scans the drive from 'root' down, generating the current list of files on the drive.
    /// Files that should not be protected (e.g. Hidden files) are not included.
    /// </summary>
    public void Scan()
    {
      DriveStatus = DriveStatus.Scanning;
      cancelScan = false;
      ReportProgress(0);
      try {

        // Convert list of ignores to a list of Regex
        List<Regex> ignores = new List<Regex>();
        foreach (string i in config.Ignores) {
          string pattern = Regex.Escape(i.ToLower());       // Escape the original string
          pattern = pattern.Replace(@"\?", ".");  // Replace all \? with .
          pattern = pattern.Replace(@"\*", ".*"); // Replace all \* with .*
          ignores.Add(new Regex(pattern));
        }

        scanFiles.Clear();
        LogFile.Log("Scanning {0}...", root);
        scanProgress = null;
        try {
          Scan(new DirectoryInfo(root), ignores);
          if (!cancelScan) {
            long totalSize = 0;
            foreach (FileRecord f in scanFiles)
              totalSize += f.Length;
            Status = "Scan complete. Analyzing results...";
            ReportProgress(1.0);
            Compare();
            LogFile.Log("{0}: Found {1} file{2} ({3} total) Adds: {4} Deletes: {5} Moves: {6} Edits: {7}", Root, scanFiles.Count,
              scanFiles.Count == 1 ? "" : "s", Utils.SmartSize(totalSize), adds.Count, deletes.Count, moves.Count, edits.Count);
            if (cancelScan) {
              DriveStatus = DriveStatus.ScanRequired;
              return;
            }
            // process moves now as part of the scan, since they don't require changes to parity
            ProcessMoves();
          }
          else
            LogFile.Log("{0}: Scan cancelled", Root);
        }
        catch (Exception e) {
          LogFile.Log("Could not scan {0}: {1}", root, e.Message);
          LastError = e.Message;
          DriveStatus = DriveStatus.AccessError;
          return;
        }
      }
      finally {
        ReportProgress(0);
        UpdateStatus();
        scanFiles.Clear();
        seenFileNames.Clear();
        FireScanCompleted();
      }
    }

    private void Scan(DirectoryInfo dir, List<Regex> ignores, ProgressEstimator progress = null)
    {
      if (cancelScan)
        return;
      Status = "Scanning " + dir.FullName;
      if (scanProgress != null)
        ReportProgress(scanProgress.Progress);
      DirectoryInfo[] subDirs;
      try {
        subDirs = dir.GetDirectories();
      }
      catch (Exception e) {
        if (progress == null)
          throw;
        LogFile.Log("Warning: Could not enumerate subdirectories of {0}: {1}", dir.FullName, e.Message);
        return;
      }
      FileInfo[] fileInfos;
      try {
        fileInfos = dir.GetFiles();
      }
      catch (Exception e) {
        LogFile.Log("Warning: Could not enumerate files in {0}: {1}", dir.FullName, e.Message);
        return;
      }

      ProgressEstimator folderProgress;
      if (scanProgress == null) {
        scanProgress = new ProgressEstimator();
        scanProgress.Reset(subDirs.Length);
        folderProgress = scanProgress;
      }
      else
        folderProgress = progress.BeginSubPhase(subDirs.Length);

      foreach (DirectoryInfo d in subDirs) {
        if (cancelScan)
          return;
        if ((config.IgnoreHidden && (d.Attributes & FileAttributes.Hidden) != 0) ||
            ((d.Attributes & FileAttributes.System) != 0)) {
          folderProgress.EndPhase();
          continue;
        }
        Scan(d, ignores, folderProgress);
        folderProgress.EndPhase();
      }
      ReportProgress(scanProgress.Progress);
      string relativePath = Utils.StripRoot(root, dir.FullName);
      foreach (FileInfo f in fileInfos) {
        if (cancelScan)
          return;
        if (f.Attributes == (FileAttributes)(-1))
          continue;
        if (config.IgnoreHidden && (f.Attributes & FileAttributes.Hidden) != 0)
          continue;
        if ((f.Attributes & FileAttributes.System) != 0)
          continue;
        bool ignore = false;
        foreach (Regex regex in ignores)
          if (regex.IsMatch(f.Name.ToLower())) {
            ignore = true;
            break;
          }
        if (ignore) {
          LogFile.Log("Skipping {0} because it matches an ignore...", f.FullName);
          continue;
        }
        FileRecord r = new FileRecord(f, relativePath, this);
        scanFiles.Add(r);
        seenFileNames[r.Name.ToLower()] = r;
      }
    }

    /// <summary>
    /// Called from ParitySet when an update has completed.  We can clean up all scan-related stuff here.
    /// </summary>
    public void UpdateFinished()
    {
      // Clear() adds, deletes, etc. here???
      ReportProgress(0);
    }

    // Caution: Keep this thread safe!
    public void CancelScan()
    {
      cancelScan = true;
    }

    public void FireScanCompleted()
    {
      if (ScanCompleted != null)
        ScanCompleted(this, new EventArgs());
    }

    // the "deletes" list contains FileRecords from the master files list
    private List<FileRecord> deletes = new List<FileRecord>();
    public List<FileRecord> Deletes { get { return deletes; } }

    // the "adds" list contains FileRecords from the new scanFiles list
    private List<FileRecord> adds = new List<FileRecord>();
    public List<FileRecord> Adds { get { return adds; } }

    // the "edits" list contains FileRecords from the master files list
    // Note that edited files are also in the deletes and adds list
    private List<FileRecord> edits = new List<FileRecord>();
    public List<FileRecord> Edits { get { return edits; } }

    // the "moves" dictionary maps old file names to new entries from the scanFiles list
    private Dictionary<string, FileRecord> moves = new Dictionary<string, FileRecord>();

    /// <summary>
    /// Compare the old list of files with the new list in order to
    /// determine which files had been added, removed, moved, or edited.
    /// </summary>
    private void Compare()
    {
      adds.Clear();
      deletes.Clear();
      moves.Clear();
      edits.Clear();

      // build list of new files we haven't seen before (adds)
      foreach (FileRecord r in scanFiles)
        if (!files.ContainsKey(r.Name.ToLower()))
          adds.Add(r);

      // build list of old files we don't see now (deletes)
      foreach (var kvp in files)
        if (!seenFileNames.ContainsKey(kvp.Key))
          deletes.Add(kvp.Value);
      
      // some of the files in add/delete list might actually be moves, check for that
      foreach (FileRecord a in adds) {
        byte[] hashCode = null;
        if (a.Length > 0)
          foreach (FileRecord d in deletes)
            if (a.Length == d.Length && a.LastWriteTime == d.LastWriteTime) {
              // probably the same file, but we need to check the hash to be sure
              if (hashCode == null)
                hashCode = ComputeHash(a);
              if (Utils.HashCodesMatch(hashCode, d.HashCode)) {
                LogFile.Log("{0} moved to {1}", Utils.MakeFullPath(root, d.Name), Utils.MakeFullPath(root, a.Name));
                moves[d.Name.ToLower()] = a;
              }
            }
      }

      // remove the moved files from the add and delete lists
      foreach (var kvp in moves) {
        FileRecord delete = null;
        foreach (FileRecord r in deletes)
          if (String.Equals(kvp.Key, r.Name.ToLower())) {
            delete = r;
            break;
          }
        if (delete != null)
          deletes.Remove(delete);
        adds.Remove(kvp.Value);
      }

      // now check for edits
      foreach (var kvp in files) {
        if (cancelScan)
          return;
        FileRecord n;
        // a file can only be edited if the file name was seen this can
        if (seenFileNames.TryGetValue(kvp.Key, out n)) {
          // if we detect an edit, we add the "new" version of the file to the adds list, 
          // because it has the new attributes and we want those saved later.  The old value goes
          // into the edits and deletes lists.
          if (kvp.Value.Length != n.Length) {
            edits.Add(kvp.Value);
            deletes.Add(kvp.Value);
            adds.Add(n);
          } else if (kvp.Value.LastWriteTime != n.LastWriteTime)
            // length hasn't changed but timestamp says file was modified, check hash code to be sure it has changed
            if (!HashCheck(kvp.Value)) {
              edits.Add(kvp.Value);
              deletes.Add(kvp.Value);
              adds.Add(n);
            }
        }
      }

    }

    /// <summary>
    /// Process moves by updating their records to reflect the new locations
    /// </summary>
    private void ProcessMoves()
    {
      if (moves.Count == 0)
        return;
      // the moves dictionary maps old file names to new FileRecords
      LogFile.Log("Processing moves for {0}...", root);
      foreach (var kvp in moves) {
        // find the old entry
        FileRecord old;
        if (!files.TryGetValue(kvp.Key, out old))
          throw new Exception("Unable to locate moved file " + kvp.Key + " in master file table");
        // remove it from the table
        files.Remove(kvp.Key);
        // update new record to carry over meta data from the old one
        kvp.Value.StartBlock = old.StartBlock;
        kvp.Value.HashCode = old.HashCode;
        // store new record in master file table
        files[kvp.Value.Name.ToLower()] = kvp.Value;
      }
      // save updated master file table
      SaveFileList();
      // clear moves list, don't need it anymore
      moves.Clear();
    }

    /// <summary>
    /// Removes the file from the master files list and saves the new list to disk
    /// </summary>
    public void RemoveFile(FileRecord r)
    {
      string filename = r.Name.ToLower();
      Debug.Assert(files.ContainsKey(filename));
      files.Remove(filename);
      SaveFileList();
      FileCount--;
    }

    /// <summary>
    /// Adds the file to newFiles and saves the new list to disk
    /// </summary>
    public void AddFile(FileRecord r)
    {
      string filename = r.Name.ToLower();
      Debug.Assert(!files.ContainsKey(filename));
      files[filename] = r;
      SaveFileList();
      FileCount++;
    }

    public void ClearAdds()
    {
      adds.Clear();
      UpdateStatus();
    }

    public void UpdateStatus()
    {
      if (adds.Count > 0 || moves.Count > 0 || deletes.Count > 0)
        DriveStatus = DriveStatus.UpdateRequired;
      else
        DriveStatus = DriveStatus.UpToDate;
    }

    /// <summary>
    /// Run a hash check on every file on this drive.  Return the number of failures.
    /// </summary>
    public int HashCheck()
    {
      int failures = 0;
      foreach (FileRecord r in files.Values)
        try {
          LogFile.Log("Checking hash for {0}...", r.FullPath);
          if (!HashCheck(r)) {
            LogFile.Log("Hash check FAILED");
            failures++;
          }
        }
        catch (Exception e) {
          LogFile.Log("Error opening {0} for hash check: {1}", r.FullPath, e.Message);
        }
      return failures;
    }

    /// <summary>
    /// Verify that the hash on record for this file matches the actual file currently on disk
    /// </summary>
    public bool HashCheck(FileRecord r)
    {
      if (r.Length == 0)
        return true; // zero length files cannot fail a hash check
      else
       return Utils.HashCodesMatch(ComputeHash(r), r.HashCode);
    }

    /// <summary>
    /// Compute the hash code for the file on disk
    /// </summary>
    private byte[] ComputeHash(FileRecord r)
    {
      // ComputeHash(stream) is nice, but it's not cancellable, so we can't use it
      using (FileStream s = new FileStream(r.FullPath, FileMode.Open, FileAccess.Read))
      using (MD5 hash = MD5.Create()) {
        hash.Initialize();
        byte[] buf = new byte[Parity.BLOCK_SIZE];
        int read = 0;
        while (!cancelScan && ((read = s.Read(buf, 0, Parity.BLOCK_SIZE)) > 0))
          hash.TransformBlock(buf, 0, read, buf, 0);
        hash.TransformFinalBlock(buf, 0, 0);
        return hash.Hash;
      }
    }

    /// <summary>
    /// Finds the file containing this block
    /// </summary>
    public FileRecord FileFromBlock(UInt32 block)
    {
      foreach (FileRecord r in files.Values)
        if (r.ContainsBlock(block))
          return r;
      return null;
    }

    private UInt32 enumBlock;
    private UInt32 enumBlocks;
    private int enumCount;
    private FileStream enumFile;
    private long enumSize;
    private List<FileRecord>.Enumerator enumerator;
    private bool enumComplete;

    public void BeginFileEnum()
    {
      enumBlock = 0;
      enumFile = null;
      enumComplete = false;
      enumBlocks = 0;
      foreach (FileRecord r in adds)
        enumBlocks += r.LengthInBlocks;
      enumerator = adds.GetEnumerator();
      enumCount = 0;
      enumSize = 0;
    }

    public bool GetNextBlock(byte[] buf)
    {
      if (buf.Length != Parity.BLOCK_SIZE)
        throw new Exception("Invalid buffer size (must be " + Parity.BLOCK_SIZE + " bytes)");
      if (enumFile == null) {
        if (enumComplete)
          return false;
        if (!enumerator.MoveNext()) {
          EndFileEnum();
          enumComplete = true;
          LoadFileList(); // this loads completed filesX.dat back into the master files list
          DriveStatus = DriveStatus.UpToDate;
          return false;
        }
        // TODO: Handle zero-length file here?
        string fullName = Utils.MakeFullPath(root, enumerator.Current.Name);
        try {
          enumFile = new FileStream(fullName, FileMode.Open, FileAccess.Read, FileShare.Read);
        }
        catch (Exception e) {
          LogFile.Log("Error opening {0} for reading: {1}", fullName, e.Message);
          enumerator.Current.Skipped = true;
          enumerator.MoveNext();
          return GetNextBlock(buf);
        }
        ReportProgress(0);
        DriveStatus = DriveStatus.ReadingFile;
        Status = "Reading " + fullName;
        LogFile.Log(Status);
        hash.Initialize();
        enumerator.Current.StartBlock = enumBlock;
      }
      int bytesRead = enumFile.Read(buf, 0, Parity.BLOCK_SIZE);
      if (enumFile.Position < enumFile.Length)
        hash.TransformBlock(buf, 0, bytesRead, buf, 0);
      else {
        // reached end of this file
        hash.TransformFinalBlock(buf, 0, bytesRead);
        enumerator.Current.HashCode = hash.Hash;
        AppendFileRecord(enumerator.Current);
        enumFile.Close();
        enumFile.Dispose();
        enumFile = null;
        FileCount = enumCount;
        TotalFileSize = enumSize;
        enumCount++;
        enumSize += enumerator.Current.Length;
        Array.Clear(buf, bytesRead, Parity.BLOCK_SIZE - bytesRead);
      }
      enumBlock++;
      ReportProgress((double)enumBlock / (double)enumBlocks);
      return true;
    }    

    public void EndFileEnum()
    {
      enumerator.Dispose();
      if (enumFile != null) {
        enumFile.Dispose();
        enumFile = null;
      }
      ReportProgress(0);
      UpdateStatus();
    }

    private FileRecord currentOpenFile;
    private FileStream currentOpenFileStream;

    /// <summary>
    /// Reads a block of data from the drive.  Returns the File containing the block, if any, in r.
    /// Returns true if data was read.
    /// Returns false if no file contains this block.  r will be null.
    /// If the file doens't exist, returns false, and sets r to the file.
    /// If the file exists but has been modified, reads the block and returns true.  It is the
    ///   caller's reponsibility to check r.Modified and handle appropriately.
    /// Throws an exception if opening or reading the file fails.
    /// </summary>
    public bool ReadBlock(UInt32 block, byte[] data, out FileRecord r)
    {
      r = FindFileContaining(block);
      if (r == null)
        return false;
      lock (fileCloseLock) {
        if (r != currentOpenFile) {
          if (currentOpenFileStream != null) {
            currentOpenFileStream.Dispose();
            currentOpenFileStream = null;
          }
          if (!File.Exists(r.FullPath))
            return false;
          try {
            currentOpenFileStream = new FileStream(r.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
          }
          catch (Exception e) {
            throw new Exception(String.Format("Error opening {0}: {1}", r.FullPath, e.Message), e);
          }
          currentOpenFile = r;
        }
        fileCloseTimer.Stop();
        fileCloseTimer.Start();
        Status = "Reading " + currentOpenFile.FullPath;
        DriveStatus = DriveStatus.ReadingFile;
        try {
          currentOpenFileStream.Position = (block - r.StartBlock) * Parity.BLOCK_SIZE;
          int bytesRead = currentOpenFileStream.Read(data, 0, data.Length);
          while (bytesRead < data.Length)
            data[bytesRead++] = 0;
        }
        catch (Exception e) {
          throw new Exception(String.Format("Error reading {0}: {1}", r.FullPath, e.Message), e);
        }
      }
      return true;
    }

    private void HandleFileCloseTimer(object sender, ElapsedEventArgs args)
    {
      lock (fileCloseLock) {
        if (currentOpenFileStream != null) {
          currentOpenFileStream.Dispose();
          currentOpenFileStream = null;
          currentOpenFile = null;
          UpdateStatus();
        }
      }
    }

    /// <summary>
    /// Returns a mask of used/unused blocks for this drive
    /// </summary>
    public BitArray BlockMask
    {
      get
      {
        BitArray blockMask = new BitArray((int)MaxBlock);
        foreach (FileRecord r in files.Values) {
          UInt32 endBlock = r.StartBlock + r.LengthInBlocks;
          for (int i = (int)r.StartBlock; i < endBlock; i++)
            blockMask.Set(i, true);
        }
        return blockMask;
      }
    }

    private FileRecord FindFileContaining(UInt32 block)
    {
      if (block < MaxBlock)
        foreach (FileRecord r in files.Values)
          if (r.ContainsBlock(block))
            return r;
      return null;
    }

    /// <summary>
    /// Loads the files.dat file containing the records of any existing protected file data
    /// </summary>
    private void LoadFileList()
    {
      using (FileStream metaData = new FileStream(MetaFilePath, FileMode.Open, FileAccess.Read)) {
        UInt32 version = FileRecord.ReadUInt32(metaData);
        if (version == 1)
          // skip past unused count field
          FileRecord.ReadUInt32(metaData);
        else if (version != META_FILE_VERSION)
          throw new Exception("file version mismatch: " + MetaFilePath);
        files.Clear();
        while (metaData.Position < metaData.Length) {
          FileRecord r = FileRecord.LoadFromFile(metaData, this);
          files[r.Name.ToLower()] = r;
        }
      }
      CalculateMaxBlock();
      FileCount = files.Count;
    }

    private void SaveFileList()
    {
      DateTime start = DateTime.Now;
      LogFile.VerboseLog("Saving file data for {0}...", root);
      string backup = "";
      if (File.Exists(MetaFilePath)) {
        backup = Path.ChangeExtension(MetaFilePath, ".BAK");
        File.Move(MetaFilePath, backup);
      }
      using (FileStream f = new FileStream(MetaFilePath, FileMode.Create, FileAccess.Write)) {
        FileRecord.WriteUInt32(f, META_FILE_VERSION);
        FileRecord.WriteUInt32(f, (UInt32)files.Count);
        foreach (FileRecord r in files.Values)
          r.WriteToFile(f);
        f.Close();
      }
      if (backup != "")
        File.Delete(backup);
      TimeSpan elapsed = DateTime.Now - start;
      CalculateMaxBlock();
      LogFile.VerboseLog("{0} records saved in {1:F2} sec", files.Count, elapsed.TotalSeconds);
    }

    private void AppendFileRecord(FileRecord r)
    {
      if (!File.Exists(MetaFilePath))
        using (FileStream fNew = new FileStream(MetaFilePath, FileMode.Create, FileAccess.Write)) {
          FileRecord.WriteUInt32(fNew, META_FILE_VERSION);
          FileRecord.WriteUInt32(fNew, 0); // unknown count
        }
      using (FileStream f = new FileStream(MetaFilePath, FileMode.Append, FileAccess.Write))
        r.WriteToFile(f); 
    }
    
    /// <summary>
    /// Determines the index of the first unused 64K parity block for this drive.
    /// Also re-calculates TotalSize while we're at it.
    /// </summary>
    private void CalculateMaxBlock()
    {
      MaxBlock = 0;
      TotalFileSize = 0;
      foreach (FileRecord r in files.Values) {
        UInt32 endBlock = r.StartBlock + r.LengthInBlocks;
        if (endBlock > MaxBlock)
          MaxBlock = endBlock;
        TotalFileSize += r.Length;
      }
    }

    /// <summary>
    /// Generates a "free list" of unused blocks in the existing parity data 
    /// for this drive which we can then re-use for adds, so that we don't grow 
    /// the parity data unnecessarily.
    /// </summary>
    public List<FreeNode> GetFreeList()
    {
      BitArray blockMask = BlockMask;

      List<FreeNode> freeList = new List<FreeNode>();
      UInt32 block = 0;
      while (block < MaxBlock)
        if (!blockMask.Get((int)block)) {
          FreeNode n = new FreeNode();
          n.Start = block++;
          n.Length = 1;
          while (block < MaxBlock && (!blockMask.Get((int)block))) {
            n.Length++;
            block++;
          }
          freeList.Add(n);
        }
        else
          block++;

      return freeList;
    }

    public override string ToString()
    {
      return root;
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

    private DriveStatus driveStatus;
    public DriveStatus DriveStatus
    {
      get
      {
        return driveStatus;
      }
      set
      {
        SetProperty(ref driveStatus, "DriveStatus", value);
      }
    }

    private int fileCount;
    public int FileCount
    {
      get
      {
        return fileCount;
      }
      set
      {
        SetProperty(ref fileCount, "FileCount", value);
      }
    }

    /// <summary>
    /// Total size, in bytes, of all protected files on this drive
    /// Not currently a "notify changed" property because it always changes in tandem with FileCount
    /// </summary>
    public long TotalFileSize { get; private set; }

    #endregion

  }

  public class FreeNode
  {
    public UInt32 Start { get; set; }
    public UInt32 Length { get; set; }  // in blocks
    public const UInt32 INVALID_BLOCK = 0xFFFFFFFF;

    public static UInt32 FindBest(List<FreeNode> list, UInt32 blocks)
    {
      FreeNode best = null;
      foreach (FreeNode n in list)
        if (n.Length == blocks) {
          best = n;
          break;
        }
        else if (n.Length > blocks)
          if ((best == null) || (n.Length < best.Length))
            best = n;
      if (best == null)
        return INVALID_BLOCK;
      UInt32 result = best.Start;
      if (best.Length == blocks)
        list.Remove(best);
      else {
        best.Start += blocks;
        best.Length -= blocks;
      }
      return result;
    }

  }

}
