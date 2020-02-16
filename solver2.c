#include "stdio.h"
#include "time.h"

#define LOG_LEVEL 1

#define SIZE 81
#define ABS(a, b) ((a) > (b) ? (a) - (b) : (b) - (a))

const unsigned short ALL_BITS = (1 << 9) - 1;

unsigned char bit2num[ALL_BITS + 1];
unsigned short num2bit['9' + 1];

unsigned char cell2row[SIZE];
unsigned char cell2col[SIZE];
unsigned char cell2square[SIZE];

unsigned char puzzle[SIZE];
unsigned char solution[SIZE];
unsigned short rowRemain[9];
unsigned short colRemain[9];
unsigned short squareRemain[9];

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
  for (unsigned char i = 0; i <= '0'; i++)
    num2bit[i] = 0;
  for (unsigned char i = '1'; i <= '9'; i++)
    num2bit[i] = 1 << (i - '1');

  for (unsigned char i = 0; i < SIZE; i++)
  {
    unsigned char row = i / 9;
    unsigned char col = i % 9;
    cell2row[i] = row;
    cell2col[i] = col;
    cell2square[i] = (row / 3 * 3) + (col / 3);
  }

#ifdef LOG_LEVEL
  timeSetup += ((double)(clock() - start) / CLOCKS_PER_SEC);
#endif
}

inline void setNumber(unsigned char cell, unsigned short bit)
{
#ifdef LOG_LEVEL
#if LOG_LEVEL > 1
  clock_t start = clock();
#endif
#endif
  puzzle[cell] = bit2num[bit];

  unsigned short invBit = ~bit;
  rowRemain[cell2row[cell]] &= invBit;
  colRemain[cell2col[cell]] &= invBit;
  squareRemain[cell2square[cell]] &= invBit;

  missing--;

#ifdef LOG_LEVEL
#if LOG_LEVEL > 1
  timeSetNumber += ((double)(clock() - start) / CLOCKS_PER_SEC);
#endif
#endif
}

void runAlgo()
{
  unsigned char progress = 1;
  while (progress && missing)
  {
    progress = 0;
    for (unsigned char i = 0; i < SIZE; ++i)
    {
      if (puzzle[i] == '0')
      {
        unsigned short bit = rowRemain[cell2row[i]] & colRemain[cell2col[i]] & squareRemain[cell2square[i]];
        if (bit2num[bit])
        {
          setNumber(i, bit);
          progress = 1;
        }
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

    // guess();

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

  for (unsigned char i = 0; i < 9; i++)
  {
    rowRemain[i] = ALL_BITS;
    colRemain[i] = ALL_BITS;
    squareRemain[i] = ALL_BITS;
  }

  for (unsigned char i = 0; i < SIZE; ++i)
    if (puzzle[i] != '0')
      setNumber(i, num2bit[puzzle[i]]);

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

  int failed = 0;

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
        failed++; // printf("FAAAIIIL");

#ifdef LOG_LEVEL
    timeCheckSolution += ((double)(clock() - startCheckSolution) / CLOCKS_PER_SEC);
#endif
  }

  clock_t end = clock();

  printf("failed: %d\n", failed);
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