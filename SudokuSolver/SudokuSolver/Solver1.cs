#define TIME 

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace SudokuSolver
{
  class Solver1
  {
    const int PARALLEL_COUNT = 7;
    const int PUZZLE_MASK_OFFSET = 81;
    const int ROW_OFFSET = PUZZLE_MASK_OFFSET + 81;
    const int COL_OFFSET = ROW_OFFSET + 9;
    const int BOX_OFFSET = COL_OFFSET + 9;

    const char SIZE = (char)81;
    const ulong ALL_BITS = (1 << 9) - 1;
    const ulong ALL_BITS_PARALLEL = ((ulong)1 << (9 * 7)) - 1;

    static int[] bit2num = new int[ALL_BITS + 1];
    static ulong[] num2bit = new ulong['9' + 1];

    static ulong[] num2mask = new ulong['9' + 1];
    static ulong[] bit2mask = new ulong[ALL_BITS + 1];

    static int[] cell2row = new int[SIZE];
    static int[] cell2col = new int[SIZE];
    static int[] cell2box = new int[SIZE];

    static int iterationCount = 0;
    static int simpleSolveCount = 0;

    static long solveSetupTime = 0;
    static long fastSolveTime = 0;
    static long simpleSolveTime = 0;
    static long checkSolutionTime = 0;


    public static void Solve()
    {
      string[] sudokus = System.IO.File.ReadAllLines("../../../../../sudoku.csv");

      var timer = Stopwatch.StartNew();

      PrepareAndSolve(sudokus);

      timer.Stop();

      Console.WriteLine($"Milliseconds to solve 1,000,000 sudokus: {timer.ElapsedMilliseconds}");
#if TIME
      Console.WriteLine($"Setup time: {solveSetupTime / 10000}");
      Console.WriteLine($"Parallel solve time: {fastSolveTime / 10000} // Iterations: {iterationCount}");
      Console.WriteLine($"Simple solve time: {simpleSolveTime / 10000} // Simple solve count: {simpleSolveCount}");
      Console.WriteLine($"Check solution time: {checkSolutionTime / 10000}");
#endif
    }

    static void PrepareAndSolve(string[] sudokus)
    {
      Prepare();

      var length = sudokus.Length - (sudokus.Length % PARALLEL_COUNT);
      for (int i = 0; i < length; i++)
      {
        string[] subSudokus = new string[PARALLEL_COUNT];
        for (int j = 0; j < PARALLEL_COUNT; j++)
          subSudokus[j] = sudokus[i + j];

        SolveFast(subSudokus);
        i += PARALLEL_COUNT - 1;
      }

      for (int i = length; i < sudokus.Length; i++)
      {
        SimpleSolve(sudokus[i]);
      }

      ////System.Threading.Tasks.Parallel.ForEach(sudokus, Solve);
    }

    static void Prepare()
    {
      bit2num[0] = '0';
      for (int i = 1; i <= (int)ALL_BITS; ++i)
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

          bit2mask[i] = ALL_BITS;
        }
      }

      num2mask['0'] = ALL_BITS;
      for (int i = '1'; i <= '9'; i++)
      {
        num2bit[i] = (ulong)(1 << (i - '1'));
      }

      for (int i = 0; i < SIZE; i++)
      {
        int row = i / 9;
        int col = i % 9;
        cell2row[i] = row;
        cell2col[i] = col;
        cell2box[i] = ((row / 3 * 3) + (col / 3));
      }
    }

    static void SolveFast(string[] sudokus)
    {
#if TIME
      var timer = Stopwatch.StartNew();
#endif
      Span<ulong> data = stackalloc ulong[BOX_OFFSET + 9];

      unsafe
      {
        fixed (int* c2r = cell2row, c2c = cell2col, c2b = cell2box)
        {
          fixed (ulong* puzzle = &data[0], puzzleMask = &data[PUZZLE_MASK_OFFSET], row = &data[ROW_OFFSET], col = &data[COL_OFFSET], box = &data[BOX_OFFSET], n2b = num2bit, n2m = num2mask)
          {
            for (int i = 0; i < 9; i++)
            {
              row[i] = ALL_BITS_PARALLEL;
              col[i] = ALL_BITS_PARALLEL;
              box[i] = ALL_BITS_PARALLEL;
            }

            // setup puzzles
            for (int i = 0; i < SIZE; i++)
            {
              puzzle[i] = n2b[sudokus[0][i]]
                | (n2b[sudokus[1][i]] << 9)
                | (n2b[sudokus[2][i]] << 18)
                | (n2b[sudokus[3][i]] << 27)
                | (n2b[sudokus[4][i]] << 36)
                | (n2b[sudokus[5][i]] << 45)
                | (n2b[sudokus[6][i]] << 54);

              puzzleMask[i] = n2m[sudokus[0][i]]
                | (n2m[sudokus[1][i]] << 9)
                | (n2m[sudokus[2][i]] << 18)
                | (n2m[sudokus[3][i]] << 27)
                | (n2m[sudokus[4][i]] << 36)
                | (n2m[sudokus[5][i]] << 45)
                | (n2m[sudokus[6][i]] << 54);

              ulong ibits = ~puzzle[i];
              row[c2r[i]] &= ibits;
              col[c2c[i]] &= ibits;
              box[c2b[i]] &= ibits;
            }
          }
        }
      }

#if TIME
      timer.Stop();
      solveSetupTime += timer.ElapsedTicks;
      timer.Restart();
#endif

      unsafe
      {
        fixed (int* c2r = cell2row, c2c = cell2col, c2b = cell2box)
        {
          fixed (ulong* puzzle = &data[0], puzzleMask = &data[PUZZLE_MASK_OFFSET], row = &data[ROW_OFFSET], col = &data[COL_OFFSET], box = &data[BOX_OFFSET], b2m = bit2mask)
          {
            int maxIterations = 4;
            do
            {
#if TIME
              iterationCount++;
#endif
              for (int i = 0; i < SIZE; i++)
              {
                if (puzzleMask[i] == 0) continue;

                ulong bits = row[c2r[i]] & col[c2c[i]] & box[c2b[i]] & puzzleMask[i];

                // val & (val-1)
                ulong bitsMask = b2m[bits & ALL_BITS]
                  | b2m[(bits >> 9) & ALL_BITS] << 9
                  | b2m[(bits >> 18) & ALL_BITS] << 18
                  | b2m[(bits >> 27) & ALL_BITS] << 27
                  | b2m[(bits >> 36) & ALL_BITS] << 36
                  | b2m[(bits >> 45) & ALL_BITS] << 45
                  | b2m[bits >> 54] << 54;

                bits &= bitsMask;

                if (bits > 0)
                {
                  puzzle[i] |= bits;

                  ulong ibit = ~bits;
                  puzzleMask[i] &= ~bitsMask;
                  row[c2r[i]] &= ibit;
                  col[c2c[i]] &= ibit;
                  box[c2b[i]] &= ibit;
                }
              }
            } while (maxIterations-- > 0);
          }
        }
      }

#if TIME
      timer.Stop();
      fastSolveTime += timer.ElapsedTicks;
      timer.Restart();
#endif

      unsafe
      {
        fixed (ulong* puzzle = &data[0], puzzleMask = &data[PUZZLE_MASK_OFFSET])
        {
          for (int i = 0; i < SIZE; i++)
          {
            if (puzzleMask[i] > 0)
            {
              if ((puzzle[i] & ALL_BITS) == 0) SimpleSolve(data, 0);
              if (((puzzle[i] >> 9) & ALL_BITS) == 0) SimpleSolve(data, 9);
              if (((puzzle[i] >> 18) & ALL_BITS) == 0) SimpleSolve(data, 18);
              if (((puzzle[i] >> 27) & ALL_BITS) == 0) SimpleSolve(data, 27);
              if (((puzzle[i] >> 36) & ALL_BITS) == 0) SimpleSolve(data, 36);
              if (((puzzle[i] >> 45) & ALL_BITS) == 0) SimpleSolve(data, 45);
              if ((puzzle[i] >> 54) == 0) SimpleSolve(data, 54);
            }
          }
        }
      }

#if TIME
      timer.Stop();
      simpleSolveTime += timer.ElapsedTicks;
      timer.Restart();
#endif

      unsafe
      {
        fixed (ulong* puzzle = &data[0], n2b = num2bit)
        {
          // check solution
          for (int i = 0; i < SIZE; i++)
          {
            int resIdx = SIZE + 1 + i;
            var res = n2b[sudokus[0][resIdx]]
              | (n2b[sudokus[1][resIdx]] << 9)
              | (n2b[sudokus[2][resIdx]] << 18)
              | (n2b[sudokus[3][resIdx]] << 27)
              | (n2b[sudokus[4][resIdx]] << 36)
              | (n2b[sudokus[5][resIdx]] << 45)
              | (n2b[sudokus[6][resIdx]] << 54);
            if (puzzle[i] != res)
              throw new Exception("FAIL");
          }
        }
      }

#if TIME
      timer.Stop();
      checkSolutionTime += timer.ElapsedTicks;
#endif
    }

    static void SimpleSolve(string sudoku)
    {
#if TIME
      simpleSolveCount++;
#endif

      int[] puzzle = new int[SIZE];
      int[,] remain = new int[3, 9];

      for (int i = 0; i < 3; i++)
        for (int j = 0; j < 9; j++)
          remain[i, j] = (int)ALL_BITS;

      for (int i = 0; i < SIZE; ++i)
        if (sudoku[i] != '0')
          SetNumber(i, (int)num2bit[sudoku[i]], puzzle, remain);

      SimpleSolveRecursive(ref puzzle, remain);

      for (int j = 0; j < SIZE; j++)
        if (puzzle[j] != (int)num2bit[sudoku[j + SIZE + 1]])
          throw new Exception("FAIL");
    }

    static void SimpleSolve(Span<ulong> data, int shift)
    {
#if TIME
      simpleSolveCount++;
#endif

      int[] puzzle = new int[SIZE];
      int[,] remain = new int[3, 9];
      for (int i = 0; i < SIZE; i++)
        puzzle[i] = (int)((data[i] >> shift) & ALL_BITS);

      for (int i = 0; i < 9; i++)
      {
        remain[0,i] = (int)((data[i + ROW_OFFSET] >> shift) & ALL_BITS);
        remain[1,i] = (int)((data[i + COL_OFFSET] >> shift) & ALL_BITS);
        remain[2,i] = (int)((data[i + BOX_OFFSET] >> shift) & ALL_BITS);
      }

      SimpleSolveRecursive(ref puzzle, remain);

      for (int i = 0; i < SIZE; i++)
        data[i] |= (ulong)puzzle[i] << shift;
    }

    static bool SimpleSolveRecursive(ref int[] puzzle, int[,] remain)
    {
      bool progress = true;
      while (progress)
      {
        progress = false;
        for (int i = 0; i < SIZE; ++i)
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

      for (int i = 0; i < SIZE; ++i)
      {
        if (puzzle[i] == 0)
        {
          int bits = remain[0, i / 9] & remain[1, i % 9] & remain[2, cell2box[i]];
          if (isTwoBits(bits))
          {
            int[] puzzleCopy = new int[SIZE];
            Array.Copy(puzzle, puzzleCopy, SIZE);

            int[,] remainCopy = new int[3, 9];
            for (int j = 0; j < 3; j++)
              for (int k = 0; k < 9; k++)
                remainCopy[j, k] = remain[j, k];

            Array.Copy(puzzle, puzzleCopy, SIZE);

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
      for (int i = 0; i < SIZE; i++)
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
        res += bit2num[data[i] & ALL_BITS] - '0';
      }
      return res;
    }
  }
}
