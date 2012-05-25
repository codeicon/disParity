using System;
using System.Collections.Generic;
using System.Text;

namespace disParity
{

  // to do: Move parity MMF handling directly into this class 

  internal class ParityChange : IDisposable
  {

    private Parity parity;
    private ParityBlock parityBlock;
    private UInt32 block;

    public ParityChange(Parity parity, UInt32 startBlock, UInt32 lengthInBlocks)
    {
      this.parity = parity;
      parity.OpenTempParity(startBlock, lengthInBlocks);
      parityBlock = new ParityBlock(parity);
      block = startBlock;
    }

    /// <summary>
    /// Clear parity block in preparation to receive data
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

    public void Write()
    {
      parity.WriteTempBlock(0 /* Not used */, parityBlock.Data);
      block++;
    }

    public void Save()
    {
      parity.FlushTemp();
    }

    public void Dispose()
    {
      parity.CloseTemp();
    }

  }

}
