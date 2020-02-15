#include <iostream>
#include <fstream>
#include "stdio.h"
#include "time.h"

using namespace std;

#define DEBUG 1

#define SIZE 81
#define ABS(a, b) ((a) > (b) ? (a) - (b) : (b) - (a))

const short ALL_BITS = (1 << 9) - 1;

bool oneBits[ALL_BITS + 1];

char updateCells1[SIZE][20]; // row: 8, col: 8, square remain: 4
char puzzle[SIZE];
char solution[SIZE];
short possibilities[SIZE];
char missing = 0;

#ifdef DEBUG
double timeReadInput = 0;
double timeSetNumber = 0;
double timeSolve = 0;
#endif

inline short removeBit(short val)
{
  return val & (val - 1);
}
inline bool isTwoBits(short val)
{
  return val && oneBits[removeBit(val)];
}
inline short numToBit(char num)
{
  return 1 << (num - 1);
}
char bitToNum(short bit)
{
  char num = 1;
  while (bit >>= 1)
  {
    num++;
  }
  return num;
}

void setup()
{
  oneBits[0] = false;
  for (short i = 1; i <= ALL_BITS; ++i)
    oneBits[i] = !removeBit(i);

  char rows[9][9];
  char cols[9][9];
  char squares[9][9];

  for (int i = 0; i < 9; ++i)
    for (int j = 0; j < 9; ++j)
    {
      rows[i][j] = i * 9 + j;
      cols[i][j] = j * 9 + i;
    }

  for (int i = 0; i < 9; ++i)
  {
    char row = i / 3 * 3;
    char col = i % 3 * 3;

    for (int j = 0; j < 3; ++j)
      for (int k = 0; k < 3; ++k)
        squares[i][j * 3 + k] = row * 9 + col + j * 9 + k;
  }

  for (int i = 0; i < SIZE; ++i)
  {
    char row = i / 9;
    char col = i % 9;
    char square = row / 3 * 3 + col / 3;

    int idx = 0;
    for (int j = 0; j < 9; ++j)
      if (rows[row][j] != i)
        updateCells1[i][idx++] = rows[row][j];

    for (int j = 0; j < 9; ++j)
      if (cols[col][j] != i)
        updateCells1[i][idx++] = cols[col][j];

    for (int j = 0; j < 9; ++j)
    {
      char squareVal = squares[square][j];
      if (squareVal % 9 != i % 9 && ABS(squareVal, i) > 2)
        updateCells1[i][idx++] = squareVal;
    }
  }
}

inline char setNumber(int i)
{
#ifdef DEBUG
  clock_t start = clock();
#endif

  puzzle[i] = bitToNum(possibilities[i]) + '0';

  for (int j = 0; j < 20; ++j)
  {
    possibilities[updateCells1[i][j]] &= ~possibilities[i];
  }

  possibilities[i] = 0;
  missing--;

#ifdef DEBUG
  timeSetNumber += ((double)(clock() - start) / CLOCKS_PER_SEC);
#endif
}

void runAlgoFromGuess();

void guess()
{
  short possibClone[SIZE];
  char puzzleClone[SIZE];
  char missingClone = missing;

  for (int i = 0; i < SIZE; ++i)
  {
    possibClone[i] = possibilities[i];
    puzzleClone[i] = puzzle[i];
  }

  char guessCell = 0;
  for (; guessCell < SIZE; guessCell++)
  {
    if (isTwoBits(possibilities[guessCell]))
      break;
  }
  if (guessCell == SIZE)
    return;

  short bit1 = 1;
  while (!(bit1 & possibilities[guessCell]))
    bit1 <<= 1;

  short bit2 = possibilities[guessCell] & (possibilities[guessCell] - 1);

  possibilities[guessCell] = bit1;
  runAlgoFromGuess();

  if (missing > 0)
  {
    missing = missingClone;
    for (int i = 0; i < SIZE; ++i)
    {
      possibilities[i] = possibClone[i];
      puzzle[i] = puzzleClone[i];
    }
    possibilities[guessCell] = bit2;
    runAlgoFromGuess();
  }
}

void runAlgoFromGuess()
{
  char progress = 1;
  while (progress == 1 && missing)
  {
    progress = 0;
    for (int i = 0; i < SIZE; ++i)
    {
      if (!possibilities[i] && !puzzle[i])
      {
        progress = -1;
        break;
      }
      else if (oneBits[possibilities[i]])
      {
        setNumber(i);
        progress = 1;
      }
    }
  }

  if (missing && !progress)
    guess();
}

void runAlgo()
{

  char progress = 1;
  while (progress == 1 && missing)
  {
    progress = 0;
    for (int i = 0; i < SIZE; ++i)
    {
      if (oneBits[possibilities[i]])
      {
        setNumber(i);
        progress = 1;
      }
    }
  }

  if (missing && !progress)
    guess();
}

void solve()
{
#ifdef DEBUG
  clock_t start = clock();
#endif

  for (int i = 0; i < SIZE; ++i)
    possibilities[i] = puzzle[i] == '0' ? ALL_BITS : numToBit(puzzle[i] - '0');

  missing = SIZE;

  bool progress = true;
  runAlgo();

#ifdef DEBUG
  timeSolve += ((double)(clock() - start) / CLOCKS_PER_SEC);
#endif
}

bool checkSolution()
{
  for (int i = 0; i < SIZE; ++i)
    if (puzzle[i] != solution[i])
      return false;

  return true;
}

int main()
{
  clock_t start = clock();

  setup();

  FILE *fp;
  fp = fopen("sudoku.csv", "r");

  char header[50];
  fscanf(fp, "%[^\n]", header);

  int failed = 0;

  for (int i = 0; i < 1000000; ++i)
  {
#ifdef DEBUG
    clock_t sInput = clock();
#endif

    fscanf(fp, "\n%[^,],%s", puzzle, solution);

#ifdef DEBUG
    timeReadInput += ((double)(clock() - sInput) / CLOCKS_PER_SEC);
#endif

    solve();

    if (!checkSolution())
      failed++;
  }

  clock_t end = clock();
  cout << "failed: " << failed << endl;
  cout << "took: " << ((double)(end - start) / CLOCKS_PER_SEC) << "s" << endl;

#ifdef DEBUG
  cout << "time read input: " << timeReadInput << endl;
  cout << "time solve: " << timeSolve << endl;
  cout << "time update number: " << timeSetNumber << endl;
#endif

  fclose(fp);

  return 0;
}