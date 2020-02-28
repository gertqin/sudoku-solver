//#define TIME 

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics.X86;
using System.Text;

namespace SudokuSolver
{
  class SolverX64
  {
    const int SUDOKU_CELL_COUNT = 81;
    const int BYTES_PER_SUDOKU = (SUDOKU_CELL_COUNT + 1) * 2; // 82 because of ',' and '\n'
    const int BYTES_FOR_7_SUDOKUS = BYTES_PER_SUDOKU * 7;

    const int PARALLEL_COUNT = 7;
    const int ROW_OFFSET = SUDOKU_CELL_COUNT + 81;
    const int COL_OFFSET = ROW_OFFSET + 9;
    const int BOX_OFFSET = COL_OFFSET + 9;
    const int DATA_LENGTH = BOX_OFFSET + 9;

    const ulong NINE_BITS_MASK = (1 << 9) - 1;
    const ulong ALL_BITS_MASK = ((ulong)1 << (9 * 7)) - 1;

    static int[] bit2num = new int[NINE_BITS_MASK + 1];
    static ulong[] num2bit = new ulong['9' + 1];

    static ulong[] num2mask = new ulong['9' + 1];
    static ulong[] bit2bit = new ulong[NINE_BITS_MASK + 1];

    static int[] cell2row = new int[SUDOKU_CELL_COUNT];
    static int[] cell2col = new int[SUDOKU_CELL_COUNT];
    static int[] cell2box = new int[SUDOKU_CELL_COUNT];

    public static int FailedCount = 0;

    public static void GlobalSetup()
    {
      bit2num[0] = '0';
      for (int i = 1; i <= (int)NINE_BITS_MASK; ++i)
      {
        if (removeBit(i) > 0)
        {
          bit2num[i] = 0;
        }
        else
        { // single bit number
          int bit = i;
          int num = 1;
          while ((bit >>= 1) > 0)
          {
            num++;
          }
          bit2num[i] = (num + '0');

          bit2bit[i] = (ulong)i;
        }
      }

      num2mask['0'] = NINE_BITS_MASK;
      for (int i = '1'; i <= '9'; i++)
      {
        num2bit[i] = (ulong)(1 << (i - '1'));
      }

      for (int i = 0; i < SUDOKU_CELL_COUNT; i++)
      {
        int row = i / 9;
        int col = i % 9;
        cell2row[i] = row;
        cell2col[i] = col;
        cell2box[i] = ((row / 3 * 3) + (col / 3));
      }
    }

    public static void Run(byte[] bytes, bool checkSolution)
    {
      int sudokuCount = bytes.Length / BYTES_PER_SUDOKU;

      var parallelCount = sudokuCount - (sudokuCount % PARALLEL_COUNT);
      for (int i = 0; i < parallelCount; i += 7)
      {
        var sudokus_7 = new Span<byte>(bytes, i * BYTES_PER_SUDOKU, BYTES_FOR_7_SUDOKUS);
        AllocateAndSolve(sudokus_7, checkSolution);
      }

      for (int i = parallelCount; i < sudokuCount; i++)
      {
        var sudoku = new Span<byte>(bytes, i * BYTES_PER_SUDOKU, BYTES_PER_SUDOKU);
        SimpleSolve(sudoku);
      }
    }


