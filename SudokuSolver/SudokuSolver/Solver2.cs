#define CHECK_SOLUTION

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
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
      for (int i = 0; i < SUDOKU_CELL_COUNT; i++)
      {
        int row = i / 9;
        int col = i % 9;
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

#if CHECK_SOLUTION
      ExtractSudokus(importSudokus.Slice(SUDOKU_CELL_COUNT + 1), solutions);
#endif

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

#if CHECK_SOLUTION
      CheckSolutions(data, solutions);
#endif
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static void SetupStep(Span<ushort> data, Span<ushort> puzzleChars)
    {
      unsafe
      {
        fixed (ushort* p_puzzleChars = &puzzleChars[0], p_puzzles = &data[0], p_rows = &data[ROW_OFFSET], p_cols = &data[COL_OFFSET], p_boxs = &data[BOX_OFFSET])
        {
          int filterMax = 9 << 4;
          for (int i = 0; i < filterMax; i++)
          {
            p_rows[i] = NINE_BITS_MASK;
            p_cols[i] = NINE_BITS_MASK;
            p_boxs[i] = NINE_BITS_MASK;
          }

          var vectorCharOffset = Vector256.Create('0');
          var vectorOnes = Vector256.Create((uint)1);

          int r = 0, c = 0;
          for (int i = 0; i < SUDOKU_CELL_COUNT; i++)
          {
            // subtract '0' from all input values
            var cellsInBase10 = Avx2.Subtract(Avx.LoadVector256(&p_puzzleChars[i << 4]), vectorCharOffset);
            var cellsInBase2 = ConvertBase10ToBase2(cellsInBase10, vectorOnes);

            Avx.Store(&p_puzzles[i << 4], cellsInBase2);

            ushort* p_row = &p_rows[r << 4], p_col = &p_cols[c << 4], p_box = &p_boxs[cell2box[i]];
            Avx.Store(p_row, Avx2.AndNot(cellsInBase2, Avx.LoadVector256(p_row)));
            Avx.Store(p_col, Avx2.AndNot(cellsInBase2, Avx.LoadVector256(p_col)));
            Avx.Store(p_box, Avx2.AndNot(cellsInBase2, Avx.LoadVector256(p_box)));

            ++c;
            if (c == 9)
            {
              c = 0;
              ++r;
            }
          }
        }
      }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static void SolveStep(Span<ushort> data, int iterations)
    {
      unsafe
      {
        fixed (ushort* p_puzzles = &data[0], p_rows = &data[ROW_OFFSET], p_cols = &data[COL_OFFSET], p_boxs = &data[BOX_OFFSET])
        {
          do
          {
            int r = 0, c = 0;
            for (int i = 0; i != SUDOKU_CELL_COUNT; ++i)
            {
              var p_puzzle = &p_puzzles[i << 4];

              if (Avx2.MoveMask(Avx2.CompareEqual(Avx.LoadVector256(p_puzzle), Vector256<ushort>.Zero).AsByte()) != 0)
              {
                ushort* p_row = &p_rows[r << 4],
                  p_col = &p_cols[c << 4],
                  p_box = &p_boxs[cell2box[i]];

                Vector256<ushort> bits = Avx2.Or(
                  Avx2.And(Avx2.And(Avx.LoadVector256(p_row), Avx.LoadVector256(p_col)), Avx.LoadVector256(p_box)),
                  Avx.LoadVector256(p_puzzle)
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
                  Avx.Store(p_puzzle, Avx2.Or(bits, Avx.LoadVector256(p_puzzle)));

                  Avx.Store(p_row, Avx2.AndNot(bits, Avx.LoadVector256(p_row)));
                  Avx.Store(p_col, Avx2.AndNot(bits, Avx.LoadVector256(p_col)));
                  Avx.Store(p_box, Avx2.AndNot(bits, Avx.LoadVector256(p_box)));
                }
              }

              ++c;
              if (c == 9)
              {
                c = 0;
                ++r;
              }
            }
          } while (--iterations > 0);
        }
      }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static void SolveRemainingStep(Span<ushort> data)
    {
      unsafe
      {
        fixed (ushort* p_puzzles = &data[0])
        {
          for (int i = 0; i != ROW_OFFSET; i += 16)
          {
            var mask = Avx2.MoveMask(Avx2.CompareEqual(Avx.LoadVector256(&p_puzzles[i]), Vector256<ushort>.Zero).AsByte());
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
        fixed (ushort* p_puzzles = &data[puzzleOffset], p_rows = &data[ROW_OFFSET + puzzleOffset], p_cols = &data[COL_OFFSET + puzzleOffset], p_boxs = &data[BOX_OFFSET + puzzleOffset])
        {
          bool progress = false;
          do
          {
            progress = false;
            int r = 0, c = 0;
            for (int i = 0; i != SUDOKU_CELL_COUNT; ++i)
            {
              ushort* p_puzzle = &p_puzzles[i << 4];

              if (*p_puzzle == 0)
              {
                ushort* p_row = &p_rows[r << 4], p_col = &p_cols[c << 4], p_box = &p_boxs[cell2box[i]];
                uint bits = (uint)(*p_row & *p_col & *p_box);

                uint bitCount = Popcnt.PopCount(bits);
                if (bitCount == 0)
                  return false;
                else if (bitCount == 1)
                {
                  *p_puzzle = (ushort)bits;
                  *p_row = (ushort)(*p_row & ~bits);
                  *p_col = (ushort)(*p_col & ~bits);
                  *p_box = (ushort)(*p_box & ~bits);

                  progress = true;
                }
              }

              ++c;
              if (c == 9)
              {
                c = 0;
                ++r;
              }
            }
          } while (progress);

          // check if guess is needed
          {
            int r = 0, c = 0;
            for (int i = 0; i < SUDOKU_CELL_COUNT; ++i)
            {
              ushort* p_puzzle = &p_puzzles[i << 4];
              if (*p_puzzle == 0)
              {
                ushort* p_row = &p_rows[r << 4], p_col = &p_cols[c << 4], p_box = &p_boxs[cell2box[i]];
                uint bits = (uint)(*p_row & *p_col & *p_box);

                if (Popcnt.PopCount(bits) == 2)
                {
                  Span<ushort> dataCopy = stackalloc ushort[DATA_LENGTH];
                  data.CopyTo(dataCopy);

                  uint bit1 = 1;
                  while ((bit1 & bits) == 0)
                    bit1 <<= 1;

                  uint bit2 = Bmi1.ResetLowestSetBit(bits);

                  *p_puzzle = (ushort)bit1;
                  *p_row = (ushort)(*p_row & ~bit1);
                  *p_col = (ushort)(*p_col & ~bit1);
                  *p_box = (ushort)(*p_box & ~bit1);

                  if (SolveSingleStep(data, puzzleOffset))
                    return true;

                  dataCopy.CopyTo(data);

                  *p_puzzle = (ushort)bit2;
                  *p_row = (ushort)(*p_row & ~bit2);
                  *p_col = (ushort)(*p_col & ~bit2);
                  *p_box = (ushort)(*p_box & ~bit2);

                  return SolveSingleStep(data, puzzleOffset);
                }
              }

              ++c;
              if (c == 9)
              {
                c = 0;
                ++r;
              }
            }
          }
        }
      }
      return true;
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static void CheckSolutions(Span<ushort> data, Span<ushort> solutions)
    {
      unsafe
      {
        fixed (ushort* p_puzzles = &data[0], p_solution = &solutions[0])
        {
          var vectorCharOffset = Vector256.Create('0');
          var vectorOnes = Vector256.Create((uint)1);

          // check solution
          for (int i = 0; i < SUDOKU_CELL_COUNT; i++)
          {
            var solutionCellsBase10 = Avx2.Subtract(Avx.LoadVector256(&p_solution[i << 4]), vectorCharOffset);
            var solutionCellsBase2 = ConvertBase10ToBase2(solutionCellsBase10, vectorOnes);
            if ((uint)Avx2.MoveMask(Avx2.CompareEqual(Avx.LoadVector256(&p_puzzles[i << 4]), solutionCellsBase2).AsByte()) != 0xFFFFFFFF)
            {
              failed++;
              break;
            }
          }
        }
      }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static Vector256<ushort> ConvertBase10ToBase2(Vector256<ushort> cellsInBase10, Vector256<uint> vectorOnes)
    {
      // convert values to bits (in low/high as only uint can be shifted) 
      var lowCellsInBase2 = Avx2.ConvertToVector256Int32(Avx2.ExtractVector128(cellsInBase10, 0)).AsUInt32();
      lowCellsInBase2 = Avx2.ShiftRightLogical(Avx2.ShiftLeftLogicalVariable(vectorOnes, lowCellsInBase2), 1);

      var highCellsInBase2 = Avx2.ConvertToVector256Int32(Avx2.ExtractVector128(cellsInBase10, 1)).AsUInt32();
      highCellsInBase2 = Avx2.ShiftRightLogical(Avx2.ShiftLeftLogicalVariable(vectorOnes, highCellsInBase2), 1);

      var cellsInBase2 = Avx2.PackUnsignedSaturate(lowCellsInBase2.AsInt32(), highCellsInBase2.AsInt32()); // pack into low[1..64], high[1..64], low[65..128], high[65..128]
      cellsInBase2 = Avx2.Permute4x64(cellsInBase2.AsUInt64(), 0xD8).AsUInt16(); // shuffle packed bytes into order 0xD8 = 00 10 01 11 = 1,3,2,4
      return cellsInBase2;
    }

    static string PrintSudoku(Span<ushort> data)
    {
      int[] bit2num = new int[NINE_BITS_MASK + 1];
      bit2num[0] = 0;
      int num = 1, bit = 1;

      while (num <= 9)
      {
        bit2num[bit] = num;
        bit <<= 1;
        ++num;
      } 

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
