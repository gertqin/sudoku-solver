using System;

namespace SudokuSolver
{
  class Program
  {
    const int PARALLEL_COUNT = 7;
    const int SHIFT = 9;
    const char SIZE = (char)81;
    const ulong ALL_BITS = (1 << 9) - 1;
    const ulong MASK_1 = ALL_BITS;
    const ulong MASK_2 = ALL_BITS << SHIFT;
    const ulong MASK_3 = ALL_BITS << (SHIFT * 2);
    const ulong MASK_4 = ALL_BITS << (SHIFT * 3);
    const ulong MASK_5 = ALL_BITS << (SHIFT * 4);
    const ulong MASK_6 = ALL_BITS << (SHIFT * 5);
    const ulong MASK_7 = ALL_BITS << (SHIFT * 6);
    const ulong IMASK_1 = ~MASK_1;
    const ulong IMASK_2 = ~MASK_2;
    const ulong IMASK_3 = ~MASK_3;
    const ulong IMASK_4 = ~MASK_4;
    const ulong IMASK_5 = ~MASK_5;
    const ulong IMASK_6 = ~MASK_6;
    const ulong IMASK_7 = ~MASK_7;
    const ulong ALL_BITS_PARALLEL = MASK_1 | MASK_2 | MASK_3 | MASK_4 | MASK_5 | MASK_6 | MASK_7;


    static int[] bit2num = new int[ALL_BITS + 1];
    static int[] num2bit = new int['9' + 1];
    static ulong[] val2bit = new ulong[10] { 0, 1, 2, 4, 8, 16, 32, 64, 128, 256 };

    static int[] cell2square = new int[SIZE];

    static int missing = 0;

    static void Main()
    {
      string[] sudokus = System.IO.File.ReadAllLines("C:/projects-fun/sudoku-solver/sudoku.csv");

      var timer = System.Diagnostics.Stopwatch.StartNew();

      PrepareAndSolve(sudokus);

      timer.Stop();

      Console.WriteLine($"Milliseconds to solve 1,000,000 sudokus: {timer.ElapsedMilliseconds}");
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
        Solve(sudokus[i]);
      }

      ////System.Threading.Tasks.Parallel.ForEach(sudokus, Solve);
    }

