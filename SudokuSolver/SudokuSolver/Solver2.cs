﻿#define CHECK_SOLUTION
//#define RUN_MULTIPLE_LOOPS

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace SudokuSolver
{
  class Solver2
  {
    const int MULTIPLE_RUN_COUNT = 10;

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
      long readInputMs = 0;
      var timer = Stopwatch.StartNew();

      var bytes = File.ReadAllBytes("../../../../../sudoku.csv");

      timer.Stop();
      readInputMs = timer.ElapsedMilliseconds;
      timer.Restart();

      SolveAllSudokus(bytes);

      timer.Stop();

      var sudokuCount = 1000000;
#if RUN_MULTIPLE_LOOPS
      sudokuCount *= MULTIPLE_RUN_COUNT;
#endif

      Console.WriteLine($"Time to read input: {readInputMs}ms");
      Console.WriteLine($"Time to solve {sudokuCount.ToString("N0")} sudokus: {timer.ElapsedMilliseconds}ms");
      Console.WriteLine($"Failed sudokus: {failed}");
    }

    static void SolveAllSudokus(byte[] bytes)
    {
      GlobalSetup();

#if RUN_MULTIPLE_LOOPS
      for (int n = 0; n < MULTIPLE_RUN_COUNT; n++)
#endif
      {
        for (int i = 0; i < bytes.Length; i += BYTES_FOR_16_SUDOKUS)
        {
          var sudokus_16 = new Span<byte>(bytes, i, BYTES_FOR_16_SUDOKUS);
          AllocateAndSolve(sudokus_16);
        }
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

      TransposeSudokus(importSudokus, puzzles);

#if CHECK_SOLUTION
      TransposeSudokus(importSudokus.Slice(SUDOKU_CELL_COUNT + 1), solutions);
#endif

      Solve16Sudokus(data, puzzles, solutions);
    }

    // either puzzles or solutions
    static void TransposeSudokus(Span<byte> importSudokus, Span<ushort> parallelSudokus)
    {
      unsafe
      {
        fixed (byte* p_importSudokus = &importSudokus[0])
        fixed (ushort* p_parallelSudokus = &parallelSudokus[0])
        {
          byte* p_src = p_importSudokus;
          ushort* p_dest = p_parallelSudokus;

          // 5x16 = 80
          for (int i = 0; i < 5; i++)
          {
            // solve 16x16
            Transpose8x16(p_src, p_dest);
            Transpose8x16(p_src + (BYTES_PER_SUDOKU << 3), p_dest + 8);

            p_src += 16;
            p_dest += (1 << 8);
          }

          for (int i = 0; i < 16; i++)
          {
            *p_dest = *p_src;
            p_src += BYTES_PER_SUDOKU;
            ++p_dest;
          }
        }
      }
    }

    // transpose 8 rows x 16 cols, with input in bytes and output in ushorts
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static unsafe void Transpose8x16(byte *p_src, ushort *p_dest)
    {
      var v1 = Avx2.ConvertToVector256Int16(p_src).AsUInt16(); p_src += BYTES_PER_SUDOKU; // a1,b1,c1,...,p1
      var v2 = Avx2.ConvertToVector256Int16(p_src).AsUInt16(); p_src += BYTES_PER_SUDOKU; // a2,b2,c2,...,p2
      var v3 = Avx2.ConvertToVector256Int16(p_src).AsUInt16(); p_src += BYTES_PER_SUDOKU;
      var v4 = Avx2.ConvertToVector256Int16(p_src).AsUInt16(); p_src += BYTES_PER_SUDOKU;
      var v5 = Avx2.ConvertToVector256Int16(p_src).AsUInt16(); p_src += BYTES_PER_SUDOKU;
      var v6 = Avx2.ConvertToVector256Int16(p_src).AsUInt16(); p_src += BYTES_PER_SUDOKU;
      var v7 = Avx2.ConvertToVector256Int16(p_src).AsUInt16(); p_src += BYTES_PER_SUDOKU; // a7,b7,c7,...,p7
      var v8 = Avx2.ConvertToVector256Int16(p_src).AsUInt16();                           

      var lo12 = Avx2.UnpackLow(v1, v2); // a1,a2,b1,...,d2,i1,i2,k1,...,l2
      var lo34 = Avx2.UnpackLow(v3, v4); // a3,a4,b3,...,d2,i3,i4,k3,...,l4
      var lo56 = Avx2.UnpackLow(v5, v6);
      var lo78 = Avx2.UnpackLow(v7, v8);
      var hi12 = Avx2.UnpackHigh(v1, v2); // e1,e2,f1,...,h2,m1,m2,...,p2
      var hi34 = Avx2.UnpackHigh(v3, v4);
      var hi56 = Avx2.UnpackHigh(v5, v6);
      var hi78 = Avx2.UnpackHigh(v7, v8);

      v1 = lo12; v2 = lo34; v3 = lo56; v4 = lo78; v5 = hi12; v6 = hi34; v7 = hi56; v8 = hi78;

      lo12 = Avx2.UnpackLow(v1.AsUInt32(), v2.AsUInt32()).AsUInt16(); // a1,a2,a3,a4,b1,...,i1,i2,...,j4
      lo34 = Avx2.UnpackLow(v3.AsUInt32(), v4.AsUInt32()).AsUInt16(); // a5,a6,a7,a8,b5,...,i1,i2,...,j8
      lo56 = Avx2.UnpackLow(v5.AsUInt32(), v6.AsUInt32()).AsUInt16(); // c1,c2,...k4
      lo78 = Avx2.UnpackLow(v7.AsUInt32(), v8.AsUInt32()).AsUInt16();
      hi12 = Avx2.UnpackHigh(v1.AsUInt32(), v2.AsUInt32()).AsUInt16();
      hi34 = Avx2.UnpackHigh(v3.AsUInt32(), v4.AsUInt32()).AsUInt16();
      hi56 = Avx2.UnpackHigh(v5.AsUInt32(), v6.AsUInt32()).AsUInt16();
      hi78 = Avx2.UnpackHigh(v7.AsUInt32(), v8.AsUInt32()).AsUInt16(); // g5,g6,g7,g8,h5,...,p8

      v1 = lo12; v2 = lo34; v3 = hi12; v4 = hi34; v5 = lo56; v6 = lo78; v7 = hi56; v8 = hi78;

      lo12 = Avx2.UnpackLow(v1.AsUInt64(), v2.AsUInt64()).AsUInt16(); // a1,a2,a3,a4,a5,a6,a7,8,i1,...,i8
      lo34 = Avx2.UnpackLow(v3.AsUInt64(), v4.AsUInt64()).AsUInt16(); // b1,b2,...,k8
      lo56 = Avx2.UnpackLow(v5.AsUInt64(), v6.AsUInt64()).AsUInt16();
      lo78 = Avx2.UnpackLow(v7.AsUInt64(), v8.AsUInt64()).AsUInt16();
      hi12 = Avx2.UnpackHigh(v1.AsUInt64(), v2.AsUInt64()).AsUInt16();
      hi34 = Avx2.UnpackHigh(v3.AsUInt64(), v4.AsUInt64()).AsUInt16();
      hi56 = Avx2.UnpackHigh(v5.AsUInt64(), v6.AsUInt64()).AsUInt16();
      hi78 = Avx2.UnpackHigh(v7.AsUInt64(), v8.AsUInt64()).AsUInt16(); // h1,h2,...,p8

      v1 = lo12; v2 = hi12; v3 = lo34; v4 = hi34; v5 = lo56; v6 = hi56; v7 = lo78; v8 = hi78;

      Sse2.Store(p_dest, v1.GetLower());
      Sse2.Store(p_dest + (1 << 4), v2.GetLower());
      Sse2.Store(p_dest + (2 << 4), v3.GetLower());
      Sse2.Store(p_dest + (3 << 4), v4.GetLower());
      Sse2.Store(p_dest + (4 << 4), v5.GetLower());
      Sse2.Store(p_dest + (5 << 4), v6.GetLower());
      Sse2.Store(p_dest + (6 << 4), v7.GetLower());
      Sse2.Store(p_dest + (7 << 4), v8.GetLower());
      Sse2.Store(p_dest + (8 << 4), v1.GetUpper());
      Sse2.Store(p_dest + (9 << 4), v2.GetUpper());
      Sse2.Store(p_dest + (10 << 4), v3.GetUpper());
      Sse2.Store(p_dest + (11 << 4), v4.GetUpper());
      Sse2.Store(p_dest + (12 << 4), v5.GetUpper());
      Sse2.Store(p_dest + (13 << 4), v6.GetUpper());
      Sse2.Store(p_dest + (14 << 4), v7.GetUpper());
      Sse2.Store(p_dest + (15 << 4), v8.GetUpper());
    }

    static void Solve16Sudokus(Span<ushort> data, Span<ushort> puzzles, Span<ushort> solutions)
    {
      SetupStep(data, puzzles);

      SolveStep(data, iterations: 6);

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
            ushort* p_puzzle = &p_puzzles[0], p_row = &p_rows[0], p_col = &p_cols[0], p_box = &p_boxs[0];
            for (int i = 0; i < 9; i++)
            {
              if (i == 3 || i == 6)
                p_box += 3 << 4;
              FillRow(p_puzzle, p_row, p_col, p_box);
              p_puzzle += 9 << 4;
              p_row += 16;
            }
          } while (--iterations > 0);
        }
      }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static unsafe void FillRow(ushort* p_puzzle, ushort* p_row, ushort* p_col, ushort* p_box)
    {
      var row = Avx.LoadVector256(p_row);
      for (int i = 0; i < 3; i++)
      {
        var box = Avx.LoadVector256(p_box);
        var rowAndBox = Avx2.And(row, box);
        FillCell(p_puzzle, p_row, p_col, p_box, rowAndBox); p_col += 16; p_puzzle += 16;
        FillCell(p_puzzle, p_row, p_col, p_box, rowAndBox); p_col += 16; p_puzzle += 16;
        FillCell(p_puzzle, p_row, p_col, p_box, rowAndBox); p_col += 16; p_puzzle += 16;
        p_box += 16;
      }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static unsafe void FillCell(ushort* p_puzzle, ushort* p_row, ushort* p_col, ushort* p_box, Vector256<ushort> rowAndBox)
    {
      var zeros = Vector256<ushort>.Zero;
      if (Avx2.MoveMask(Avx2.CompareEqual(Avx.LoadVector256(p_puzzle), zeros).AsByte()) != 0)
      {
        Vector256<ushort> bits = Avx2.Or(
          Avx2.And(rowAndBox, Avx.LoadVector256(p_col)),
          Avx.LoadVector256(p_puzzle)
        );

        var bitsWithoutLSB = Avx2.And(bits, Avx2.Subtract(bits, Vector256.Create((ushort)1)));
        var mask = Avx2.CompareEqual(bitsWithoutLSB, zeros);

        if (Avx2.MoveMask(mask.AsByte()) != 0)
        {
          bits = Avx2.And(mask, bits);
          Avx.Store(p_puzzle, Avx2.Or(bits, Avx.LoadVector256(p_puzzle)));

          Avx.Store(p_row, Avx2.AndNot(bits, Avx.LoadVector256(p_row)));
          Avx.Store(p_col, Avx2.AndNot(bits, Avx.LoadVector256(p_col)));
          Avx.Store(p_box, Avx2.AndNot(bits, Avx.LoadVector256(p_box)));
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
