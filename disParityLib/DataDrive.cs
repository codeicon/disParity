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
    private int ignoreCount; // number of files that matched an ignore filter this scan
    private HashSet<string> errorFiles = new HashSet<string>(); // list of files that had errors during the scan.  We won't add them on an update, but we won't remove them either.
    private MD5 hash;
    private Config config;
    private ProgressEstimator scanProgress;
    private bool cancelScan;
    private Timer fileCloseTimer;
    private object fileCloseLock = new object();
    private FileSystemWatcher watcher;
    private IEnvironment env;
    private DateTime lastScanStart;
    private int scanFileCount; // number of files encountered this scan

    const UInt32 META_FILE_VERSION = 1;
    const int MAX_FOLDER = 248;
    const int MAX_PATH = 260;

    public event EventHandler<ScanCompletedEventArgs> ScanCompleted;
    public event EventHandler<ErrorMessageEventArgs> ErrorMessage;

    public DataDrive(string root, string metaFile, Config config, IEnvironment environment)
    {
      this.root = root;
      this.metaFile = metaFile;
      this.config = config;
      this.env = environment;

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

      if (config.MonitorDrives)
        EnableWatcher();

      lastScanStart = DateTime.MinValue;
      LastChange = DateTime.Now;
    }

    public void EnableWatcher()
    {
      if (watcher == null)
      {
        try
        {
          watcher = new FileSystemWatcher();
          watcher.Path = Root;
          watcher.Filter = "*.*";
          watcher.IncludeSubdirectories = true;
          watcher.Created += HandleWatcherEvent;
          watcher.Changed += HandleWatcherEvent;
          watcher.Deleted += HandleWatcherEvent;
          watcher.Renamed += HandleWatcherEvent;
          watcher.EnableRaisingEvents = true;
        }
        catch (Exception e)
        {
          env.LogCrash(e);
          LogFile.Error("Could not enable file watcher for {0}: {1}", Root, e.Message);
        }
      }
    }

    public void DisableWatcher()
    {
      if (watcher != null) {
        watcher.Dispose();
        watcher = null;
      }
    }

    private void HandleWatcherEvent(object sender, FileSystemEventArgs args)
    {
      LogFile.Log(String.Format("Changes detected on {0} ({1})", root, args.ChangeType));
      LastChange = DateTime.Now;
      ChangesDetected = true;
      if (DriveStatus == DriveStatus.UpToDate)
        DriveStatus = DriveStatus.ScanRequired;
    }

    private string MetaFilePath
    {
      get { return Path.Combine(config.ParityDir, metaFile); }
    }

    public string MetaFile { get { return metaFile; } }

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
    /// Returns the total size of all protected files on this drive, in blocks.
    /// </summary>
    public UInt32 TotalFileBlocks
    {
      get
      {
        UInt32 result = 0;
        foreach (FileRecord r in files.Values)
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
      if (File.Exists(Path.ChangeExtension(MetaFilePath, ".bak")))
        File.Delete(Path.ChangeExtension(MetaFilePath, ".bak"));
      files.Clear();
      FileCount = 0;
      TotalFileSize = 0;
      Status = "Scan required";
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
      Status = "Scan required";
      DriveStatus = DriveStatus.ScanRequired;
    }

    /// <summary>
    /// Scans the drive from 'root' down, generating the current list of files on the drive.
    /// Files that should not be protected (e.g. Hidden files) are not included.
    /// </summary>
    public void Scan(bool auto = false)
    {
      Debug.Assert(!Scanning);
      if (Scanning)
        return; // shouldn't happen
      Scanning = true;
      DriveStatus = DriveStatus.Scanning;
      cancelScan = false;
      Progress = 0;
      lastScanStart = DateTime.Now;
      ignoreCount = 0;
      bool error = false;
      try {

        // Convert list of ignores to a list of Regex
        List<Regex> ignores = new List<Regex>();
        foreach (string i in config.Ignores) {
          string pattern = Regex.Escape(i.ToLower());       // Escape the original string
          pattern = pattern.Replace(@"\?", ".");  // Replace all \? with .
          pattern = pattern.Replace(@"\*", ".*"); // Replace all \* with .*
          ignores.Add(new Regex(pattern));
        }

        // clear seen flag on all files
        foreach (var kvp in files)
          kvp.Value.Seen = false;

        adds.Clear();
        edits.Clear();
        scanFileCount = 0;
        LogFile.Log("Scanning {0}...", root);
        scanProgress = null;
        try {
          DirectoryInfo rootInfo = new DirectoryInfo(root);
          // try enumerating the sub directories of root, for the sole purpose of triggering an exception 
          // (caught below) if there is some reason why we can't access the drive
          rootInfo.GetDirectories();
          Scan(rootInfo, ignores); 
          if (!cancelScan) {
            Status = "Scan complete. Analyzing results...";
            Progress = 1;
            AnalyzingResults = true;
            Compare();
            if (cancelScan) {
              Status = "Scan required";
              DriveStatus = DriveStatus.ScanRequired;
              return;
            }
            LogFile.Log("Scan of {0} complete. Found {1} file{2} Adds: {3} Deletes: {4} Moves: {5} Edits: {6} Ignored: {7}", Root, scanFileCount,
              scanFileCount == 1 ? "" : "s", adds.Count, deletes.Count, moves.Count, edits.Count, ignoreCount);
            // process moves now as part of the scan, since they don't require changes to parity
            ProcessMoves();
            // if no file system events occurred since the start of the scan, we are now up to date
            if (lastChange < lastScanStart)
              ChangesDetected = false;
          }
          else
            LogFile.Log("{0}: Scan cancelled", Root);
        }
        catch (Exception e) {
          adds.Clear();
          FireErrorMessage(String.Format("Could not scan {0}: {1}", root, e.Message));
          LogFile.Log(e.StackTrace);
          LastError = e.Message;
          DriveStatus = DriveStatus.AccessError;
          error = true;
          return;
        }
      }
      finally {
        AnalyzingResults = false;
        Progress = 0;
        UpdateStatus();
        errorFiles.Clear();
        FireScanCompleted(cancelScan, error, adds.Count > 0 || deletes.Count > 0, auto);
        Scanning = false;
      }
    }

    private void Scan(DirectoryInfo dir, List<Regex> ignores, ProgressEstimator progress = null)
    {
      if (cancelScan)
        return;
      // never allow scanning of our own parity folder
      if (Utils.PathsAreEqual(dir.FullName, config.ParityDir)) {
        LogFile.Log("Warning: skipping " + dir.FullName + " because it is the parity folder.");
        return;
      }
      Status = "Scanning " + dir.FullName;
      if (scanProgress != null)
        Progress = scanProgress.Progress;
      DirectoryInfo[] subDirs;
      try {
        subDirs = dir.GetDirectories();
      }
      catch (Exception e) {
        if (progress == null)
          throw;
        LogFile.Error("Warning: Could not enumerate subdirectories of {0}: {1}", dir.FullName, e.Message);
        return;
      }
      FileInfo[] fileInfos;
      try {
        fileInfos = dir.GetFiles();
      }
      catch (Exception e) {
        LogFile.Error("Warning: Could not enumerate files in {0}: {1}", dir.FullName, e.Message);
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
        string subDir = Path.Combine(dir.FullName, d.Name);
        if (subDir.Length >= MAX_FOLDER) 
          LogFile.Error("Warning: skipping folder \"" + subDir + "\" because the path is too long.");
        else
          Scan(d, ignores, folderProgress);
        folderProgress.EndPhase();
      }
      Progress = scanProgress.Progress;
      string relativePath = Utils.StripRoot(root, dir.FullName);
      foreach (FileInfo f in fileInfos) {
        if (cancelScan)
          return;
        // have to use Path.Combine here because accessing the f.FullName property throws
        // an exception if the path is too long
        string fullName = Path.Combine(dir.FullName, f.Name);
        try {
          if (fullName.Length >= MAX_PATH) {
            LogFile.Error("Warning: skipping file \"" + fullName + "\" because the path is too long");
            continue;
          }
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
            if (LogFile.Verbose)
              LogFile.Log("Skipping \"{0}\" because it matches an ignore", f.FullName); 
            ignoreCount++;
            continue;
          }
          ProcessScannedFile(relativePath, f);
        }
        catch (Exception e) {
          errorFiles.Add(fullName.ToLower());
          FireErrorMessage(String.Format("Error scanning \"{0}\": {1}", fullName, e.Message));
        }
      }
    }

    private void ProcessScannedFile(string relativePath, FileInfo f)
    {
      // try to find this file in the master list of protected files
      FileRecord r;
      string relativePathLower = Utils.MakeFullPath(relativePath, f.Name).ToLower();
      if (files.TryGetValue(relativePathLower, out r))
      {
        r.Seen = true;
        // check for possible edit
        if (r.Length != f.Length || r.LastWriteTime != f.LastWriteTime)
          edits.Add(r);
      }
      else
        // not in the list, must be an add
        adds.Add(new FileRecord(f, relativePath, this));
      scanFileCount++;
    }

    /// <summary>
    /// Called from ParitySet when an update has completed.  We can clean up all scan-related stuff here.
    /// </summary>
    public void UpdateFinished()
    {
      // Clear() adds, deletes, etc. here???
      Progress = 0;
    }

    // Caution: Keep this thread safe!
    public void CancelScan()
    {
      cancelScan = true;
    }

    public void FireScanCompleted(bool cancelled, bool error, bool updateNeeded, bool auto)
    {
      if (ScanCompleted != null)
        ScanCompleted(this, new ScanCompletedEventArgs(cancelled, error, updateNeeded, auto));
    }

    private void FireErrorMessage(string message)
    {
      LogFile.Error(message);
      if (ErrorMessage != null)
        ErrorMessage(this, new ErrorMessageEventArgs(message));
    }

    // the "deletes" list contains FileRecords from the master files list
    private List<FileRecord> deletes = new List<FileRecord>();
    public List<FileRecord> Deletes { get { return deletes; } }

    // the "adds" list contains new FileRecords generated during the current scan
    private List<FileRecord> adds = new List<FileRecord>();
    public List<FileRecord> Adds { get { return adds; } }

    // the "edits" list contains FileRecords from the master files list
    private List<FileRecord> edits = new List<FileRecord>();

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

      // build list of old files we don't see now (deletes)
      foreach (var kvp in files)
        if (!kvp.Value.Seen && !errorFiles.Contains(kvp.Value.FullPath.ToLower()))
          deletes.Add(kvp.Value);
      
      // some of the files in add/delete list might actually be moves, check for that
      foreach (FileRecord a in adds) {
        byte[] hashCode = null;
        if (a.Length > 0)
          foreach (FileRecord d in deletes)
            if (a.Length == d.Length && a.LastWriteTime == d.LastWriteTime) {
              // probably the same file, but we need to check the hash to be sure
              if (hashCode == null) {
                Status = "Checking " + a.FullPath;
                hashCode = ComputeHash(a, true);
              }
              if (cancelScan)
                return;
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
      bool saveFileList = false;
      foreach (FileRecord r in edits) 
      {
        if (cancelScan)
          return;
        FileInfo info = new FileInfo(r.FullPath);
        // For edited files we add the "new" version of the file to the adds list,  because it has the new
        // attributes and we want those saved later.  The old value goes into the deletes lists.
        if (r.Length != info.Length)
        {
          deletes.Add(r);
          adds.Add(new FileRecord(info, this));
        }
        else 
        {
          // length hasn't changed but timestamp says file was modified, check hash code to be sure it has changed
          Status = "Checking " + r.FullPath;
          if (!HashCheck(r))
          {
            deletes.Add(r);
            adds.Add(new FileRecord(info, this));
          }
          else
          {
            // file hasn't actually changed, but we still need to update the LastWriteTime so we don't
            // keep re-checking it on every scan
            r.LastWriteTime = info.LastWriteTime;
            saveFileList = true;
          }
        }
      }

      if (saveFileList)
        SaveFileList();

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
      BackupMetaFile();
      SaveFileList();
      // clear moves list, don't need it anymore
      moves.Clear();
    }

    /// <summary>
    /// Removes the file from the master files list
    /// </summary>
    public void RemoveFile(FileRecord r)
    {
      string filename = r.Name.ToLower();
      Debug.Assert(files.ContainsKey(filename));
      files.Remove(filename);
      FileCount--;
    }

    /// <summary>
    /// Adds the file to the master files list
    /// </summary>
    public void AddFile(FileRecord r)
    {
      string filename = r.Name.ToLower();
      Debug.Assert(!files.ContainsKey(filename));
      files[filename] = r;
      FileCount++;
    }

    public void UpdateStatus()
    {
      if (ChangesDetected)
        DriveStatus = DriveStatus.ScanRequired;
      else if (adds.Count > 0 || deletes.Count > 0)
        DriveStatus = DriveStatus.UpdateRequired;
      else if (DriveStatus != DriveStatus.ScanRequired && DriveStatus != DriveStatus.AccessError)
        DriveStatus = DriveStatus.UpToDate;
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
    private byte[] ComputeHash(FileRecord r, bool showProgress = false)
    {
      using (FileStream s = new FileStream(r.FullPath, FileMode.Open, FileAccess.Read))
      using (MD5 hash = MD5.Create()) {
        hash.Initialize();
        byte[] buf = new byte[Parity.BLOCK_SIZE];
        int read;
        int block = 0;
        if (showProgress)
          Progress = 0;
        while (!cancelScan && ((read = s.Read(buf, 0, Parity.BLOCK_SIZE)) > 0)) {
          hash.TransformBlock(buf, 0, read, buf, 0);
          if (showProgress)
            Progress = (double)++block / r.LengthInBlocks;
        }
        if (cancelScan)
          return null;
        hash.TransformFinalBlock(buf, 0, 0);
        /* Uncomment to see hash values
        StringBuilder sb = new StringBuilder();
        foreach (byte b in hash.Hash)
          sb.Append(String.Format("{0:X2}", b));
        Status = sb.ToString();
         */
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
          adds.Clear();
          enumComplete = true;
          if (enumCount > 0)
            LoadFileList(); // this loads completed filesX.dat back into the master files list
          UpdateStatus(); // set status to UpToDate, or possibly ScanRequired if more changes have occurred
          return false;
        }
        // Check for zero-length files
        if (enumerator.Current.Length == 0) {
          AppendFileRecord(enumerator.Current);
          enumCount++;
          return GetNextBlock(buf);
        }
        string fullName = Utils.MakeFullPath(root, enumerator.Current.Name);
        try {
          enumFile = new FileStream(fullName, FileMode.Open, FileAccess.Read, FileShare.Read);
        }
        catch (Exception e) {
          FireErrorMessage(String.Format("Error opening {0} for reading ({1}.)  File will be skipped this update.", fullName, e.Message));
          enumFile = null; // just to be sure
          return GetNextBlock(buf);
        }
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
        enumCount++;
        enumSize += enumerator.Current.Length;
        TotalFileSize = enumSize;
        FileCount = enumCount;
        Array.Clear(buf, bytesRead, Parity.BLOCK_SIZE - bytesRead);
      }

      enumBlock++;
      Progress = (double)enumBlock / (double)enumBlocks;
      return true;

    }    

    public void EndFileEnum()
    {
      enumerator.Dispose();
      if (enumFile != null) {
        enumFile.Dispose();
        enumFile = null;
      }
      Progress = 0;
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
          currentOpenFileStream.Position = ((long)(block - r.StartBlock)) * Parity.BLOCK_SIZE;
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
        CalculateMaxBlock();
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
      try {
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
            if (r == null)
              break;
            files[r.Name.ToLower()] = r;
          }
        }
      }
      catch (Exception e) {
        env.LogCrash(e);
        LogFile.Error(String.Format("Error reading {0}: {1}", MetaFilePath, e.Message));
        files.Clear();
        throw new MetaFileLoadException(MetaFilePath, e);
      }
      CalculateMaxBlock();
      FileCount = files.Count;
    }

    /// <summary>
    /// Returns the actual size of the current filesX.dat file on disk
    /// </summary>
    public long MetaFileSize
    {
      get
      {
        string metaFile = MetaFilePath;
        if (File.Exists(metaFile)) {
          FileInfo fi = new FileInfo(metaFile);
          return fi.Length;
        }
        else
          return 0;
      }
    }

    /// <summary>
    /// Returns the predicted meta file size if the current list of Adds is 
    /// written to the filesX.dat file.  Called only before a Create() to check free space.
    /// </summary>
    public long PredictedMetaFileSize
    {
      get
      {
        long size = 0;
        foreach (FileRecord r in adds)
          size += r.RecordSize;
        return size;
      }
    }

    private void WriteFileHeader(FileStream f, UInt32 count)
    {
      FileRecord.WriteUInt32(f, META_FILE_VERSION);
      FileRecord.WriteUInt32(f, count);
    }

    private void WriteFileList(string fileName)
    {
      try {
        Stopwatch sw = new Stopwatch();
        sw.Start();
        using (FileStream f = new FileStream(fileName, FileMode.OpenOrCreate, FileAccess.Write)) {
          WriteFileHeader(f, (UInt32)files.Count);
          foreach (FileRecord r in files.Values)
            r.WriteToFile(f);
          f.SetLength(f.Position);
          f.Close();
        }
        LogFile.Log(String.Format("{0} saved in {1}ms", fileName, sw.ElapsedMilliseconds));
      }
      catch (Exception e) {
        LogFile.Error(String.Format("Could not save {0}: {1}", fileName, e.Message));
        // try to delete it in case it got partly saved
        try {
          File.Delete(fileName);
        }
        catch {
        }
        throw new MetaFileSaveException(fileName, e);
      }
    }

    public bool BackupMetaFile()
    {
      string fileName = MetaFilePath;
      if (!File.Exists(fileName))
        return true; // this is a valid case, if a new drive has just been added to the array
      string backup = Path.ChangeExtension(MetaFilePath, ".bak");
      try {
        File.Copy(fileName, backup, true);
      }
      catch (Exception e) {
        LogFile.Error(String.Format("Could not backup {0}: {1}", fileName, e.Message));
        return false;
      }
      return true;
    }

    public bool RestoreMetaFile()
    {
      string fileName = MetaFilePath;
      string backup = Path.ChangeExtension(MetaFilePath, ".bak");
      try {
        File.Copy(backup, fileName, true);
      }
      catch (Exception e) {
        LogFile.Error(String.Format("Could not restore {0} from backup: {1}", fileName, e.Message));
        return false;
      }
      return true;
    }

    /// <summary>
    /// Backs up and then extendes the existing filesX.dat by the amount necessary to store the given new file record
    /// </summary>
    public bool ExtendMetaFile(FileRecord add)
    {
      if (!BackupMetaFile())
        return false;
      string fileName = MetaFilePath;
      if (!File.Exists(fileName)) 
        // this is a valid case if a new drive has just been added to the array
        using (FileStream f = new FileStream(fileName, FileMode.Create, FileAccess.Write))
          WriteFileHeader(f, 0);
      FileInfo fi = new FileInfo(fileName);
      try {
        using (FileStream f = new FileStream(fileName, FileMode.Open, FileAccess.Write))
          f.SetLength(fi.Length + add.RecordSize);
      }
      catch (Exception e) {
        LogFile.Error(String.Format("Could not extend {0}: {1}", fileName, e.Message));
        RestoreMetaFile();
        return false;
      }
      return true;
    }

    public void SaveFileList()
    {
      try {
        WriteFileList(MetaFilePath);
      }
      catch (Exception e) {
        // try to restore from the backup
        RestoreMetaFile();
        throw e;
      }
      CalculateMaxBlock();
    }

    private void AppendFileRecord(FileRecord r)
    {
      if (!File.Exists(MetaFilePath))
        using (FileStream fNew = new FileStream(MetaFilePath, FileMode.Create, FileAccess.Write)) {
          WriteFileHeader(fNew, 0); // unknown count
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
    /// If the adds list contains a file with the given name, remove it.
    /// Called during undelete in case the restored file was actually an edit.
    /// </summary>
    /// <param name="name"></param>
    public void MaybeRemoveAddByName(string name)
    {
      FileRecord found = null;
      foreach (FileRecord r in adds)
        if (String.Compare(name, r.FullPath, true) == 0) {
          found = r;
          break;
        }
      if (found != null)
        adds.Remove(found);
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

    public bool AnalyzingResults { get; private set; }

    /// <summary>
    /// If true, indicates that changes have been detected on the drive that may not be reflected 
    /// in the most recent scan.
    /// </summary>
    private bool changesDetected;
    public bool ChangesDetected
    {
      get
      {
        return changesDetected;
      }
      set
      {
        SetProperty(ref changesDetected, "ChangesDetected", value);
      }
    }

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

    private bool scanning;
    public bool Scanning
    {
      get
      {
        return scanning;
      }
      private set
      {
        SetProperty(ref scanning, "Scanning", value);
      }
    }

    private DateTime lastChange;
    public DateTime LastChange 
    {
      get
      {
        return lastChange;
      }
      private set
      {
        SetProperty(ref lastChange, "LastChange", value);
      }
    }

    /// <summary>
    /// Total size, in bytes, of all protected files on this drive
    /// Not currently a "notify changed" property because it always changes in tandem with FileCount
    /// </summary>
    public long TotalFileSize { get; private set; }



    #endregion

  }

  public class ScanCompletedEventArgs : EventArgs
  {
    public ScanCompletedEventArgs(bool cancelled, bool error, bool updateNeeded, bool auto)
    {
      Cancelled = cancelled;
      Error = error;
      UpdateNeeded = updateNeeded;
      Auto = auto;
    }

    public bool Cancelled { get; private set; }
    public bool Error { get; private set; }
    public bool UpdateNeeded { get; private set; }
    public bool Auto { get; private set; }

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

  public class MetaFileSaveException : Exception
  {
    public MetaFileSaveException(string file, Exception inner) : base("Error saving " + file + ": " + inner.Message, inner) { }
  }

  public class MetaFileLoadException : Exception
  {
    public MetaFileLoadException(string file, Exception inner) : base("Error reading " + file + ": " + inner.Message, inner) { }
  }

}

