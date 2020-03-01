using System;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace SudokuSolver
{
  class SolverAvx2
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

    public static int FailedCount = 0;

    public static void GlobalSetup()
    {
      for (int i = 0; i < SUDOKU_CELL_COUNT; i++)
      {
        int row = i / 9;
        int col = i % 9;
        cell2box[i] = ((row / 3 * 3) + (col / 3)) << 4;
      }
    }
    public static void Run(byte[] bytes, bool checkSolutions)
    {
      for (int i = 0; i < bytes.Length; i += BYTES_FOR_16_SUDOKUS)
      {
        var sudokus_16 = new Span<byte>(bytes, i, BYTES_FOR_16_SUDOKUS);
        AllocateAndSolve(sudokus_16, checkSolutions);
      }
    }

    static void AllocateAndSolve(Span<byte> importSudokus, bool checkSolutions)
    {
      // allocate data in its own function should avoid zeroing
      Span<ushort> data = stackalloc ushort[DATA_LENGTH];

      TransposeSudokus(importSudokus, data);

      Solve16Sudokus(data);

      if (checkSolutions)
      {
        Span<ushort> solutions = stackalloc ushort[SUDOKU_CELL_COUNT << 4];
        TransposeSudokus(importSudokus.Slice(SUDOKU_CELL_COUNT + 1), solutions);
        CheckSolutions(data, solutions);
      }
    }

    // either puzzles or solutions
    static unsafe void TransposeSudokus(Span<byte> importSudokus, Span<ushort> parallelSudokus)
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
          *p_dest = (ushort)(1 << (*p_src - '0') >> 1);
          p_src += BYTES_PER_SUDOKU;
          ++p_dest;
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

      var vectorCharOffset = Vector256.Create('0');
      var vectorOnes = Vector256.Create((uint)1);
      v1 = ConvertCharsToBase2(v1, vectorCharOffset, vectorOnes);
      v2 = ConvertCharsToBase2(v2, vectorCharOffset, vectorOnes);
      v3 = ConvertCharsToBase2(v3, vectorCharOffset, vectorOnes);
      v4 = ConvertCharsToBase2(v4, vectorCharOffset, vectorOnes);
      v5 = ConvertCharsToBase2(v5, vectorCharOffset, vectorOnes);
      v6 = ConvertCharsToBase2(v6, vectorCharOffset, vectorOnes);
      v7 = ConvertCharsToBase2(v7, vectorCharOffset, vectorOnes);
      v8 = ConvertCharsToBase2(v8, vectorCharOffset, vectorOnes);

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
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static Vector256<ushort> ConvertCharsToBase2(Vector256<ushort> cellsAsChars, Vector256<ushort> vectorCharOffset, Vector256<uint> vectorOnes)
    {
      var cellsInBase10 = Avx2.Subtract(cellsAsChars, vectorCharOffset);

      // convert values to bits (in low/high as only uint can be shifted) 
      var lowCellsInBase2 = Avx2.ConvertToVector256Int32(Avx2.ExtractVector128(cellsInBase10, 0)).AsUInt32();
      lowCellsInBase2 = Avx2.ShiftRightLogical(Avx2.ShiftLeftLogicalVariable(vectorOnes, lowCellsInBase2), 1);

      var highCellsInBase2 = Avx2.ConvertToVector256Int32(Avx2.ExtractVector128(cellsInBase10, 1)).AsUInt32();
      highCellsInBase2 = Avx2.ShiftRightLogical(Avx2.ShiftLeftLogicalVariable(vectorOnes, highCellsInBase2), 1);

      var cellsInBase2 = Avx2.PackUnsignedSaturate(lowCellsInBase2.AsInt32(), highCellsInBase2.AsInt32()); // pack into low[1..64], high[1..64], low[65..128], high[65..128]
      cellsInBase2 = Avx2.Permute4x64(cellsInBase2.AsUInt64(), 0xD8).AsUInt16(); // shuffle packed bytes into order 0xD8 = 00 10 01 11 = 1,3,2,4
      return cellsInBase2;
    }

    static void Solve16Sudokus(Span<ushort> data)
    {
      SetupStep(data);

      SolveStep(data, iterations: 6);

      SolveRemainingStep(data);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static unsafe void SetupStep(Span<ushort> data)
    {
      fixed (ushort* p_puzzles = &data[0], p_rows = &data[ROW_OFFSET], p_cols = &data[COL_OFFSET], p_boxs = &data[BOX_OFFSET])
      {
        int filterMax = 9 << 4;
        int i, r, c;
        for (i = 0; i < filterMax; i++)
        {
          p_rows[i] = NINE_BITS_MASK;
          p_cols[i] = NINE_BITS_MASK;
          p_boxs[i] = NINE_BITS_MASK;
        }

        i = 0;
        for (r = 0; r < 9; ++r)
        {
          ushort* p_row = &p_rows[r << 4], p_box = &p_boxs[cell2box[i]];
          var rVec = Avx.LoadVector256(p_row);
          var bVec = Avx.LoadVector256(p_box);

          for (c = 0; c < 9; ++c) {
            ushort* p_col = &p_cols[c << 4];
            var pVec = Avx.LoadVector256(&p_puzzles[i << 4]);

            rVec = Avx2.AndNot(pVec, rVec);
            bVec = Avx2.AndNot(pVec, bVec);
            Avx.Store(p_col, Avx2.AndNot(pVec, Avx.LoadVector256(p_col)));

            ++i;
            if (c == 2 || c == 5)
            {
              Avx.Store(p_box, bVec);
              p_box += 16;
              bVec = Avx.LoadVector256(p_box);
            }
          }

          Avx.Store(p_row, rVec);
          Avx.Store(p_box, bVec);
        }
      }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static unsafe void SolveStep(Span<ushort> data, int iterations)
    {
      fixed (ushort* p_puzzles = &data[0], p_rows = &data[ROW_OFFSET], p_cols = &data[COL_OFFSET], p_boxs = &data[BOX_OFFSET])
      {
        int r, c, i;
        var zeroVec = Vector256<ushort>.Zero;
        var oneVec = Vector256.Create((ushort)1);

        Vector256<ushort> rVec, bVec, cVec, pVec, bits, bitsWithoutLSB, mask;
        ushort* p_row, p_box, p_col, p_puzzle;

        do
        {
          i = 0;
          for (r = 0; r < 9; ++r)
          {
            p_row = &p_rows[r << 4];
            rVec = Avx.LoadVector256(p_row);

            if ((uint)Avx2.MoveMask(Avx2.CompareEqual(rVec, zeroVec).AsByte()) == 0xFFFFFFFF)
            {
              i += 9;
              continue;
            }

            p_box = &p_boxs[cell2box[i]];
            bVec = Avx.LoadVector256(p_box);

            for (c = 0; c < 9; ++c)
            {
              p_puzzle = &p_puzzles[i << 4];
              pVec = Avx.LoadVector256(p_puzzle);

              if (Avx2.MoveMask(Avx2.CompareEqual(pVec, zeroVec).AsByte()) != 0)
              {
                p_col = &p_cols[c << 4];
                cVec = Avx.LoadVector256(p_col);

                bits = Avx2.Or(
                  Avx2.And(Avx2.And(rVec, bVec), cVec),
                  pVec
                );

                bitsWithoutLSB = Avx2.And(bits, Avx2.Subtract(bits, oneVec));
                mask = Avx2.CompareEqual(bitsWithoutLSB, zeroVec);

                if (Avx2.MoveMask(mask.AsByte()) != 0)
                {
                  bits = Avx2.And(mask, bits);
                  Avx.Store(p_puzzle, Avx2.Or(bits, pVec));

                  rVec = Avx2.AndNot(bits, rVec);
                  bVec = Avx2.AndNot(bits, bVec);
                  Avx.Store(p_col, Avx2.AndNot(bits, cVec));
                }
              }

              ++i;
              if (c == 2 || c == 5)
              {
                Avx.Store(p_box, bVec);
                p_box += 16;
                bVec = Avx.LoadVector256(p_box);
              }
            }

            Avx.Store(p_row, rVec);
            Avx.Store(p_box, bVec);
          }
        } while (--iterations > 0);
      }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static unsafe void SolveRemainingStep(Span<ushort> data)
    {
      fixed (ushort* p_puzzles = &data[0], p_rows = &data[ROW_OFFSET])
      {
        var zeros = Vector256<short>.Zero;

        int maxI = 9 << 4;
        for (int i = 0; i < maxI; i += 16)
        {
          var mask = Avx2.MoveMask(Avx2.CompareGreaterThan(Avx.LoadVector256(&p_rows[i]).AsInt16(), zeros).AsByte());
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

    static unsafe bool SolveSingleStep(Span<ushort> data, int puzzleOffset)
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

                uint bit2 = Bmi1.ResetLowestSetBit(bits);
                uint bit1 = bits ^ bit2;

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
      return true;
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static unsafe void CheckSolutions(Span<ushort> data, Span<ushort> solutions)
    {
      fixed (ushort* p_puzzle = &data[0], p_solution = &solutions[0])
      {
        // check solution
        int maxI = SUDOKU_CELL_COUNT << 4;
        for (int i = 0; i < maxI; i+=16)
        {
          if ((uint)Avx2.MoveMask(Avx2.CompareEqual(Avx.LoadVector256(&p_puzzle[i]), Avx.LoadVector256(&p_solution[i])).AsByte()) != 0xFFFFFFFF)
          {
            ++FailedCount;
            break;
          }
        }
      }
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
