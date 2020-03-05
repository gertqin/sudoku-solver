#include "immintrin.h"
#include "stdint.h"
#include "stdio.h"
#include "time.h"

#define TEST
#define CHECK_SOLUTION

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

#define ROW_OFFSET 1296  // 81 << 4
#define BOX_OFFSET 1440  // + 9 << 4
#define COL_OFFSET 1584  // + 9 << 4
#define DATA_LENGTH 1782 // + 9 << 4

// #region function defines
static void run(const uint8_t *bytes);
static void solve16sudokus(const uint8_t *sudokus, uint16_t *data);
static void transformSudokus(const uint8_t *sudokus, uint16_t *data);
static void setupStep(uint16_t *data, int *r2b);
static double time_ms(const clock_t start, const clock_t end);
// #endregion

int main() {
  clock_t start = clock();

  uint8_t *bytes =
      (uint8_t *)malloc(SUDOKU_COUNT * BYTES_FOR_1_SUDOKU * sizeof(uint8_t));
  FILE *fp = fopen("../sudoku.csv", "r");

  fread(bytes, sizeof(uint8_t), SUDOKU_COUNT * BYTES_FOR_1_SUDOKU, fp);

  fclose(fp);

  clock_t end = clock();
  printf("Reading input took: %.0fms\n", time_ms(start, end));

  start = clock();
  run(bytes);
  end = clock();
  printf("Solving 1.000.000 sudokus took: %.0fms\n", time_ms(start, end));

  return 0;
}

static void run(const uint8_t *bytes) {
  static uint16_t data[DATA_LENGTH];

  for (int i = 0; i < SUDOKU_COUNT; i += 16) {
    solve16sudokus(&bytes[i * BYTES_FOR_1_SUDOKU], data);
  }
}

static void solve16sudokus(const uint8_t *sudokus, uint16_t *data) {
  transformSudokus(sudokus, data);

  static int r2b[9] = {0, 0, 0, 3, 3, 3, 6, 6, 6};

  setupStep(data, r2b);
}

static inline void transformSudokus(const uint8_t *sudokus, uint16_t *data) {
  int i, j, sOffset;

  for (i = 0; i < 16; i++) {
    sOffset = i * BYTES_FOR_1_SUDOKU;
    for (j = 0; j < SUDOKU_CELL_COUNT; j++) {
      data[(j << 4) + i] =
          (uint16_t)(0b100000000 >> ('9' - sudokus[sOffset + j]));
    }
  }
}

static inline void setupStep(uint16_t *data, int *r2b) {
  uint16_t *p_rows = &data[ROW_OFFSET], *p_boxs = &data[BOX_OFFSET],
           *p_cols = &data[COL_OFFSET];
  int i, rowMaxI = 9 << 4;

  __m256i_u maskVec = _mm256_set1_epi16((short)0b111111111);
  for (i = 0; i < rowMaxI; i += 16) {
    _mm256_storeu_si256((__m256i_u *)(p_rows + i), maskVec);
    _mm256_storeu_si256((__m256i_u *)(p_boxs + i), maskVec);
    _mm256_storeu_si256((__m256i_u *)(p_cols + i), maskVec);
  }

  int p = 0, r = 0, b, c, maxB, maxC;
  __m256i_u *p_r, *p_b, *p_c;
  for (; r < 9; r++) {
    p_r = (__m256i_u *)&p_rows[r << 4];
    __m256i_u rVec = _mm256_loadu_si256(p_r);

    b = r2b[r];
    c = 0;
    maxB = b + 3;
    for (; b < maxB; b++) {
      p_b = (__m256i_u *)&p_boxs[b << 4];
      __m256i_u bVec = _mm256_loadu_si256(p_b);

      maxC = c + 3;
      for (; c < maxC; c++, p++) {
        p_c = (__m256i_u *)&p_cols[c << 4];
        __m256i cVec = _mm256_loadu_si256(p_c);
        __m256i pVec = _mm256_loadu_si256((__m256i_u *)&data[p << 4]);

        rVec = _mm256_andnot_si256(pVec, rVec);
        bVec = _mm256_andnot_si256(pVec, bVec);
        cVec = _mm256_andnot_si256(pVec, cVec);
        _mm256_storeu_si256(p_c, cVec);
      }

      _mm256_storeu_si256(p_b, bVec);
    }
    _mm256_storeu_si256(p_r, rVec);
  }
}

static void solveFullIteration() {
  // todo
}

static double time_ms(const clock_t start, const clock_t end) {
  return ((double)(end - start) * 1000 / CLOCKS_PER_SEC);
}