    static void Prepare()
    {
      bit2num[0] = 0;
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
        cell2square[i] = ((row / 3 * 3) + (col / 3));
      }
    }

    static void SolveFast(string[] sudokus)
    {
      ulong[] puzzle = new ulong[SIZE];
      ulong[] rowRemain = new ulong[9];
      ulong[] colRemain = new ulong[9];
      ulong[] squareRemain = new ulong[9];

      for (int i = 0; i < 9; i++)
      {
        rowRemain[i] = ALL_BITS_PARALLEL;
        colRemain[i] = ALL_BITS_PARALLEL;
        squareRemain[i] = ALL_BITS_PARALLEL;
      }

      // setup puzzles
      for (int i = 0; i < SIZE; i++)
      {
        for (int j = 0; j < PARALLEL_COUNT; j++)
        {
          ulong val = (ulong)sudokus[j][i] - '0';
          if (val > 0)
          {
            int shift = SHIFT * j;
            ulong bit = val2bit[val];
            puzzle[i] |= (bit << shift); // TODO val or bit?

            ulong invBit = ~(val2bit[val] << shift);
            rowRemain[i / 9] &= invBit;
            colRemain[i % 9] &= invBit;
            squareRemain[cell2square[i]] &= invBit;
          }
        }
      }

      bool progress = true;
      while (progress)
      {
        progress = false;
        for (int i = 0; i < SIZE; ++i)
        {
          ulong bits = rowRemain[i / 9] & colRemain[i % 9] & squareRemain[cell2square[i]];

          if ((puzzle[i] & MASK_1) > 0 || (((bits & MASK_1) & ((bits & MASK_1) - 1)) > 0)) bits &= IMASK_1;
          if ((puzzle[i] & MASK_2) > 0 || ((bits & MASK_2) & ((bits & MASK_2) - 1)) > 0) bits &= IMASK_2;
          if ((puzzle[i] & MASK_3) > 0 || ((bits & MASK_3) & ((bits & MASK_3) - 1)) > 0) bits &= IMASK_3;
          if ((puzzle[i] & MASK_4) > 0 || ((bits & MASK_4) & ((bits & MASK_4) - 1)) > 0) bits &= IMASK_4;
          if ((puzzle[i] & MASK_5) > 0 || ((bits & MASK_5) & ((bits & MASK_5) - 1)) > 0) bits &= IMASK_5;
          if ((puzzle[i] & MASK_6) > 0 || ((bits & MASK_6) & ((bits & MASK_6) - 1)) > 0) bits &= IMASK_6;
          if ((puzzle[i] & MASK_7) > 0 || ((bits & MASK_7) & ((bits & MASK_7) - 1)) > 0) bits &= IMASK_7;

          if (bits > 0)
          {
            puzzle[i] |= bits;

            ulong invBit = ~bits;
            rowRemain[i / 9] &= invBit;
            colRemain[i % 9] &= invBit;
            squareRemain[cell2square[i]] &= invBit;

            progress = true;
          }
        }
      }

      for (int j = 0; j < PARALLEL_COUNT; j++)
      {
        for (int i = 0; i < SIZE; i++)
        {
          int shift = j * SHIFT;
          ulong val = ((puzzle[i] >> shift) & ALL_BITS);

          if (val == 0)
          {
            Solve(sudokus[j]);
            break;
          }

          ulong res = (ulong)sudokus[j][i + SIZE + 1] - '0';
          if (val != val2bit[res])
          {
            throw new Exception("FAIL");
          }
        }
      }
    }

    static void Solve(string sudoku)
    {
      int[] puzzle = new int[SIZE];

      for (int j = 0; j < SIZE; j++)
      {
        puzzle[j] = sudoku[j];
      }

      int[] rowRemain = new int[9];
      int[] colRemain = new int[9];
      int[] squareRemain = new int[9];

      for (int i = 0; i < 9; i++)
      {
        rowRemain[i] = (int)ALL_BITS;
        colRemain[i] = (int)ALL_BITS;
        squareRemain[i] = (int)ALL_BITS;
      }

      missing = SIZE;

      for (int i = 0; i < SIZE; ++i)
        if (puzzle[i] != '0')
          setNumber(i, num2bit[puzzle[i]], puzzle, rowRemain, colRemain, squareRemain);

      runAlgo(puzzle, rowRemain, colRemain, squareRemain);

      for (int j = 0; j < SIZE; j++)
      {
        if (puzzle[j] != sudoku[j + SIZE + 1])
          throw new Exception("FAIL");
      }
    }
    
    static void runAlgo(int[] puzzle, int[] rowRemain, int[] colRemain, int[] squareRemain)
    {
      bool progress = true; 
      while (progress && missing > 0)
      {
        progress = false;
        for (int i = 0; i < SIZE; ++i)
        {
          if (puzzle[i] == '0')
          {
            int bit = (rowRemain[i / 9] & colRemain[i % 9] & squareRemain[cell2square[i]]);
            if (bit2num[bit] > 0)
            {
              setNumber(i, bit, puzzle, rowRemain, colRemain, squareRemain);
              progress = true;
            }
          }
        }
      }

      if (missing > 0)
      {
        guess(puzzle, rowRemain, colRemain, squareRemain);
      }
    }
    
    static void guess(int[] puzzle, int[] rowRemain, int[] colRemain, int[] squareRemain)
    {
      int guessCell = SIZE;
      int guessBit = 0;
      for (int i = 0; i < SIZE; ++i)
      {
        if (puzzle[i] == '0')
        {
          int bit = (rowRemain[i / 9] & colRemain[i % 9] & squareRemain[cell2square[i]]);
          if (bit == 0)
            return;
          else if (isTwoBits(bit) && guessBit == 0)
          {
            guessCell = i;
            guessBit = bit;
          }
        }
      }
      if (guessCell == SIZE)
        return;

      int[] rowCopy = new int[9];
      int[] colCopy = new int[9];
      int[] squareCopy = new int[9];
      int[] puzzleCopy = new int[SIZE];

      Array.Copy(rowRemain, rowCopy, 9);
      Array.Copy(colRemain, colCopy, 9);
      Array.Copy(squareRemain, squareCopy, 9);
      Array.Copy(puzzle, puzzleCopy, SIZE);

      int missingCopy = missing;

      int bit1 = 1;
      while ((bit1 & guessBit) == 0)
        bit1 <<= 1;

      int bit2 = removeBit(guessBit);

      setNumber(guessCell, bit1, puzzle, rowRemain, colRemain, squareRemain);
      runAlgo(puzzle, rowRemain, colRemain, squareRemain);

      if (missing > 0)
      {
        missing = missingCopy;
        Array.Copy(rowCopy, rowRemain, 9);
        Array.Copy(colCopy, colRemain, 9);
        Array.Copy(squareCopy, squareRemain, 9);
        Array.Copy(puzzleCopy, puzzle, SIZE);

        setNumber(guessCell, bit2, puzzle, rowRemain, colRemain, squareRemain);
        runAlgo(puzzle, rowRemain, colRemain, squareRemain);
      }
    }

    static void setNumber(int cell, int bit, int[] puzzle, int[] rowRemain, int[] colRemain, int[] squareRemain)
    {
      puzzle[cell] = bit2num[bit];

      int invBit = ~bit;
      rowRemain[cell / 9] &= invBit;
      colRemain[cell % 9] &= invBit;
      squareRemain[cell2square[cell]] &= invBit;

      missing--;
    }
    static int removeBit(int val)
    {
      return (val & (val - 1));
    }
    static bool isTwoBits(int val)
    {
      return val > 0 && bit2num[removeBit(val)] > 0;
    }
  }
}
