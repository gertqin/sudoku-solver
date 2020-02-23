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

    public static void Run()
    {
      //using (var fs = File.OpenRead("../../../../../sudoku.csv"))
      //{
      //  byte[] data = new byte[2];
      //  fs.Read(data, 0, 2);
      //}

      var bytes = File.ReadAllBytes("../../../../../sudoku.csv");

      Thread.Sleep(1000);

      var timer = Stopwatch.StartNew();


      SolveAllSudokus(bytes);

      timer.Stop();

      Console.WriteLine($"Milliseconds to solve 1,000,000 sudokus: {timer.ElapsedMilliseconds}");
      Console.WriteLine($"Failed: {failed}");
    }

    static void SolveAllSudokus(byte[] bytes)
    {
      GlobalSetup();

      for (int i = 0; i < bytes.Length; i += BYTES_FOR_16_SUDOKUS)
      {
        var sudokus_16 = new Span<byte>(bytes, i, BYTES_FOR_16_SUDOKUS);
        AllocateAndSolve(sudokus_16);
      }
    }

    static void GlobalSetup()
    {
      bit2num[0] = 0;
      for (int i = 1; i <= NINE_BITS_MASK; ++i)
      {
        if ((i & (i - 1)) > 0)
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

    static void AllocateAndSolve(Span<byte> importSudokus)
    {
      // allocate data in its own function should avoid zeroing
      Span<ushort> data = stackalloc ushort[DATA_LENGTH];

      Span<ushort> puzzles = stackalloc ushort[SUDOKU_CELL_COUNT << 4];
      Span<ushort> solutions = stackalloc ushort[SUDOKU_CELL_COUNT << 4];

      ExtractSudokus(importSudokus, puzzles);
      ExtractSudokus(importSudokus.Slice(SUDOKU_CELL_COUNT + 1), solutions);

      Solve16Sudokus(data, puzzles, solutions);
    }

    // either puzzles or solutions
    static void ExtractSudokus(Span<byte> importSudokus, Span<ushort> parallelSudokus)
    {
      for (int p = 0; p < 16; ++p)
      {
        int importOffset = p * BYTES_PER_SUDOKU;
        for (int i = 0; i < SUDOKU_CELL_COUNT; ++i)
          parallelSudokus[(i << 4) + p] = importSudokus[importOffset + i];
      }
    }

    static void Solve16Sudokus(Span<ushort> data, Span<ushort> puzzles, Span<ushort> solutions)
    {
      SetupStep(data, puzzles);

      SolveStep(data, iterations: 5);

      SolveRemainingStep(data);

      CheckSolutions(data, solutions);
    }

    static void SetupStep(Span<ushort> data, Span<ushort> puzzleChars)
    {
      unsafe
      {
        fixed (ushort* puzzleChar = &puzzleChars[0], puzzle = &data[0], row = &data[ROW_OFFSET], col = &data[COL_OFFSET], box = &data[BOX_OFFSET])
        {
          for (int i = 0; i < (9 << 4); i++)
          {
            row[i] = NINE_BITS_MASK;
            col[i] = NINE_BITS_MASK;
            box[i] = NINE_BITS_MASK;
          }

          var vectorCharOffset = Vector256.Create('0');
          var vectorOnes = Vector256.Create((uint)1);

          for (int i = 0; i < SUDOKU_CELL_COUNT; i++)
          {
            // subtract '0' from all input values
            var cellsInBase10 = Avx2.Subtract(Avx.LoadVector256(&puzzleChar[i << 4]), vectorCharOffset);

            // convert values to bits (in low/high as only uint can be shifted) 
            var lowCellsInBase2 = Avx2.ConvertToVector256Int32(Avx2.ExtractVector128(cellsInBase10, 0)).AsUInt32();
            lowCellsInBase2 = Avx2.ShiftRightLogical(Avx2.ShiftLeftLogicalVariable(vectorOnes, lowCellsInBase2), 1);

            var highCellsInBase2 = Avx2.ConvertToVector256Int32(Avx2.ExtractVector128(cellsInBase10, 1)).AsUInt32();
            highCellsInBase2 = Avx2.ShiftRightLogical(Avx2.ShiftLeftLogicalVariable(vectorOnes, highCellsInBase2), 1);

            var cellsInBase2 = Avx2.PackUnsignedSaturate(lowCellsInBase2.AsInt32(), highCellsInBase2.AsInt32()); // pack into low[1..64], high[1..64], low[65..128], high[65..128]
            cellsInBase2 = Avx2.Permute4x64(cellsInBase2.AsUInt64(), 0xD8).AsUInt16(); // shuffle packed bytes into order 0xD8 = 00 10 01 11 = 1,3,2,4

            Avx.Store(&puzzle[i << 4], cellsInBase2);

            ushort* r = &row[cell2row[i]], c = &col[cell2col[i]], b = &box[cell2box[i]];
            Avx.Store(r, Avx2.AndNot(cellsInBase2, Avx.LoadVector256(r)));
            Avx.Store(c, Avx2.AndNot(cellsInBase2, Avx.LoadVector256(c)));
            Avx.Store(b, Avx2.AndNot(cellsInBase2, Avx.LoadVector256(b)));
          }
        }
      }
    }

    static void SolveStep(Span<ushort> data, int iterations)
    {
      unsafe
      {
        fixed (ushort* puzzle = &data[0], row = &data[ROW_OFFSET], col = &data[COL_OFFSET], box = &data[BOX_OFFSET])
        {
          do
          {
            for (int i = 0; i != SUDOKU_CELL_COUNT; ++i)
            {
              var p = &puzzle[i << 4];

              if (Avx2.MoveMask(Avx2.CompareEqual(Avx.LoadVector256(p), Vector256<ushort>.Zero).AsByte()) == 0) continue;

              ushort* r = &row[cell2row[i]], c = &col[cell2col[i]], b = &box[cell2box[i]];

              Vector256<ushort> bits = Avx2.Or(
                Avx2.And(Avx2.And(Avx.LoadVector256(r), Avx.LoadVector256(c)), Avx.LoadVector256(b)),
                Avx.LoadVector256(p)
              );

              var mask = Avx2.CompareGreaterThan(Vector256.Create((short)3), bits.AsInt16()).AsUInt16();
              var po2 = Vector256.Create((ushort)4);
              mask = Avx2.Or(mask, Avx2.CompareEqual(bits, po2)); po2 = Avx2.ShiftLeftLogical(po2, 1);
              mask = Avx2.Or(mask, Avx2.CompareEqual(bits, po2)); po2 = Avx2.ShiftLeftLogical(po2, 1);
              mask = Avx2.Or(mask, Avx2.CompareEqual(bits, po2)); po2 = Avx2.ShiftLeftLogical(po2, 1);
              mask = Avx2.Or(mask, Avx2.CompareEqual(bits, po2)); po2 = Avx2.ShiftLeftLogical(po2, 1);
              mask = Avx2.Or(mask, Avx2.CompareEqual(bits, po2)); po2 = Avx2.ShiftLeftLogical(po2, 1);
              mask = Avx2.Or(mask, Avx2.CompareEqual(bits, po2)); po2 = Avx2.ShiftLeftLogical(po2, 1);
              mask = Avx2.Or(mask, Avx2.CompareEqual(bits, po2));

              if (Avx2.MoveMask(mask.AsByte()) != 0)
              {
                bits = Avx2.And(mask, bits);
                Avx.Store(p, Avx2.Or(bits, Avx.LoadVector256(p)));

                Avx.Store(r, Avx2.AndNot(bits, Avx.LoadVector256(r)));
                Avx.Store(c, Avx2.AndNot(bits, Avx.LoadVector256(c)));
                Avx.Store(b, Avx2.AndNot(bits, Avx.LoadVector256(b)));
              }
            }
          } while (--iterations > 0);
        }
      }
    }

    static void SolveRemainingStep(Span<ushort> data)
    {
      unsafe
      {
        fixed (ushort* puzzle = &data[0])
        {
          for (int i = 0; i != ROW_OFFSET; i += 16)
          {
            var mask = Avx2.MoveMask(Avx2.CompareEqual(Avx.LoadVector256(&puzzle[i]), Vector256<ushort>.Zero).AsByte());
            if (mask != 0)
            {
              for (int j = 0; j != 16; ++j)
              {
                if ((mask & 1) == 1)
                  SolveSingleStep(data, j);

                mask >>= 2;
              }
            }
          }
        }
      }
    }

    static bool SolveSingleStep(Span<ushort> data, int puzzleOffset)
    {
      unsafe
      {
        fixed (ushort* puzzle = &data[puzzleOffset], row = &data[ROW_OFFSET + puzzleOffset], col = &data[COL_OFFSET + puzzleOffset], box = &data[BOX_OFFSET + puzzleOffset])
        {
          bool progress = false;
          do
          {
            progress = false;
            for (int i = 0; i != SUDOKU_CELL_COUNT; ++i)
            {
              ushort* p = &puzzle[i << 4];

              if (*p > 0) continue;

              ushort* r = &row[cell2row[i]], c = &col[cell2col[i]], b = &box[cell2box[i]];
              uint bits = (uint)(*r & *c & *b);

              uint bitCount = Popcnt.PopCount(bits);
              if (bitCount == 0)
                return false;
              else if (bitCount == 1)
              {
                *p = (ushort)bits;
                *r = (ushort)(*r & ~bits);
                *c = (ushort)(*c & ~bits);
                *b = (ushort)(*b & ~bits);

                progress = true;
              }
            }
          } while (progress);

          // check if guess is needed
          for (int i = 0; i < SUDOKU_CELL_COUNT; ++i)
          {
            ushort* p = &puzzle[i << 4];
            if (*p > 0) continue;

            ushort* r = &row[cell2row[i]], c = &col[cell2col[i]], b = &box[cell2box[i]];
            uint bits = (uint)(*r & *c & *b);

            if (Popcnt.PopCount(bits) == 2)
            {
              Span<ushort> dataCopy = stackalloc ushort[DATA_LENGTH];
              data.CopyTo(dataCopy);

              uint bit1 = 1;
              while ((bit1 & bits) == 0)
                bit1 <<= 1;

              uint bit2 = Bmi1.ResetLowestSetBit(bits);

              *p = (ushort)bit1;
              *r = (ushort)(*r & ~bit1);
              *c = (ushort)(*c & ~bit1);
              *b = (ushort)(*b & ~bit1);

              if (SolveSingleStep(data, puzzleOffset))
                return true;

              dataCopy.CopyTo(data);

              *p = (ushort)bit2;
              *r = (ushort)(*r & ~bit2);
              *c = (ushort)(*c & ~bit2);
              *b = (ushort)(*b & ~bit2);

              return SolveSingleStep(data, puzzleOffset);
            }
          }
        }
      }
      return true;
    }

    static bool GuessStep(Span<ushort> data, int puzzleOffset)
    {
      unsafe
      {
        fixed (ushort* puzzle = &data[puzzleOffset], row = &data[ROW_OFFSET + puzzleOffset], col = &data[COL_OFFSET + puzzleOffset], box = &data[BOX_OFFSET + puzzleOffset])
        {
          for (int i = 0; i < ROW_OFFSET; i += 16)
          {
            if (puzzle[i] != 0) continue;

            int cell = i >> 4;
            ushort* r = &row[cell2row[cell]], c = &col[cell2col[cell]], b = &box[cell2box[cell]];

            uint bits = (uint)(*r & *c & *b);

            if (Popcnt.PopCount(bits) == 2)
            {
              Span<ushort> dataCopy = stackalloc ushort[DATA_LENGTH];
              data.CopyTo(dataCopy);

              uint bit1 = 1;
              while ((bit1 & bits) == 0)
                bit1 <<= 1;

              uint bit2 = Bmi1.ResetLowestSetBit(bits);

              puzzle[i] = (ushort)bit1;
              *r = (ushort)(*r & ~bit1);
              *c = (ushort)(*c & ~bit1);
              *b = (ushort)(*b & ~bit1);

              // TODO solve

              dataCopy.CopyTo(data);

              puzzle[i] = (ushort)bit2;
              *r = (ushort)(*r & ~bit2);
              *c = (ushort)(*c & ~bit2);
              *b = (ushort)(*b & ~bit2);

              return true;
            }
          }
        }
        return false;
      }
    }




    static void CheckSolutions(Span<ushort> data, Span<ushort> solutions)
    {
      unsafe
      {
        fixed (ushort* puzzle = &data[0], solution = &solutions[0], n2b = num2bit)
        {
          // check solution
          for (int i = 0; i < SUDOKU_CELL_COUNT << 4; i++)
          {
            var res = n2b[solution[i]];
            if (puzzle[i] != res)
            {
              failed++;
              break;
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
