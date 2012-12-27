using System;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Text;

namespace disParity
{

  internal class ParityChange : IDisposable
  {

    const string TEMP_PARITY_FILENAME = "parity.tmp";
    const int MAX_MEMORY_LOAD = 80;

    private Parity parity;
    private ParityBlock parityBlock;
    private UInt32 block;
    private UInt32 startBlock;
    private Stream tempFileStream;
    private string tempDir;
    private string tempFileName;
    private MemoryMappedFile mmf;
    private MemoryMappedViewStream mmfStream;
    private UInt32 lastMMFBlock;
    private bool writingToMMF;

    public ParityChange(Parity parity, Config config, UInt32 startBlock, UInt32 lengthInBlocks)
    {
      this.parity = parity;
      this.startBlock = startBlock;
      tempDir = config.TempDir;
      writingToMMF = true;
      UInt32 maxMMFBlocks = Parity.LengthInBlocks((long)config.MaxTempRAM * 1024 * 1024);
      UInt32 mmfBlocks = (lengthInBlocks < maxMMFBlocks) ? lengthInBlocks : maxMMFBlocks;
      try {
        mmf = MemoryMappedFile.CreateNew("disparity.tmp", (long)mmfBlocks * Parity.BLOCK_SIZE);
        mmfStream = mmf.CreateViewStream();
      }
      catch {
        // We'll use a temp file only
        mmf = null;
        mmfStream = null;
        writingToMMF = false;
      }
      tempFileStream = null;
      parityBlock = new ParityBlock(parity);
      block = startBlock;
    }

    /// <summary>
    /// Clear current parity block in preparation to receive data
    /// </summary>
    public void Reset(bool fromParity)
    {
      if (fromParity)
        // todo: handle parity block read failure (fatal error!)
        parityBlock.Load(block);
      else
        parityBlock.Clear();
    }

    /// <summary>
    /// Add (or remove) more data to the current block by XOR'ing it in
    /// </summary>
    public void AddData(byte[] data)
    {
      parityBlock.Add(data);
    }

    /// <summary>
    /// Commit the current parity block by writing it to the temporary buffer.
    /// </summary>
    public void Write()
    {
      if (writingToMMF && (mmfStream.Position < mmfStream.Capacity) && Utils.MemoryLoad() < MAX_MEMORY_LOAD) {
        mmfStream.Write(parityBlock.Data, 0, Parity.BLOCK_SIZE);
        lastMMFBlock = block + 1;
      } 
      else {
        if (tempFileStream == null) {
          LogFile.Log("Switching from RAM to temp file at {0}", Utils.SmartSize(mmfStream.Position));
          mmfStream.Seek(0, SeekOrigin.Begin); // return MMF stream to its start
          writingToMMF = false;
          // make sure temp directory exists
          if (!Directory.Exists(tempDir))
            Directory.CreateDirectory(tempDir);
          tempFileName = Path.Combine(tempDir, TEMP_PARITY_FILENAME);
          tempFileStream = new FileStream(tempFileName, FileMode.Create, FileAccess.ReadWrite);
        }
        tempFileStream.Write(parityBlock.Data, 0, Parity.BLOCK_SIZE);
      }
      block++;
    }

    public double SaveProgress
    {
      get
      {
        if (saveBlock == 0)
          return 0;
        else
          return ((double)(saveBlock - startBlock)) / (block - startBlock);
      }
    }

    UInt32 saveBlock;

    /// <summary>
    /// Complete the change by copying the temporary buffer to the actual parity
    /// </summary>
    public void Save()
    {
      DateTime start = DateTime.Now;
      byte[] data = new byte[Parity.BLOCK_SIZE];
      try {
        saveBlock = startBlock;
        UInt32 endBlock = block;
        if (mmfStream != null) {
          mmfStream.Seek(0, SeekOrigin.Begin); // return MMF stream to its start (it may still be at the end if on-disk temp was never used)
          while (saveBlock < endBlock && saveBlock < lastMMFBlock) {
            mmfStream.Read(data, 0, Parity.BLOCK_SIZE);
            parity.WriteBlock(saveBlock, data);
            saveBlock++;
          }
          FreeMMF();
        }
        if (tempFileStream != null) {
#if DEBUG
          LogFile.Log("Flushing MMF parity took {0} seconds", (DateTime.Now - start).TotalSeconds);
#endif
          tempFileStream.Seek(0, SeekOrigin.Begin);
          while (saveBlock < endBlock) {
            tempFileStream.Read(data, 0, Parity.BLOCK_SIZE);
            parity.WriteBlock(saveBlock, data);
            saveBlock++;
          }
        }
      }
      catch (Exception e) {
        LogFile.Log("Fatal error in ParityChange.Save(): " + e.Message);
        throw new Exception("A fatal error occurred (" + e.Message + ") when writing to parity.  Parity data may be damaged.", e);
      }
    }

    private void FreeMMF()
    {
      if (mmfStream != null) {
        mmfStream.Dispose();
        mmfStream = null;
      }
      if (mmf != null) {
        mmf.Dispose();
        mmf = null;
      }
    }

    public void Dispose()
    {
      FreeMMF();
      if (tempFileStream != null)
        tempFileStream.Dispose();
      if (!String.IsNullOrEmpty(tempFileName) && File.Exists(tempFileName))
        File.Delete(tempFileName);
    }

  }

}