    static void AllocateAndSolve(Span<byte> importSudokus, bool checkSolutions)
    {
      // allocate data in its own function should avoid zeroing
      Span<ulong> data = stackalloc ulong[DATA_LENGTH];

      SetupStep(importSudokus, data);

      SolveFast(data);
      
      if (checkSolutions)
      {
        CheckSolutions(importSudokus, data);
      }
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static unsafe void SetupStep(Span<byte> sudokus, Span<ulong> data)
    {
      fixed (byte* p_sudokus = sudokus)
      fixed (int* c2r = cell2row, c2c = cell2col, c2b = cell2box)
      fixed (ulong* puzzle = &data[0], row = &data[ROW_OFFSET], col = &data[COL_OFFSET], box = &data[BOX_OFFSET], n2b = num2bit, n2m = num2mask)
      {
        for (int i = 0; i < 9; i++)
        {
          row[i] = ALL_BITS_MASK;
          col[i] = ALL_BITS_MASK;
          box[i] = ALL_BITS_MASK;
        }

        int offset1 = BYTES_PER_SUDOKU,
          offset2 = offset1 + BYTES_PER_SUDOKU,
          offset3 = offset2 + BYTES_PER_SUDOKU,
          offset4 = offset3 + BYTES_PER_SUDOKU,
          offset5 = offset4 + BYTES_PER_SUDOKU,
          offset6 = offset5 + BYTES_PER_SUDOKU;

        // setup puzzles
        for (int i = 0; i < SUDOKU_CELL_COUNT; i++)
        {
          puzzle[i] = n2b[sudokus[i]]
            | (n2b[sudokus[offset1 + i]] << 9)
            | (n2b[sudokus[offset2 + i]] << 18)
            | (n2b[sudokus[offset3 + i]] << 27)
            | (n2b[sudokus[offset4 + i]] << 36)
            | (n2b[sudokus[offset5 + i]] << 45)
            | (n2b[sudokus[offset6 + i]] << 54);

          ulong ibits = ~puzzle[i];
          row[c2r[i]] &= ibits;
          col[c2c[i]] &= ibits;
          box[c2b[i]] &= ibits;
        }
      }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static unsafe void SolveFast(Span<ulong> data)
    {
      fixed (int* c2r = cell2row, c2c = cell2col, c2b = cell2box)
      {
        fixed (ulong* puzzle = &data[0], row = &data[ROW_OFFSET], col = &data[COL_OFFSET], box = &data[BOX_OFFSET], b2b = bit2bit)
        {
          int maxIterations = 5;
          do
          {
            for (int i = 0; i < SUDOKU_CELL_COUNT; i++)
            {
              if (Popcnt.X64.PopCount(puzzle[i]) == 7) continue;

              ulong bits = (row[c2r[i]] & col[c2c[i]] & box[c2b[i]]) | puzzle[i];

              bits = b2b[bits & NINE_BITS_MASK]
                | b2b[(bits >> 9) & NINE_BITS_MASK] << 9
                | b2b[(bits >> 18) & NINE_BITS_MASK] << 18
                | b2b[(bits >> 27) & NINE_BITS_MASK] << 27
                | b2b[(bits >> 36) & NINE_BITS_MASK] << 36
                | b2b[(bits >> 45) & NINE_BITS_MASK] << 45
                | b2b[bits >> 54] << 54;

              if (bits > 0)
              {
                puzzle[i] |= bits;

                ulong ibit = ~bits;
                row[c2r[i]] &= ibit;
                col[c2c[i]] &= ibit;
                box[c2b[i]] &= ibit;
              }
            }
          } while (--maxIterations > 0);
        }
      }

      fixed (ulong* puzzle = &data[0])
      {
        for (int i = 0; i < SUDOKU_CELL_COUNT; i++)
        {
          if (Popcnt.X64.PopCount(puzzle[i]) < 7)
          {
            if ((puzzle[i] & NINE_BITS_MASK) == 0) SimpleSolve(data, 0);
            if (((puzzle[i] >> 9) & NINE_BITS_MASK) == 0) SimpleSolve(data, 9);
            if (((puzzle[i] >> 18) & NINE_BITS_MASK) == 0) SimpleSolve(data, 18);
            if (((puzzle[i] >> 27) & NINE_BITS_MASK) == 0) SimpleSolve(data, 27);
            if (((puzzle[i] >> 36) & NINE_BITS_MASK) == 0) SimpleSolve(data, 36);
            if (((puzzle[i] >> 45) & NINE_BITS_MASK) == 0) SimpleSolve(data, 45);
            if ((puzzle[i] >> 54) == 0) SimpleSolve(data, 54);
          }
        }
      }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static unsafe void CheckSolutions(Span<byte> sudokus, Span<ulong> data)
    {
      fixed (byte* p_sudokus = sudokus)
      fixed (ulong* puzzle = data, n2b = num2bit)
      {
        int offset0 = SUDOKU_CELL_COUNT + 1,
          offset1 = offset0 + BYTES_PER_SUDOKU,
          offset2 = offset1 + BYTES_PER_SUDOKU,
          offset3 = offset2 + BYTES_PER_SUDOKU,
          offset4 = offset3 + BYTES_PER_SUDOKU,
          offset5 = offset4 + BYTES_PER_SUDOKU,
          offset6 = offset5 + BYTES_PER_SUDOKU;

        // setup puzzles
        for (int i = 0; i < SUDOKU_CELL_COUNT; i++)
        {
          var solution = n2b[sudokus[offset0 + i]]
            | (n2b[sudokus[offset1 + i]] << 9)
            | (n2b[sudokus[offset2 + i]] << 18)
            | (n2b[sudokus[offset3 + i]] << 27)
            | (n2b[sudokus[offset4 + i]] << 36)
            | (n2b[sudokus[offset5 + i]] << 45)
            | (n2b[sudokus[offset6 + i]] << 54);

          if (puzzle[i] != solution)
          {
            ++FailedCount;
            break;
          }
        }
      }
    }

    static void SimpleSolve(Span<byte> sudoku)
    {
      int[] puzzle = new int[SUDOKU_CELL_COUNT];
      int[,] remain = new int[3, 9];

      for (int i = 0; i < 3; i++)
        for (int j = 0; j < 9; j++)
          remain[i, j] = (int)NINE_BITS_MASK;

      for (int i = 0; i < SUDOKU_CELL_COUNT; ++i)
        if (sudoku[i] != '0')
          SetNumber(i, (int)num2bit[sudoku[i]], puzzle, remain);

      SimpleSolveRecursive(ref puzzle, remain);

      for (int j = 0; j < SUDOKU_CELL_COUNT; j++)
        if (puzzle[j] != (int)num2bit[sudoku[j + SUDOKU_CELL_COUNT + 1]])
        {
          ++FailedCount;
          break;
        }
    }

    static void SimpleSolve(Span<ulong> data, int shift)
    {
      int[] puzzle = new int[SUDOKU_CELL_COUNT];
      int[,] remain = new int[3, 9];
      for (int i = 0; i < SUDOKU_CELL_COUNT; i++)
        puzzle[i] = (int)((data[i] >> shift) & NINE_BITS_MASK);

      for (int i = 0; i < 9; i++)
      {
        remain[0,i] = (int)((data[i + ROW_OFFSET] >> shift) & NINE_BITS_MASK);
        remain[1,i] = (int)((data[i + COL_OFFSET] >> shift) & NINE_BITS_MASK);
        remain[2,i] = (int)((data[i + BOX_OFFSET] >> shift) & NINE_BITS_MASK);
      }

      SimpleSolveRecursive(ref puzzle, remain);

      for (int i = 0; i < SUDOKU_CELL_COUNT; i++)
        data[i] |= (ulong)puzzle[i] << shift;
    }

    static bool SimpleSolveRecursive(ref int[] puzzle, int[,] remain)
    {
      bool progress = true;
      while (progress)
      {
        progress = false;
        for (int i = 0; i < SUDOKU_CELL_COUNT; ++i)
        {
          if (puzzle[i] == 0)
          {
            int bits = remain[0, i / 9] & remain[1, i % 9] & remain[2, cell2box[i]];
            if (bits == 0)
              return false;
            else if (bit2num[bits] > 0)
            {
              SetNumber(i, bits, puzzle, remain);
              progress = true;
            }
          }
        }
      }

      for (int i = 0; i < SUDOKU_CELL_COUNT; ++i)
      {
        if (puzzle[i] == 0)
        {
          int bits = remain[0, i / 9] & remain[1, i % 9] & remain[2, cell2box[i]];
          if (isTwoBits(bits))
          {
            int[] puzzleCopy = new int[SUDOKU_CELL_COUNT];
            Array.Copy(puzzle, puzzleCopy, SUDOKU_CELL_COUNT);

            int[,] remainCopy = new int[3, 9];
            for (int j = 0; j < 3; j++)
              for (int k = 0; k < 9; k++)
                remainCopy[j, k] = remain[j, k];

            Array.Copy(puzzle, puzzleCopy, SUDOKU_CELL_COUNT);

            int bit1 = 1;
            while ((bit1 & bits) == 0)
              bit1 <<= 1;

            int bit2 = removeBit(bits);

            SetNumber(i, bit1, puzzle, remain);
            if (SimpleSolveRecursive(ref puzzle, remain))
              return true;

            puzzle = puzzleCopy;
            remain = remainCopy;
            SetNumber(i, bit2, puzzle, remain);

            return SimpleSolveRecursive(ref puzzle, remain);
          }
        }
      }
      return true;
    }
    static void SetNumber(int cell, int bit, int[] puzzle, int[,] remain)
    {
      puzzle[cell] = bit;

      int invBit = ~bit;
      remain[0, cell / 9] &= invBit;
      remain[1, cell % 9] &= invBit;
      remain[2, cell2box[cell]] &= invBit;
    }

    static int removeBit(int val)
    {
      return (val & (val - 1));
    }
    static bool isTwoBits(int val)
    {
      return val > 0 && bit2num[removeBit(val)] > 0;
    }

    static string ToBinary(ulong val)
    {
      string binary = "";
      for (int i = 0; i < 63; i++)
      {
        if (i > 0 && i % 9 == 0)
          binary += " ";
        binary += (val & 1);
        val >>= 1;
      }
      return binary;
    }

    static string PrintSudoku(Span<ulong> data)
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
        res += bit2num[data[i] & NINE_BITS_MASK] - '0';
      }
      return res;
    }
  }
}
