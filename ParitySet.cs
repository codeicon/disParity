using System;
using System.Collections.Generic;
using System.Text;
using System.IO;

namespace disParity
{

  public class ParitySet
  {

    private Config config;
    private string configFilePath;
    private DataDrive[] drives;

    public ParitySet(string configFilePath)
    {
      if (!File.Exists(configFilePath))
        throw new Exception(configFilePath + " not found");

      this.configFilePath = configFilePath;
      try {
        config = new Config(configFilePath);
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
      drives = new DataDrive[config.BackupDirs.Length];
      for (int i = 0; i < config.BackupDirs.Length; i++) {
        string metaFile = Path.Combine(config.ParityDir, String.Format("files{0}.dat", i + 1));
        if (File.Exists(metaFile))
          Empty = false;
        drives[i] = new DataDrive(config.BackupDirs[i], metaFile);
      }

      Parity.Initialize(config.ParityDir, config.TempDir, false);
    }

    public bool Empty { get; private set; }

    /// <summary>
    /// Erase a previously created parity set
    /// </summary>
    public void Erase()
    {
      Parity.DeleteAll();
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
      foreach (DataDrive d in drives) {
        d.Scan();
        d.Compare();
      }

    }

    /// <summary>
    /// Creates a new snapshot from scratch
    /// </summary>
    private void Create()
    {
      DateTime start = DateTime.Now;

      // generate the list of all files to be protected from all drives
      foreach (DataDrive d in drives)
        d.Scan();

      // TO DO: check free space on parity drive here

      foreach (DataDrive d in drives)
        d.BeginFileEnum();

      byte[] parityBuf = new byte[Parity.BlockSize];
      byte[] dataBuf = new byte[Parity.BlockSize];
      UInt32 block = 0;

      bool done = false;
      while (!done) {
        done = true;
        foreach (DataDrive d in drives)
          if (d.GetNextBlock(done ? parityBuf : dataBuf))
            if (done)
              done = false;
            else
              Parity.FastXOR(parityBuf, dataBuf);
        if (!done)
          Parity.WriteBlock(block, parityBuf);
        block++;
      }
      Parity.Close();

    }

    private void ValidateConfig()
    {
      if (config.BackupDirs.Length == 0)
        throw new Exception("No drives found in " + configFilePath);

      // Make sure all data paths are set and valid
      for (int i = 0; i < config.BackupDirs.Length; i++) {
        if (config.BackupDirs[i] == null)
          throw new Exception(String.Format("Path {0} is not set (check {1})", i + 1, configFilePath));
        if (!Path.IsPathRooted(config.BackupDirs[i]))
          throw new Exception(String.Format("Path {0} is not valid (must be absolute)", config.BackupDirs[i]));
      }

      if (!Path.IsPathRooted(config.ParityDir))
        throw new Exception(String.Format("{0} is not a valid parity path (must be absolute)", config.ParityDir));

    }

  }

}
