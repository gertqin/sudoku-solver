using System;

namespace SudokuSolver
{
  class Program
  {
    const int PARALLEL_COUNT = 7;
    const int SHIFT = 9;
    const int SHIFT_MASK1 = 18;
    const int SHIFT_MASK2 = SHIFT_MASK1*2;
    const int SHIFT_MASK3 = SHIFT_MASK1*3;

    const char SIZE = (char)81;
    const ulong ALL_BITS = (1 << 9) - 1;
    const ulong ALL_BITS_PARALLEL = MASK_1 | MASK_2 | MASK_3 | MASK_4 | MASK_5 | MASK_6 | MASK_7;
    const ulong ALL_BITS_MASK = ALL_BITS & (ALL_BITS << SHIFT);

    static ulong mask1 = new ulong[ALL_BITS_MASK + 1]; 
    static ulong mask2 = new ulong[ALL_BITS_MASK + 1]; 
    static ulong mask3 = new ulong[ALL_BITS_MASK + 1]; 
    static ulong mask4 = new ulong[ALL_BITS + 1]; 


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

      for (int i = 0; i < 9; i++)
      {
        for (int j = 0; j < 9; j++)
        {
          mask1[1 << j] = ALL_BITS;
          mask1[1 << (j + SHIFT)] = ALL_BITS << SHIFT;
          mask1[(1 << j) & (1 << (j + SHIFT))] = ALL_BITS_MASK;

          mask2[1 << j] = ALL_BITS << SHIFT_MASK1;
          mask2[1 << (j + SHIFT)] = ALL_BITS << (SHIFT_MASK1 + SHIFT);
          mask2[(1 << j) & (1 << (j + SHIFT))] = ALL_BITS_MASK << SHIFT_MASK1;

          mask3[1 << j] = ALL_BITS << SHIFT_MASK2;
          mask3[1 << (j + SHIFT)] = ALL_BITS << (SHIFT_MASK2 + SHIFT);
          mask3[(1 << j) & (1 << (j + SHIFT))] = ALL_BITS_MASK << SHIFT_MASK2;

          mask4[1 << j] = ALL_BITS << SHIFT_MASK3;
        }
      }
    }

    static void SolveFast(string[] sudokus)
    {
      ulong[] puzzle = new ulong[SIZE];
      ulong[] puzzleMask = new ulong[SIZE];
      ulong[] row = new ulong[9];
      ulong[] col = new ulong[9];
      ulong[] square = new ulong[9];

      for (int i = 0; i < 9; i++)
      {
        row[i] = ALL_BITS_PARALLEL;
        col[i] = ALL_BITS_PARALLEL;
        square[i] = ALL_BITS_PARALLEL;
      }

      // setup puzzles
      for (int i = 0; i < SIZE; i++)
      {
        puzzleMask[i] = ALL_BITS_PARALLEL;

        for (int j = 0; j < PARALLEL_COUNT; j++)
        {
          ulong val = (ulong)sudokus[j][i] - '0';
          if (val > 0)
          {
            int shift = SHIFT * j;
            ulong bit = val2bit[val] << shift;
            ulong invBit = ~bit;

            puzzle[i] |= bit;

            puzzleMask[i] &= invBit;
            row[i / 9] &= invBit;
            col[i % 9] &= invBit;
            square[cell2square[i]] &= invBit;
          }
        }
      }

      bool progress = true;
      while (progress)
      {
        progress = false;
        for (int i = 0; i < SIZE; ++i)
        {
          ulong bits = row[i / 9] & col[i % 9] & square[cell2square[i]];

          bits &= puzzleMask[i];
          bits &= mask1[bits & ALL_BITS_MASK];
          bits &= mask2[(bits >> SHIFT_MASK1) & ALL_BITS_MASK];
          bits &= mask3[(bits >> SHIFT_MASK2) & ALL_BITS_MASK];
          bits &= mask4[bits >> SHIFT_MASK3];

          if (bits > 0)
          {
            puzzle[i] |= bits;

            ulong invBit = ~bits;
            puzzleMask[i] &= invBit;
            row[i / 9] &= invBit;
            col[i % 9] &= invBit;
            square[cell2square[i]] &= invBit;

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

    static void SimpleSolve(string sudoku)
    {
      int[] puzzle = new int[SIZE];
      int[,] remain = new int[3, 9];

      for (int j = 0; j < SIZE; j++)
        puzzle[j] = sudoku[j];

      for (int i = 0; i < 3; i++)
        for (int j = 0; j < 9; j++)
          remain[i,j] = (int)ALL_BITS;

      for (int i = 0; i < SIZE; ++i)
        if (puzzle[i] != '0')
          setNumber(i, num2bit[puzzle[i]], puzzle, remain);

      SimpleSolveRecursive(puzzle, remain);

      for (int j = 0; j < SIZE; j++)
        if (puzzle[j] != sudoku[j + SIZE + 1])
          throw new Exception("FAIL");
    }

    static bool SimpleSolveRecursive(int[] puzzle, int[,] remain)
    {
      bool progress = true; 
      while (progress && missing > 0)
      {
        progress = false;
        for (int i = 0; i < SIZE; ++i)
        {
          if (puzzle[i] == '0')
          {
            int bits = remain[0, i / 9] & remain[1, i % 9] & remain[2, cell2square[i]];
            if (bits == 0)
              return false;
            else if (bit2num[bits] > 0)
            {
              setNumber(i, bits, puzzle, rowRemain, colRemain, squareRemain);
              progress = true;
            }
            else if (isTwoBits(bits))
            {
              int[] puzzleCopy = new int[SIZE];
              Array.Copy(puzzle, puzzleCopy, SIZE);

              int[,] remainCopy = new int[3,9];
              for (int i = 0; i < 3; i++)
                for (int j = 0; j < 9; j++)
                  remainCopy[i,j] = remain[i,j];

              Array.Copy(puzzle, puzzleCopy, SIZE);

              int bit1 = 1;
              while ((bit1 & bits) == 0)
                bit1 <<= 1;

              int bit2 = removeBit(bits);

              setNumber(i, bit1, puzzle, remain);
              bool solved = SimpleSolveRecursive(puzzle, remain);

              if (solved) return true;

              puzzle = puzzleCopy;
              remain = remainCopy;
              setNumber(i, bit2, puzzle, remain);
            }
          }
        }
      }
      return true;
    }
    static void setNumber(int cell, int bit, int[] puzzle, int[,] remain)
    {
      puzzle[cell] = bit2num[bit];

      int invBit = ~bit;
      rowRemain[cell / 9] &= invBit;
      colRemain[cell % 9] &= invBit;
      squareRemain[cell2square[cell]] &= invBit;
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
