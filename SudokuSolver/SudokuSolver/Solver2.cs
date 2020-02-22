#define TIME 

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Text;
using System.Threading;

namespace SudokuSolver
{
  class Solver2
  {
    const int SUDOKU_CELL_COUNT = 81;
    const int ROW_OFFSET = SUDOKU_CELL_COUNT << 4;
    const int COL_OFFSET = ROW_OFFSET + (9 << 4);
    const int BOX_OFFSET = COL_OFFSET + (9 << 4);
    const int DATA_LENGTH = BOX_OFFSET + (9 << 4);

    const int BYTES_PER_SUDOKU = (SUDOKU_CELL_COUNT + 1) * 2; // 82 because of ',' and '\n'
    const int BYTES_FOR_16_SUDOKUS = BYTES_PER_SUDOKU << 4;

    const ushort NINE_BITS_MASK = (1 << 9) - 1;

    static int[] bit2num = new int[NINE_BITS_MASK + 1];
    static ushort[] num2bit = new ushort['9' + 1];

    static int[] cell2row = new int[SUDOKU_CELL_COUNT];
    static int[] cell2col = new int[SUDOKU_CELL_COUNT];
    static int[] cell2box = new int[SUDOKU_CELL_COUNT];

    static int failed = 0;

    public static void Solve()
    {
      //using (var fs = File.OpenRead("../../../../../sudoku.csv"))
      //{
      //  byte[] data = new byte[2];
      //  fs.Read(data, 0, 2);
      //}

      var bytes = File.ReadAllBytes("../../../../../sudoku.csv");

      Thread.Sleep(1000);

      var timer = Stopwatch.StartNew();

      PrepareAndSolve(bytes);

      timer.Stop();

      Console.WriteLine($"Milliseconds to solve 1,000,000 sudokus: {timer.ElapsedMilliseconds}");
      Console.WriteLine($"Failed: {failed}");
    }

    static void PrepareAndSolve(byte[] bytes)
    {
      Prepare();

      for (int i = 0; i < bytes.Length; i += BYTES_FOR_16_SUDOKUS)
      {
        var sudokus_16 = new Span<byte>(bytes, i, BYTES_FOR_16_SUDOKUS);
        Solve(sudokus_16);
      }
    }

    static void Prepare()
    {
      bit2num[0] = 0;
      for (int i = 1; i <= (int)NINE_BITS_MASK; ++i)
      {
        if ((i & (i-1)) > 0)
        {
          bit2num[i] = 0;
        }
        else
        { // single bit number
          int bit = i;
          int num = 1;
          while ((bit >>= 1) > 0)
            num++;

          bit2num[i] = num;
        }
      }

      for (int i = '1'; i <= '9'; i++)
        num2bit[i] = (ushort)(1 << (i - '1'));

      for (int i = 0; i < SUDOKU_CELL_COUNT; i++)
      {
        int row = i / 9;
        int col = i % 9;
        cell2row[i] = row << 4;
        cell2col[i] = col << 4;
        cell2box[i] = ((row / 3 * 3) + (col / 3)) << 4;
      }
    }

    static void Solve(Span<byte> sudokus)
    {
      Span<ushort> data = stackalloc ushort[DATA_LENGTH];

      unsafe
      {
        fixed (ushort* puzzle = &data[0], row = &data[ROW_OFFSET], col = &data[COL_OFFSET], box = &data[BOX_OFFSET])
        {
          for (int i = 0; i < (9 << 4); i++)
          {
            row[i] = NINE_BITS_MASK;
            col[i] = NINE_BITS_MASK;
            box[i] = NINE_BITS_MASK;
          }

          // setup puzzles
          for (int i = 0; i < SUDOKU_CELL_COUNT; i++)
            for (int j = 0; j < 16; j++)
              puzzle[(i << 4) + j] = num2bit[sudokus[j * BYTES_PER_SUDOKU + i]];

          for (int i = 0; i < SUDOKU_CELL_COUNT; i++)
          {
            var pVector = Avx.LoadVector256(&puzzle[i << 4]);

            ushort* r = &row[cell2row[i]], c = &col[cell2col[i]], b = &box[cell2box[i]];
            Avx.Store(r, Avx2.AndNot(pVector, Avx.LoadVector256(r)));
            Avx.Store(c, Avx2.AndNot(pVector, Avx.LoadVector256(c)));
            Avx.Store(b, Avx2.AndNot(pVector, Avx.LoadVector256(b)));
          }
        }
      }

      unsafe
      {
        fixed (ushort* puzzle = &data[0], row = &data[ROW_OFFSET], col = &data[COL_OFFSET], box = &data[BOX_OFFSET])
        {
          int maxIterations = 4;
          do
          {
            for (int i = 0; i < SUDOKU_CELL_COUNT; i++)
            {
              var p = &puzzle[i << 4];

              if (Avx2.MoveMask(Avx2.CompareEqual(Avx.LoadVector256(p), Vector256<ushort>.Zero).AsByte()) == 0) continue;

              ushort* r = &row[cell2row[i]], c = &col[cell2col[i]], b = &box[cell2box[i]];

              Vector256<ushort> bits = Avx2.Or(
                Avx2.And(Avx2.And(Avx.LoadVector256(r), Avx.LoadVector256(c)), Avx.LoadVector256(b)),
                Avx.LoadVector256(p)
              );

              var po2 = Vector256.Create((ushort)1);
              var mask = Vector256<ushort>.Zero;
              for (ushort j = 1; j <= 9; j++)
              {
                mask = Avx2.Or(mask, Avx2.CompareEqual(bits, po2));
                po2 = Avx2.ShiftLeftLogical(po2, 1);
              }

              if (Avx2.MoveMask(mask.AsByte()) != 0)
              {
                bits = Avx2.And(mask, bits);
                Avx.Store(p, Avx2.Or(bits, Avx.LoadVector256(p)));

                Avx.Store(r, Avx2.AndNot(bits, Avx.LoadVector256(r)));
                Avx.Store(c, Avx2.AndNot(bits, Avx.LoadVector256(c)));
                Avx.Store(b, Avx2.AndNot(bits, Avx.LoadVector256(b)));
              }
            }
          } while (maxIterations-- > 0);
      }
      }

      unsafe
      {
        fixed (ushort* puzzle = &data[0], n2b = num2bit)
        {
          // check solution
          for (int j = 0; j < 16; j++)
          {
            for (int i = 0; i < SUDOKU_CELL_COUNT; i++)
            {
              var res = n2b[sudokus[j * BYTES_PER_SUDOKU + i + 82]];
              if (puzzle[(i << 4) + j] != res)
              {
                failed++;
                break;
              }
            }
          }
        }
      }
    }

    static string PrintSudoku(Span<ushort> data)
    {
      string res = "";
      for (int i = 0; i < SUDOKU_CELL_COUNT; i++)
      {
        if (i > 0)
        {
          if (i % 3 == 0)
            res += " ";
          if (i % 9 == 0)
            res += "\n";
          if (i % 27 == 0)
            res += "\n";
        }
        res += bit2num[data[i << 4]];
      }
      return res;
    }
  }
}
