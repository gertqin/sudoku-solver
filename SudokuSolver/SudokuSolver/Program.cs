using System;

namespace SudokuSolver
{
  class Program
  {
    const int PARALLEL_COUNT = 7;
    const int SHIFT = 9;

    const char SIZE = (char)81;
    const ulong ALL_BITS = (1 << 9) - 1;
    const ulong ALL_BITS_PARALLEL = ((ulong)1 << (9 * 7)) - 1;

    static int[] bit2num = new int[ALL_BITS + 1];
    static int[] num2bit = new int['9' + 1];

    static ulong[] bit2mask = new ulong[ALL_BITS + 1];
    static ulong[] val2bit = new ulong[10] { 0, 1, 2, 4, 8, 16, 32, 64, 128, 256 };

    static int[] cell2box = new int[SIZE];

    static int iterationCount = 0;
    static int simpleSolveCount = 0;

    static void Main()
    {
      string[] sudokus = System.IO.File.ReadAllLines("C:/projects-fun/sudoku-solver/sudoku.csv");

      var timer = System.Diagnostics.Stopwatch.StartNew();

      PrepareAndSolve(sudokus);

      timer.Stop();

      Console.WriteLine($"Milliseconds to solve 1,000,000 sudokus: {timer.ElapsedMilliseconds}");
      Console.WriteLine($"Iteration count: {iterationCount}");
      Console.WriteLine($"SimpleSolve count: {simpleSolveCount}");
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
      for (int i = 0; i <= '0'; i++)
        num2bit[i] = 0;
      for (int i = '1'; i <= '9'; i++)
        num2bit[i] = (1 << (i - '1'));

      for (int i = 0; i < SIZE; i++)
      {
        int row = i / 9;
        int col = i % 9;
        cell2box[i] = ((row / 3 * 3) + (col / 3));
      }
    }

    static void SolveFast(string[] sudokus)
    {
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
      for (int i = 0; i < SIZE; i++)
      {
        puzzleMask[i] = ALL_BITS_PARALLEL;
      }

      // setup puzzles
      {
        int shift = 0;
        for (int j = 0; j < PARALLEL_COUNT; j++)
        {
          for (int i = 0; i < SIZE; i++)
          {
            ulong val = (ulong)sudokus[j][i] - '0';
            if (val > 0)
            {
              ulong bit = val2bit[val] << shift;
              ulong invBit = ~bit;

              puzzle[i] |= bit;
              puzzleMask[i] &= ~(bit2mask[val2bit[val]] << shift);

              row[i / 9] &= invBit;
              col[i % 9] &= invBit;
              box[cell2box[i]] &= invBit;
            }
          }
          shift += SHIFT;
        }
      }

      bool progress = true;
      while (progress)
      {
        iterationCount++;
        progress = false;

        for (int i = 0; i < SIZE; i++)
        {
          if (puzzleMask[i] == 0) continue;

          int r = i / 9;
          int c = i % 9;

          ulong bits = row[r] & col[c] & box[cell2box[i]] & puzzleMask[i];
          ulong bitsMask = 0;
          ulong bitsLeft = bits;
          int shift = 0;

          while ((bitsLeft & ALL_BITS) == 0)
          {
            shift += 9;
            bitsLeft >>= 9;
          }
          while (bitsLeft > 0)
          {
            bitsMask |= (bit2mask[bitsLeft & ALL_BITS]) << shift;
            shift += 9;
            bitsLeft >>= 9;
          }
          bits &= bitsMask;

          if (bits > 0)
          {
            puzzle[i] |= bits;

            ulong invBit = ~bits;
            puzzleMask[i] &= ~bitsMask;
            row[r] &= invBit;
            col[c] &= invBit;
            box[cell2box[i]] &= invBit;

            progress = true;
          }
        }
      }

      // check solution
      {
        int shift = 0;
        for (int p = 0; p < PARALLEL_COUNT; p++)
        {
          for (int i = 0; i < SIZE; i++)
          {
            ulong val = ((puzzle[i] >> shift) & ALL_BITS);

            if (val == 0)
            {
              SimpleSolve(sudokus[p]);
              break;
            }

            if (bit2num[val] != sudokus[p][i + SIZE + 1])
              throw new Exception("FAIL");
          }
          shift += SHIFT;
        }
      }
    }

    static void SimpleSolve(string sudoku)
    {
      simpleSolveCount++;
      int[] puzzle = new int[SIZE];

      for (int j = 0; j < SIZE; j++)
        puzzle[j] = sudoku[j];

      int[,] remain = new int[3, 9];

      for (int i = 0; i < 3; i++)
        for (int j = 0; j < 9; j++)
          remain[i, j] = (int)ALL_BITS;

      for (int i = 0; i < SIZE; ++i)
        if (puzzle[i] != '0')
          SetNumber(i, num2bit[puzzle[i]], puzzle, remain);

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
