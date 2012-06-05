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
    private Stream tempParity;
    private string tempFileName;
    private MemoryMappedFile mmf;

    public ParityChange(Parity parity, UInt32 startBlock, UInt32 lengthInBlocks)
    {
      this.parity = parity;
      this.startBlock = startBlock;
      mmf = null;
      tempParity = null;
      tempFileName = Path.Combine(parity.TempDir, TEMP_PARITY_FILENAME);
      if (lengthInBlocks < parity.MaxTempBlocks)
        try {
          mmf = MemoryMappedFile.CreateNew("disparity.tmp", (long)lengthInBlocks * Parity.BlockSize);
          tempParity = mmf.CreateViewStream();
        }
        catch {
          // Fall back to the temp file
          tempParity = null;
        }
      if (tempParity == null)
        tempParity = new FileStream(tempFileName, FileMode.Create, FileAccess.ReadWrite); 
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
    /// Add more data to the current block by XOR'ing it in
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
      tempParity.Write(parityBlock.Data, 0, Parity.BlockSize);
      block++;
    }

    /// <summary>
    /// Complete the change by copying the temporary buffer to the actual parity
    /// </summary>
    public void Save()
    {
      tempParity.Seek(0, SeekOrigin.Begin);
      byte[] data = new byte[Parity.BlockSize];
      UInt32 endBlock = block;
      for (UInt32 b = startBlock; b < endBlock; b++) {
        tempParity.Read(data, 0, Parity.BlockSize);
        parity.WriteBlock(b, data);
      }
    }

    public void Dispose()
    {
      tempParity.Close();
      tempParity.Dispose();
      if (mmf != null)
        mmf.Dispose();
      if (File.Exists(tempFileName))
        File.Delete(tempFileName);
    }

  }

}
