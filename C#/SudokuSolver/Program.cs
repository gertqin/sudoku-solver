#define CHECK_SOLUTIONS
//#define RUN_MULTIPLE_LOOPS

#define AVX_SOLVER

using System;
using System.Diagnostics;
using System.IO;

namespace SudokuSolver
{
  class Program
  {
    const int MULTIPLE_RUN_COUNT = 10;

    static void Main()
    {
      long readInputMs = 0;
      var timer = Stopwatch.StartNew();

      var bytes = File.ReadAllBytes("../../../../../sudoku.csv");
      int sudokuCount = 1000000;

      timer.Stop();
      readInputMs = timer.ElapsedMilliseconds;
      timer.Restart();

      bool checkSolutions = false;
#if CHECK_SOLUTIONS
      checkSolutions = true;
#endif

#if AVX_SOLVER
      SolverAvx2.GlobalSetup();
#else
      SolverX64.GlobalSetup();
#endif

#if RUN_MULTIPLE_LOOPS
      for (int i = 0; i < MULTIPLE_RUN_COUNT; i++)
#endif
      {
#if AVX_SOLVER
      SolverAvx2.Run(bytes, checkSolutions);
#else
      SolverX64.Run(bytes, checkSolutions);
#endif
      }

      timer.Stop();

#if AVX_SOLVER
      int failed = SolverAvx2.FailedCount;
#else
      int failed = SolverX64.FailedCount;
#endif

      Console.WriteLine($"Time to read input: {readInputMs}ms");
      Console.WriteLine($"Time to solve {sudokuCount.ToString("N0")} sudokus: {timer.ElapsedMilliseconds}ms");
      Console.WriteLine($"Failed sudokus: {failed}");
    }
  }
}
