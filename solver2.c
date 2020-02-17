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

unsigned char puzzle[SIZE + 1];
unsigned char solution[SIZE + 1];

unsigned short rowRemain[9];
unsigned short colRemain[9];
unsigned short squareRemain[9];

unsigned char missing = 0;

#ifdef LOG_LEVEL
double timeSetup = 0;
double timeReadInput = 0;
double timeSolve = 0;
double timeCheckSolution = 0;
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
  puzzle[cell] = bit2num[bit];

  unsigned short invBit = ~bit;
  rowRemain[cell2row[cell]] &= invBit;
  colRemain[cell2col[cell]] &= invBit;
  squareRemain[cell2square[cell]] &= invBit;

  missing--;
}

void runAlgo();
void guess()
{
  unsigned char guessCell = SIZE;
  unsigned short guessBit = 0;
  for (unsigned char i = 0; i < SIZE; ++i)
  {
    if (puzzle[i] == '0')
    {
      unsigned short bit = rowRemain[cell2row[i]] & colRemain[cell2col[i]] & squareRemain[cell2square[i]];
      if (!bit)
        return;
      else if (isTwoBits(bit) && !guessBit)
      {
        guessCell = i;
        guessBit = bit;
      }
    }
  }
  if (guessCell == SIZE)
    return;

  unsigned short rowCopy[9];
  unsigned short colCopy[9];
  unsigned short squareCopy[9];
  unsigned char puzzleCopy[SIZE];
  unsigned char missingCopy = missing;

  for (unsigned char i = 0; i < 9; ++i)
  {
    rowCopy[i] = rowRemain[i];
    colCopy[i] = colRemain[i];
    squareCopy[i] = squareRemain[i];
  }
  for (unsigned char i = 0; i < SIZE; ++i)
    puzzleCopy[i] = puzzle[i];

  unsigned short bit1 = 1;
  while (!(bit1 & guessBit))
    bit1 <<= 1;

  unsigned short bit2 = removeBit(guessBit);

  setNumber(guessCell, bit1);
  runAlgo();

  if (missing > 0)
  {
    missing = missingCopy;
    for (unsigned char i = 0; i < 9; ++i)
    {
      rowRemain[i] = rowCopy[i];
      colRemain[i] = colCopy[i];
      squareRemain[i] = squareCopy[i];
    }
    for (unsigned char i = 0; i < SIZE; ++i)
      puzzle[i] = puzzleCopy[i];

    setNumber(guessCell, bit2);
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
    guess();
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

  missing = SIZE;

  for (unsigned char i = 0; i < SIZE; ++i)
    if (puzzle[i] != '0')
      setNumber(i, num2bit[puzzle[i]]);

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
  // "quizzes,solutions\n" = 18
  fread(header, sizeof(char), 18, fp);

  for (unsigned int i = 0; i < 1000000; ++i)
  {
#ifdef LOG_LEVEL
    clock_t startReadInput = clock();
#endif

    fread(puzzle, sizeof(char), 82, fp);
    fread(solution, sizeof(char), 82, fp);

#ifdef LOG_LEVEL
    timeReadInput += ((double)(clock() - startReadInput) / CLOCKS_PER_SEC);
#endif

    solve();

#ifdef LOG_LEVEL
    clock_t startCheckSolution = clock();
#endif
    for (unsigned char j = 0; j < SIZE; ++j)
      if (__builtin_expect(puzzle[j] != solution[j], 0))
      {
        printf("%d: FAIL\n", i);
        return -1;
      }

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
#endif

  fclose(fp);

  return 0;
}