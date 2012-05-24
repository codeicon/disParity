using System;
using System.Collections.Generic;
using System.Text;

namespace disParity
{

  // to do: Move parity MMF handling directly into this class 

  internal class ParityChange : IDisposable
  {

    private ParityBlock parity;
    private UInt32 block;

    public ParityChange(UInt32 startBlock, UInt32 lengthInBlocks)
    {
      Parity.OpenTempParity(startBlock, lengthInBlocks);
      parity = new ParityBlock();
      block = startBlock;
    }

    /// <summary>
    /// Clear parity block in preparation to receive data
    /// </summary>
    public void Reset(bool fromParity)
    {
      if (fromParity)
        // todo: handle parity block read failure (fatal error!)
        parity.Load(block);
      else
        parity.Clear();
    }

    /// <summary>
    /// Add more data to the current block by XOR'ing it in
    /// </summary>
    public void AddData(byte[] data)
    {
      parity.Add(data);
    }

    public void Write()
    {
      Parity.WriteTempBlock(0 /* Not used */, parity.Data);
      block++;
    }

    public void Save()
    {
      Parity.FlushTemp();
    }

    public void Dispose()
    {
      Parity.CloseTemp();
    }

  }

}
