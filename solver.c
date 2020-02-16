#include "stdio.h"
#include "time.h"

#define LOG_LEVEL 1

#define SIZE 81
#define ABS(a, b) ((a) > (b) ? (a) - (b) : (b) - (a))

const unsigned short ALL_BITS = (1 << 9) - 1;

unsigned char bit2num[ALL_BITS + 1];
unsigned short num2bit['9' + 1];

unsigned char updateCells[SIZE][20]; // row: 8, col: 8, square remain: 4
unsigned char puzzle[SIZE];
unsigned char solution[SIZE];
unsigned short possibilities[SIZE];
unsigned char missing = 0;

#ifdef LOG_LEVEL
double timeSetup = 0;
double timeReadInput = 0;
double timeSolve = 0;
double timeCheckSolution = 0;
double timeSetNumber = 0;
double timeGuess = 0;
#endif

inline unsigned short removeBit(unsigned short val)
{
  return val & (val - 1);
}
inline unsigned char isTwoBits(unsigned short val)
{
  return val && bit2num[removeBit(val)];
}

void setup()
{
#ifdef LOG_LEVEL
  clock_t start = clock();
#endif

  bit2num[0] = 0;
  for (unsigned short i = 1; i <= ALL_BITS; ++i)
  {
    if (removeBit(i))
    {
      bit2num[i] = 0;
    }
    else
    { // single bit number
      unsigned short bit = i;
      unsigned char num = 1;
      while (bit >>= 1)
      {
        num++;
      }
      bit2num[i] = num + '0';
    }
  }
  for (unsigned char i = 0; i < '0'; i++)
    num2bit[i] = 0;
  num2bit['0'] = ALL_BITS; // not correct but results in faster setup before solve
  for (unsigned char i = '1'; i <= '9'; i++)
    num2bit[i] = 1 << (i - '1');

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

#ifdef LOG_LEVEL
  timeSetup += ((double)(clock() - start) / CLOCKS_PER_SEC);
#endif
}

void runAlgo();

void guess()
{
  for (unsigned char i = 0; i < SIZE; ++i)
    if (!possibilities[i] && puzzle[i] == '0')
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
  while (progress && missing)
  {
    progress = 0;
    for (unsigned char i = 0; i < SIZE; ++i)
    {
      unsigned short bit = possibilities[i];
      if (bit2num[bit])
      {
#ifdef LOG_LEVEL
#if LOG_LEVEL > 1
        clock_t start = clock();
#endif
#endif

        puzzle[i] = bit2num[bit];
        unsigned short invBit = ~bit;
        unsigned char *cells2update = updateCells[i];

        for (unsigned char j = 0; j < 20; ++j)
          possibilities[cells2update[j]] &= invBit;

        possibilities[i] = 0;
        missing--;

#ifdef LOG_LEVEL
#if LOG_LEVEL > 1
        timeSetNumber += ((double)(clock() - start) / CLOCKS_PER_SEC);
#endif
#endif
        progress = 1;
      }
    }
  }

  if (missing)
  {
#ifdef LOG_LEVEL
#if LOG_LEVEL > 1
    clock_t start = clock();
#endif
#endif

    guess();

#ifdef LOG_LEVEL
#if LOG_LEVEL > 1
    timeGuess += ((double)(clock() - start) / CLOCKS_PER_SEC);
#endif
#endif
  }
}

void solve()
{
#ifdef LOG_LEVEL
  clock_t start = clock();
#endif

  for (unsigned char i = 0; i < SIZE; ++i)
    possibilities[i] = num2bit[puzzle[i]];

  missing = SIZE;

  runAlgo();

#ifdef LOG_LEVEL
  timeSolve += ((double)(clock() - start) / CLOCKS_PER_SEC);
#endif
}

int main()
{
  clock_t start = clock();

  setup();

  FILE *fp = fopen("sudoku.csv", "r");

  unsigned char header[50];
  fscanf(fp, "%[^\n]", header);

  for (unsigned int i = 0; i < 1000000; ++i)
  {
#ifdef LOG_LEVEL
    clock_t startReadInput = clock();
#endif

    fscanf(fp, "\n%[^,],%s", puzzle, solution);

#ifdef LOG_LEVEL
    timeReadInput += ((double)(clock() - startReadInput) / CLOCKS_PER_SEC);
#endif

    solve();

#ifdef LOG_LEVEL
    clock_t startCheckSolution = clock();
#endif
    for (unsigned char i = 0; i < SIZE; ++i)
      if (__builtin_expect(puzzle[i] != solution[i], 0))
        printf("FAAAIIIL");

#ifdef LOG_LEVEL
    timeCheckSolution += ((double)(clock() - startCheckSolution) / CLOCKS_PER_SEC);
#endif
  }

  clock_t end = clock();

  printf("took: %.3fs\n", ((double)(end - start) / CLOCKS_PER_SEC));

#ifdef LOG_LEVEL
  printf("time setup: %.3fs\n", timeSetup);
  printf("time read input: %.3fs\n", timeReadInput);
  printf("time solve: %.3fs\n", timeSolve);
  printf("time check solution: %.3fs\n", timeCheckSolution);

#if LOG_LEVEL > 1
  printf("time update cells: %.3fs\n", timeSetNumber);
  printf("time guess: %.3fs\n", timeGuess);
#endif
#endif

  fclose(fp);

  return 0;
}