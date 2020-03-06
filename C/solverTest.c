
#include "immintrin.h"
#include "stdint.h"
#include "stdio.h"
#include "time.h"

#define TEST

#define SUDOKU_COUNT 1000000

#define ROW_OFFSET 81
#define BOX_OFFSET 90
#define COL_OFFSET 99
#define DATA_LENGTH 108

static double time_ms(clock_t start, clock_t end) { return ((double)(end - start) * 1000 / CLOCKS_PER_SEC); }

static void vecTest() {
  unsigned long long nums[8];
  for (int i = 0; i < 8; i++)
    nums[i] = i + 1;

  __m256i v = _mm256_loadu_si256((const __m256i_u *)nums);
  printf("%lld %lld %lld %lld", v[0], v[1], v[2], v[3]);
}

static void loop1(uint32_t *data) {
  uint32_t *p_puzzle = data, *p_row = data + ROW_OFFSET, *p_box = data + BOX_OFFSET, *p_col = data + COL_OFFSET;

  int r, b, c, p, maxB, maxCol;
  static int r2b[9] = {0, 0, 0, 3, 3, 3, 6, 6, 6};

  for (int i = 0; i < 10000000; i++) {
    for (r = 0, p = 0; r < 9; r++) {
      b = r2b[r];
      c = 0;
      maxB = b + 3;
      for (; b < maxB; b++) {

        maxCol = c + 3;
        for (; c < maxCol; c++, p++) {
          p_puzzle[p] = p_row[r] + p_box[b] + p_col[c];
        }
      }
    }
  }
}

static void loop2(uint32_t *data) {
  uint32_t *p_puzzle = data, *p_row = data + ROW_OFFSET, *p_box = data + BOX_OFFSET, *p_col = data + COL_OFFSET;

  int r, b, c, p, maxB, maxCol;

  for (int i = 0; i < 10000000; i++) {
    for (p = 0; p < 81; p++) {
      r = p / 9;
      c = p % 9;
      b = r / 3 * 3 + c / 3 * 3;

      p_puzzle[p] = p_row[r] + p_box[b] + p_col[c];
    }
  }
}

static void loop3(uint32_t *data) {
  uint32_t *p_puzzle = data, *p_row = data + ROW_OFFSET, *p_box = data + BOX_OFFSET, *p_col = data + COL_OFFSET;

  int r, b, c, p, maxB, maxCol;
  static int p2r[81], p2b[81], p2c[81];

  for (p = 0; p < 81; p++) {
    p2r[p] = p / 9;
    p2c[p] = p % 9;
    p2b[p] = p2r[p] / 3 * 3 + p2c[p] / 3 * 3;
  }

  for (int i = 0; i < 10000000; i++) {
    for (p = 0; p < 81; p++) {
      r = p2r[p];
      c = p2c[p];
      b = p2b[p];

      p_puzzle[p] = p_row[r] + p_box[b] + p_col[c];
    }
  }
}

static void loop4(uint32_t *data) {
  uint32_t *p_puzzle = data, *p_row = data + ROW_OFFSET, *p_box = data + BOX_OFFSET, *p_col = data + COL_OFFSET;

  int r, b, c, p, z, maxB, maxCol;
  static int x[81];

  for (p = 0; p < 81; p++) {
    r = p / 9;
    c = p % 9;
    b = r / 3 * 3 + c / 3 * 3;
    x[p] = r | (b << 8) | (c << 16);
  }

  for (int i = 0; i < 10000000; i++) {
    for (p = 0; p < 81; p++) {
      z = x[p];
      r = z & 0x0F;
      b = (z >> 8) & 0x0F;
      c = (z >> 16);

      p_puzzle[p] = p_row[r] + p_box[b] + p_col[c];
    }
  }
}

typedef struct {
  uint32_t *p_r;
  uint32_t *p_b;
  uint32_t *p_c;
} rbc_t;

static void loop5(uint32_t *data) {
  uint32_t *p_puzzle = data, *p_row = data + ROW_OFFSET, *p_box = data + BOX_OFFSET, *p_col = data + COL_OFFSET;

  int r, b, c, p, z, maxB, maxCol;
  static rbc_t x[81];

  for (p = 0; p < 81; p++) {
    r = p / 9;
    c = p % 9;
    b = r / 3 * 3 + c / 3 * 3;
    x[p] = (rbc_t){&p_row[r], &p_box[b], &p_col[c]};
  }

  for (int i = 0; i < 10000000; i++) {
    for (p = 0; p < 81; p++) {
      rbc_t y = x[p];

      p_puzzle[p] = *y.p_r + *y.p_b + *y.p_c;
    }
  }
}

typedef struct {
  int r, b, c;
} rbc2_t;

static void loop6(uint32_t *data) {
  uint32_t *p_puzzle = data, *p_row = data + ROW_OFFSET, *p_box = data + BOX_OFFSET, *p_col = data + COL_OFFSET;

  int r, b, c, p, z, maxB, maxCol;
  int len = 81 * 3;
  static int x[81 * 3];

  for (p = 0; p < 81; p++) {
    r = p / 9;
    c = p % 9;
    b = r / 3 * 3 + c / 3 * 3;
    x[p * 3] = r;
    x[p * 3 + 1] = b;
    x[p * 3 + 2] = c;
  }

  for (int i = 0; i < 10000000; i++) {
    for (p = 0; p < len; p += 3) {

      p_puzzle[p] = p_row[x[p]] + p_box[x[p + 1]] + p_col[x[p + 2]];
    }
  }
}

static void loopPerformanceTests() {
  uint32_t data[DATA_LENGTH];

  for (int i = 0; i < DATA_LENGTH; i++)
    data[i] = 0;

  clock_t start = clock();
  loop1(data);
  clock_t end = clock();
  printf("Loop1 took: %fms\n", time_ms(start, end));

  // start = clock();
  // loop2(data);
  // end = clock();
  // printf("Loop2 took: %fms\n", time_ms(start, end));

  start = clock();
  loop3(data);
  end = clock();
  printf("Loop3 took: %fms\n", time_ms(start, end));

  start = clock();
  loop4(data);
  end = clock();
  printf("Loop4 took: %fms\n", time_ms(start, end));

  start = clock();
  loop5(data);
  end = clock();
  printf("Loop5 took: %fms\n", time_ms(start, end));

  start = clock();
  loop6(data);
  end = clock();
  printf("Loop6 took: %fms\n", time_ms(start, end));
}

int main() {
  loopPerformanceTests();
  // todo
}
