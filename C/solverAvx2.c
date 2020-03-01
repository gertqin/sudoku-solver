#include "immintrin.h"
#include "stdint.h"
#include "stdio.h"
#include "time.h"

#define TEST

#define SUDOKU_COUNT 1000000

#define SUDOKU_CELL_COUNT 81
#define BYTES_FOR_1_SUDOKU 164
#define BYTES_FOR_2_SUDOKUS 328
#define BYTES_FOR_3_SUDOKUS 492
#define BYTES_FOR_4_SUDOKUS 656
#define BYTES_FOR_5_SUDOKUS 820
#define BYTES_FOR_6_SUDOKUS 984
#define BYTES_FOR_7_SUDOKUS 1148
#define BYTES_FOR_8_SUDOKUS 1312

void vecTest() {
  unsigned long long nums[8];
  for (int i = 0; i < 8; i++)
    nums[i] = i + 1;

  __m256i v = _mm256_loadu_si256((const __m256i_u *)nums);
  printf("%lld %lld %lld %lld", v[0], v[1], v[2], v[3]);
}

double time_ms(clock_t start, clock_t end) {
  return ((double)(end - start) * 1000 / CLOCKS_PER_SEC);
}

int main() {
  clock_t start = clock();

  uint8_t *bytes =
      (uint8_t *)malloc(SUDOKU_COUNT * BYTES_FOR_1_SUDOKU * sizeof(uint8_t));
  FILE *fp = fopen("../sudoku.csv", "r");

  fread(bytes, sizeof(uint8_t), SUDOKU_COUNT * BYTES_FOR_1_SUDOKU, fp);

  fclose(fp);

  clock_t end = clock();
  printf("Read input took: %fms\n", time_ms(start, end));

  for (int i = 0; i < BYTES_FOR_1_SUDOKU; i++)
    printf("%c", bytes[i]);

  return 0;
}