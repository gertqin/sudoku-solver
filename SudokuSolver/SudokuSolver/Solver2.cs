using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SudokuSolver
{
  class Solver2
  {

    public static void Solve()
    {
      using (var fs = File.OpenRead("../../../../../sudoku.csv"))
      {
        byte[] data = new byte[2];
        fs.Read(data, 0, 2);

      }
    }
  }
}
