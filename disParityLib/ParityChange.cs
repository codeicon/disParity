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

    private Parity parity;
    private ParityBlock parityBlock;
    private UInt32 block;
    private UInt32 startBlock;
    private Stream tempFileStream;
    private string tempDir;
    private string tempFileName;
    private MemoryMappedFile mmf;
    private MemoryMappedViewStream mmfStream;

    public ParityChange(Parity parity, Config config, UInt32 startBlock, UInt32 lengthInBlocks)
    {
      this.parity = parity;
      this.startBlock = startBlock;
      tempDir = config.TempDir;
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
      if (mmfStream != null && (mmfStream.Position < mmfStream.Capacity))
        mmfStream.Write(parityBlock.Data, 0, Parity.BLOCK_SIZE);      
      else {
        if (tempFileStream == null) {
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
          mmfStream.Seek(0, SeekOrigin.Begin);
          while (saveBlock < endBlock && mmfStream.Position < mmfStream.Capacity) {
            mmfStream.Read(data, 0, Parity.BLOCK_SIZE);
            parity.WriteBlock(saveBlock, data);
            saveBlock++;
          }
        }
        if (tempFileStream != null) {
          LogFile.Log("Flushing MMF parity took {0} seconds", (DateTime.Now - start).TotalSeconds);
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
      LogFile.Log("Flushing temp parity took {0} seconds", (DateTime.Now - start).TotalSeconds);
    }

    public void Dispose()
    {
      if (mmfStream != null)
        mmfStream.Dispose();
      if (tempFileStream != null)
        tempFileStream.Dispose();
      if (mmf != null)
        mmf.Dispose();
      if (!String.IsNullOrEmpty(tempFileName) && File.Exists(tempFileName))
        File.Delete(tempFileName);
    }

  }

}
