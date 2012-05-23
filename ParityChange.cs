using System;
using System.Collections.Generic;
using System.Text;

namespace disParity
{

  // to do: Move parity MMF handling directly into this class 

  internal class ParityChange : IDisposable
  {

    private byte[] parity;
    private UInt32 block;

    public ParityChange(UInt32 startBlock, UInt32 lengthInBlocks)
    {
      Parity.OpenTempParity(startBlock, lengthInBlocks);
      parity = new byte[Parity.BlockSize];
      block = startBlock;
    }

    /// <summary>
    /// Clear parity block in preparation to receive data
    /// </summary>
    public void Reset(bool fromParity)
    {
      if (fromParity)
        // todo: handle parity block read failure (fatal error!)
        Parity.ReadBlock(block, parity);
      else
        Array.Clear(parity, 0, Parity.BlockSize);
    }

    /// <summary>
    /// Add more data to the current block by XOR'ing it in
    /// </summary>
    public void AddData(byte[] data)
    {
      Parity.FastXOR(parity, data);
    }

    public void Write()
    {
      Parity.WriteTempBlock(0 /* Not used */, parity);
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
