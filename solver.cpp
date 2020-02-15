#include <iostream>
#include <fstream>
#include "stdio.h"
#include "time.h"

using namespace std;

#define DEBUG 1

#define SIZE 81
#define ABS(a, b) ((a) > (b) ? (a) - (b) : (b) - (a))

const unsigned short ALL_BITS = (1 << 9) - 1;

unsigned char bitToNum[ALL_BITS + 1];

unsigned char updateCells[SIZE][20]; // row: 8, col: 8, square remain: 4
unsigned char puzzle[SIZE];
unsigned char solution[SIZE];
unsigned short possibilities[SIZE];
unsigned char missing = 0;

#ifdef DEBUG
double timeSetup = 0;
double timeReadInput = 0;
double timeSetNumber = 0;
double timeSolve = 0;
double timeGuess = 0;
#endif

inline unsigned short removeBit(unsigned short val)
{
  return val & (val - 1);
}
inline unsigned char isTwoBits(unsigned short val)
{
  return val && bitToNum[removeBit(val)];
}
inline unsigned short numToBit(unsigned char num)
{
  return 1 << (num - 1);
}

void setup()
{
#ifdef DEBUG
  clock_t start = clock();
#endif

  bitToNum[0] = 0;
  for (unsigned short i = 1; i <= ALL_BITS; ++i)
  {
    if (removeBit(i))
    {
      bitToNum[i] = 0;
    }
    else
    { // single bit number
      unsigned short bit = i;
      unsigned char num = 1;
      while (bit >>= 1)
      {
        num++;
      }
      bitToNum[i] = num + '0';
    }
  }

  unsigned char rows[9][9];
  unsigned char cols[9][9];
  unsigned char squares[9][9];

  for (unsigned char i = 0; i < 9; ++i)
    for (unsigned char j = 0; j < 9; ++j)
    {
      rows[i][j] = i * 9 + j;
      cols[i][j] = j * 9 + i;
    }

  for (unsigned char i = 0; i < 9; ++i)
  {
    unsigned char row = i / 3 * 3;
    unsigned char col = i % 3 * 3;

    for (unsigned char j = 0; j < 3; ++j)
      for (unsigned char k = 0; k < 3; ++k)
        squares[i][j * 3 + k] = row * 9 + col + j * 9 + k;
  }

  for (unsigned char i = 0; i < SIZE; ++i)
  {
    unsigned char row = i / 9;
    unsigned char col = i % 9;
    unsigned char square = row / 3 * 3 + col / 3;

    unsigned char idx = 0;
    for (unsigned char j = 0; j < 9; ++j)
      if (rows[row][j] != i)
        updateCells[i][idx++] = rows[row][j];

    for (unsigned char j = 0; j < 9; ++j)
      if (cols[col][j] != i)
        updateCells[i][idx++] = cols[col][j];

    for (unsigned char j = 0; j < 9; ++j)
    {
      unsigned char squareVal = squares[square][j];
      if (squareVal % 9 != i % 9 && ABS(squareVal, i) > 2)
        updateCells[i][idx++] = squareVal;
    }
  }

#ifdef DEBUG
  timeSetup += ((double)(clock() - start) / CLOCKS_PER_SEC);
#endif
}

inline unsigned char setNumber(unsigned char num)
{
}

void runAlgoFromGuess();
void runAlgo();

void guess()
{
  for (unsigned char i = 0; i < SIZE; ++i)
    if (!possibilities[i] && !puzzle[i])
      return;

  unsigned short possibClone[SIZE];
  unsigned char puzzleClone[SIZE];
  unsigned char missingClone = missing;

  for (unsigned char i = 0; i < SIZE; ++i)
  {
    possibClone[i] = possibilities[i];
    puzzleClone[i] = puzzle[i];
  }

  unsigned char guessCell = 0;
  for (; guessCell < SIZE; guessCell++)
  {
    if (isTwoBits(possibilities[guessCell]))
      break;
  }
  if (guessCell == SIZE)
    return;

  unsigned short bit1 = 1;
  while (!(bit1 & possibilities[guessCell]))
    bit1 <<= 1;

  unsigned short bit2 = possibilities[guessCell] & (possibilities[guessCell] - 1);

  possibilities[guessCell] = bit1;
  runAlgo();

  if (missing > 0)
  {
    missing = missingClone;
    for (unsigned char i = 0; i < SIZE; ++i)
    {
      possibilities[i] = possibClone[i];
      puzzle[i] = puzzleClone[i];
    }
    possibilities[guessCell] = bit2;
    runAlgo();
  }
}

void runAlgo()
{
  unsigned char progress = 1;
  while (progress == 1 && missing)
  {
    progress = 0;
    for (unsigned char i = 0; i < SIZE; ++i)
    {
      if (bitToNum[possibilities[i]])
      {
#ifdef DEBUG
        clock_t start = clock();
#endif

        puzzle[i] = bitToNum[possibilities[i]];

        for (unsigned char j = 0; j < 20; ++j)
        {
          possibilities[updateCells[i][j]] &= ~possibilities[i];
        }

        possibilities[i] = 0;
        missing--;

#ifdef DEBUG
        timeSetNumber += ((double)(clock() - start) / CLOCKS_PER_SEC);
#endif
        progress = 1;
      }
    }
  }

  if (missing && !progress)
  {
#ifdef DEBUG
    clock_t start = clock();
#endif

    guess();

#ifdef DEBUG
    timeGuess += ((double)(clock() - start) / CLOCKS_PER_SEC);
#endif
  }
}

void solve()
{
#ifdef DEBUG
  clock_t start = clock();
#endif

  for (unsigned char i = 0; i < SIZE; ++i)
    possibilities[i] = puzzle[i] == '0' ? ALL_BITS : numToBit(puzzle[i] - '0');

  missing = SIZE;

  runAlgo();

#ifdef DEBUG
  timeSolve += ((double)(clock() - start) / CLOCKS_PER_SEC);
#endif
}

unsigned char checkSolution()
{
  for (unsigned char i = 0; i < SIZE; ++i)
    if (puzzle[i] != solution[i])
      return 0;

  return 1;
}

int main()
{
  clock_t start = clock();

  setup();

  FILE *fp = fopen("sudoku.csv", "r");

  unsigned char header[50];
  fscanf(fp, "%[^\n]", header);

  unsigned int failed = 0;

  for (unsigned int i = 0; i < 1000000; ++i)
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
  cout << "time setup: " << timeSetup << endl;
  cout << "time read input: " << timeReadInput << endl;
  cout << "time solve: " << timeSolve << endl;
  cout << "time update number: " << timeSetNumber << endl;
  cout << "time guess: " << timeGuess << endl;
#endif

  fclose(fp);

  return 0;
}