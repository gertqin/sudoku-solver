#define TIME 

using System;
using System.Diagnostics;

namespace SudokuSolver
{
  class Program
  {
    const int PARALLEL_COUNT = 7;

    const char SIZE = (char)81;
    const ulong ALL_BITS = (1 << 9) - 1;
    const ulong ALL_BITS_PARALLEL = ((ulong)1 << (9 * 7)) - 1;

    static int[] bit2num = new int[ALL_BITS + 1];
    static ulong[] num2bit = new ulong['9' + 1];

    static ulong[] num2mask = new ulong[ALL_BITS + 1];
    static ulong[] bit2mask = new ulong[ALL_BITS + 1];

    static int[] cell2box = new int[SIZE];

    static int iterationCount = 0;
    static int simpleSolveCount = 0;

    static long solveSetupTime = 0;
    static long fastSolveTime = 0;
    static long simpleSolveTime = 0;
    static long checkSolutionTime = 0;

    static void Main()
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
        cell2box[i] = ((row / 3 * 3) + (col / 3));
      }
    }

    static void SolveFast(string[] sudokus)
    {
#if TIME
      var timer = Stopwatch.StartNew();
#endif

      ulong[] puzzle = new ulong[SIZE];
      ulong[] puzzleMask = new ulong[SIZE];
      ulong[] row = new ulong[9];
      ulong[] col = new ulong[9];
      ulong[] box = new ulong[9];

      for (int i = 0; i < 9; i++)
      {
        row[i] = ALL_BITS_PARALLEL;
        col[i] = ALL_BITS_PARALLEL;
        box[i] = ALL_BITS_PARALLEL;
      }

      // setup puzzles
      for (int i = 0; i < SIZE; i++)
      {
        puzzle[i] = num2bit[sudokus[0][i]]
          | (num2bit[sudokus[1][i]] << 9)
          | (num2bit[sudokus[2][i]] << 18)
          | (num2bit[sudokus[3][i]] << 27)
          | (num2bit[sudokus[4][i]] << 36)
          | (num2bit[sudokus[5][i]] << 45)
          | (num2bit[sudokus[6][i]] << 54);

        puzzleMask[i] = num2mask[sudokus[0][i]]
          | (num2mask[sudokus[1][i]] << 9)
          | (num2mask[sudokus[2][i]] << 18)
          | (num2mask[sudokus[3][i]] << 27)
          | (num2mask[sudokus[4][i]] << 36)
          | (num2mask[sudokus[5][i]] << 45)
          | (num2mask[sudokus[6][i]] << 54);

        ulong ibits = ~puzzle[i];
        row[i / 9] &= ibits;
        col[i % 9] &= ibits;
        box[cell2box[i]] &= ibits;
      }

#if TIME
      timer.Stop();
      solveSetupTime += timer.ElapsedTicks;
      timer.Restart();
#endif

      int maxIterations = 4;
      do
      {
#if TIME
        iterationCount++;
#endif

        for (int i = 0; i < SIZE; i++)
        {
          if (puzzleMask[i] == 0) continue;

          int r = i / 9;
          int c = i % 9;

          ulong bits = row[r] & col[c] & box[cell2box[i]] & puzzleMask[i];

          ulong bitsMask = bit2mask[bits & ALL_BITS]
            | bit2mask[(bits >> 9) & ALL_BITS] << 9
            | bit2mask[(bits >> 18) & ALL_BITS] << 18
            | bit2mask[(bits >> 27) & ALL_BITS] << 27
            | bit2mask[(bits >> 36) & ALL_BITS] << 36
            | bit2mask[(bits >> 45) & ALL_BITS] << 45
            | bit2mask[bits >> 54] << 54;

          bits &= bitsMask;

          if (bits > 0)
          {
            puzzle[i] |= bits;

            ulong ibit = ~bits;
            puzzleMask[i] &= ~bitsMask;
            row[r] &= ibit;
            col[c] &= ibit;
            box[cell2box[i]] &= ibit;
          }
        }
      } while (maxIterations-- > 0);

#if TIME
      timer.Stop();
      fastSolveTime += timer.ElapsedTicks;
      timer.Restart();
#endif

      for (int i = 0; i < SIZE; i++)
      {
        if (puzzleMask[i] > 0)
        {
          if ((puzzle[i] & ALL_BITS) == 0) SimpleSolve(sudokus[0]);
          if (((puzzle[i] >> 9) & ALL_BITS) == 0) SimpleSolve(sudokus[1]);
          if (((puzzle[i] >> 18) & ALL_BITS) == 0) SimpleSolve(sudokus[2]);
          if (((puzzle[i] >> 27) & ALL_BITS) == 0) SimpleSolve(sudokus[3]);
          if (((puzzle[i] >> 36) & ALL_BITS) == 0) SimpleSolve(sudokus[4]);
          if (((puzzle[i] >> 45) & ALL_BITS) == 0) SimpleSolve(sudokus[5]);
          if ((puzzle[i] >> 54) == 0) SimpleSolve(sudokus[6]);

          return;
          // TODO missing check solution
        }
      }

#if TIME
      timer.Stop();
      simpleSolveTime += timer.ElapsedTicks;
      timer.Restart();
#endif

      // check solution
      for (int i = 0; i < SIZE; i++)
      {
        int resIdx = SIZE + 1 + i;
        var res = num2bit[sudokus[0][resIdx]]
          | (num2bit[sudokus[1][resIdx]] << 9)
          | (num2bit[sudokus[2][resIdx]] << 18)
          | (num2bit[sudokus[3][resIdx]] << 27)
          | (num2bit[sudokus[4][resIdx]] << 36)
          | (num2bit[sudokus[5][resIdx]] << 45)
          | (num2bit[sudokus[6][resIdx]] << 54);
        if (puzzle[i] != res)
          throw new Exception("FAIL");
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

      for (int j = 0; j < SIZE; j++)
        puzzle[j] = sudoku[j];

      int[,] remain = new int[3, 9];

      for (int i = 0; i < 3; i++)
        for (int j = 0; j < 9; j++)
          remain[i, j] = (int)ALL_BITS;

      for (int i = 0; i < SIZE; ++i)
        if (puzzle[i] != '0')
          SetNumber(i, (int)num2bit[puzzle[i]], puzzle, remain);

      SimpleSolveRecursive(ref puzzle, remain);

      for (int j = 0; j < SIZE; j++)
        if (puzzle[j] != sudoku[j + SIZE + 1])
          throw new Exception("FAIL");
    }

    static bool SimpleSolveRecursive(ref int[] puzzle, int[,] remain)
    {
      bool progress = true;
      while (progress)
      {
        progress = false;
        for (int i = 0; i < SIZE; ++i)
        {
          if (puzzle[i] == '0')
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
        if (puzzle[i] == '0')
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
      puzzle[cell] = bit2num[bit];

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
  }
}
