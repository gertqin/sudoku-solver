#include "immintrin.h"
#include "stdint.h"
#include "stdio.h"
#include "time.h"

#define TEST
#define CHECK_SOLUTIONS

#define SUDOKU_COUNT 1000000

#define SUDOKU_CELL_COUNT 81
#define BYTES_FOR_1_SUDOKUS 164
#define BYTES_FOR_2_SUDOKUS 328
#define BYTES_FOR_3_SUDOKUS 492
#define BYTES_FOR_4_SUDOKUS 656
#define BYTES_FOR_5_SUDOKUS 820
#define BYTES_FOR_6_SUDOKUS 984
#define BYTES_FOR_7_SUDOKUS 1148
#define BYTES_FOR_8_SUDOKUS 1312
#define BYTES_FOR_9_SUDOKUS 1476
#define BYTES_FOR_A_SUDOKUS 1640
#define BYTES_FOR_B_SUDOKUS 1804
#define BYTES_FOR_C_SUDOKUS 1968
#define BYTES_FOR_D_SUDOKUS 2132
#define BYTES_FOR_E_SUDOKUS 2296
#define BYTES_FOR_F_SUDOKUS 2460

#define ROW_OFFSET 1296  // 81 << 4
#define BOX_OFFSET 1440  // + 9 << 4
#define COL_OFFSET 1584  // + 9 << 4
#define DATA_LENGTH 1728 // + 9 << 4

#pragma region function declerations
static void run(const uint8_t *bytes);
static void solve16sudokus(const uint8_t *sudokus, uint16_t *data);

static void transform_sudokus(const uint8_t *sudokus, uint16_t *data);
static void transpose8x16(const uint8_t *p_src, uint16_t *p_dest);
static void convert2base2(__m256i_u *cellVec, __m256i_u *nineCharVec, __m256i_u *nineBitVec);

static void transform_sudokus_2(const uint8_t *sudokus, uint16_t *data);
static void transpose16x8(const uint8_t *p_src, uint16_t *p_dest);
static void convert2base2_2(__m256i_u *cellVec, __m256i_u *nineBitVec);

static void setup_step(uint16_t *data, int *r2b);
static void solve_parallel(uint16_t *data, int *r2b);
static void solve_cell(__m256i_u *pVec, __m256i_u *rVec, __m256i_u *bVec, __m256i_u *cVec, __m256i_u *zeroVec,
                       __m256i_u *oneVec);
static void check_solutions(uint16_t *data, uint16_t *solutions);

static void test_transform_sudokus(const uint8_t *sudokus, uint16_t *data);
static void test_setup_step(uint16_t *data);

static double time_ms(const clock_t start, const clock_t end);
#pragma endregion

int failedCount = 0;
uint64_t queueLengthTotal = 0;

int main() {
  clock_t start = clock();

  uint8_t *bytes = (uint8_t *)malloc(SUDOKU_COUNT * BYTES_FOR_1_SUDOKUS * sizeof(uint8_t));
  FILE *fp = fopen("../sudoku.csv", "r");

  fread(bytes, sizeof(uint8_t), SUDOKU_COUNT * BYTES_FOR_1_SUDOKUS, fp);

  fclose(fp);

  clock_t end = clock();
  printf("Reading input took: %.0fms\n", time_ms(start, end));

  start = clock();
  run(bytes);
  end = clock();
  printf("Solving 1.000.000 sudokus took: %.0fms\n", time_ms(start, end));
  printf("Failed: %d\n", failedCount);

  printf("Full iterations: %d\n", (1000000 >> 4) * 3 * SUDOKU_CELL_COUNT);
  printf("Queue iterations: %d\n", queueLengthTotal);

  return 0;
}

static void run(const uint8_t *bytes) {
  static uint16_t data[DATA_LENGTH];

  for (int i = 0; i < SUDOKU_COUNT; i += 16) {
    solve16sudokus(&bytes[i * BYTES_FOR_1_SUDOKUS], data);
  }
}

static void solve16sudokus(const uint8_t *sudokus, uint16_t *data) {
  transform_sudokus_2(sudokus, data);
#ifdef TEST
  test_transform_sudokus(sudokus, data);
#endif

  static int r2b[9] = {0, 0, 0, 3, 3, 3, 6, 6, 6};

  setup_step(data, r2b);
#ifdef TEST
  test_setup_step(data);
#endif

  solve_parallel(data, r2b);

#ifdef CHECK_SOLUTIONS
  static uint16_t solutions[SUDOKU_CELL_COUNT << 4];
  transform_sudokus(&sudokus[SUDOKU_CELL_COUNT + 1], solutions);
  check_solutions(data, solutions);
#endif
}

static inline void transform_sudokus(const uint8_t *sudokus, uint16_t *data) {
  int i, j;
  const uint8_t *p_src = sudokus;
  uint16_t *p_dest = data;

  // 5x16 = 80
  for (i = 0; i < 5; i++) {
    // solve 16x16
    transpose8x16(p_src, p_dest);
    transpose8x16(p_src + (BYTES_FOR_1_SUDOKUS << 3), p_dest + 8);

    p_src += 16;
    p_dest += (1 << 8);
  }

  for (i = 0; i < 16; i++) {
    *p_dest = (uint16_t)(0b100000000 >> ('9' - *p_src));
    p_src += BYTES_FOR_1_SUDOKUS;
    ++p_dest;
  }
}

// transpose 8 rows x 16 cols, with input in bytes and output in ushorts
static inline void transpose8x16(const uint8_t *p_src, uint16_t *p_dest) {
  __m256i_u v1, v2, v3, v4, v5, v6, v7, v8, lo12, lo34, lo56, lo78, hi12, hi34, hi56, hi78;

  v1 = _mm256_cvtepi8_epi16(_mm256_extracti128_si256(_mm256_loadu_si256((__m256i_u *)p_src), 0));
  v2 = _mm256_cvtepi8_epi16(_mm256_extracti128_si256(_mm256_loadu_si256((__m256i_u *)(p_src + BYTES_FOR_1_SUDOKUS)), 0));
  v3 = _mm256_cvtepi8_epi16(_mm256_extracti128_si256(_mm256_loadu_si256((__m256i_u *)(p_src + BYTES_FOR_2_SUDOKUS)), 0));
  v4 = _mm256_cvtepi8_epi16(_mm256_extracti128_si256(_mm256_loadu_si256((__m256i_u *)(p_src + BYTES_FOR_3_SUDOKUS)), 0));
  v5 = _mm256_cvtepi8_epi16(_mm256_extracti128_si256(_mm256_loadu_si256((__m256i_u *)(p_src + BYTES_FOR_4_SUDOKUS)), 0));
  v6 = _mm256_cvtepi8_epi16(_mm256_extracti128_si256(_mm256_loadu_si256((__m256i_u *)(p_src + BYTES_FOR_5_SUDOKUS)), 0));
  v7 = _mm256_cvtepi8_epi16(_mm256_extracti128_si256(_mm256_loadu_si256((__m256i_u *)(p_src + BYTES_FOR_6_SUDOKUS)), 0));
  v8 = _mm256_cvtepi8_epi16(_mm256_extracti128_si256(_mm256_loadu_si256((__m256i_u *)(p_src + BYTES_FOR_7_SUDOKUS)), 0));

  lo12 = _mm256_unpacklo_epi16(v1, v2);
  lo34 = _mm256_unpacklo_epi16(v3, v4);
  lo56 = _mm256_unpacklo_epi16(v5, v6);
  lo78 = _mm256_unpacklo_epi16(v7, v8);
  hi12 = _mm256_unpackhi_epi16(v1, v2);
  hi34 = _mm256_unpackhi_epi16(v3, v4);
  hi56 = _mm256_unpackhi_epi16(v5, v6);
  hi78 = _mm256_unpackhi_epi16(v7, v8);

  v1 = lo12;
  v2 = lo34;
  v3 = lo56;
  v4 = lo78;
  v5 = hi12;
  v6 = hi34;
  v7 = hi56;
  v8 = hi78;

  lo12 = _mm256_unpacklo_epi32(v1, v2);
  lo34 = _mm256_unpacklo_epi32(v3, v4);
  lo56 = _mm256_unpacklo_epi32(v5, v6);
  lo78 = _mm256_unpacklo_epi32(v7, v8);
  hi12 = _mm256_unpackhi_epi32(v1, v2);
  hi34 = _mm256_unpackhi_epi32(v3, v4);
  hi56 = _mm256_unpackhi_epi32(v5, v6);
  hi78 = _mm256_unpackhi_epi32(v7, v8);

  v1 = lo12;
  v2 = lo34;
  v3 = hi12;
  v4 = hi34;
  v5 = lo56;
  v6 = lo78;
  v7 = hi56;
  v8 = hi78;

  lo12 = _mm256_unpacklo_epi64(v1, v2);
  lo34 = _mm256_unpacklo_epi64(v3, v4);
  lo56 = _mm256_unpacklo_epi64(v5, v6);
  lo78 = _mm256_unpacklo_epi64(v7, v8);
  hi12 = _mm256_unpackhi_epi64(v1, v2);
  hi34 = _mm256_unpackhi_epi64(v3, v4);
  hi56 = _mm256_unpackhi_epi64(v5, v6);
  hi78 = _mm256_unpackhi_epi64(v7, v8);

  v1 = lo12;
  v2 = hi12;
  v3 = lo34;
  v4 = hi34;
  v5 = lo56;
  v6 = hi56;
  v7 = lo78;
  v8 = hi78;

  __m256i_u nineCharVec = _mm256_set1_epi16('9');
  __m256i_u oneVec = _mm256_set1_epi32(0b100000000);
  convert2base2(&v1, &nineCharVec, &oneVec);
  convert2base2(&v2, &nineCharVec, &oneVec);
  convert2base2(&v3, &nineCharVec, &oneVec);
  convert2base2(&v4, &nineCharVec, &oneVec);
  convert2base2(&v5, &nineCharVec, &oneVec);
  convert2base2(&v6, &nineCharVec, &oneVec);
  convert2base2(&v7, &nineCharVec, &oneVec);
  convert2base2(&v8, &nineCharVec, &oneVec);

  _mm_storeu_si128((__m128i_u *)p_dest, _mm256_extracti128_si256(v1, 0));
  _mm_storeu_si128((__m128i_u *)(p_dest + 0x10), _mm256_extracti128_si256(v2, 0));
  _mm_storeu_si128((__m128i_u *)(p_dest + 0x20), _mm256_extracti128_si256(v3, 0));
  _mm_storeu_si128((__m128i_u *)(p_dest + 0x30), _mm256_extracti128_si256(v4, 0));
  _mm_storeu_si128((__m128i_u *)(p_dest + 0x40), _mm256_extracti128_si256(v5, 0));
  _mm_storeu_si128((__m128i_u *)(p_dest + 0x50), _mm256_extracti128_si256(v6, 0));
  _mm_storeu_si128((__m128i_u *)(p_dest + 0x60), _mm256_extracti128_si256(v7, 0));
  _mm_storeu_si128((__m128i_u *)(p_dest + 0x70), _mm256_extracti128_si256(v8, 0));
  _mm_storeu_si128((__m128i_u *)(p_dest + 0x80), _mm256_extracti128_si256(v1, 1));
  _mm_storeu_si128((__m128i_u *)(p_dest + 0x90), _mm256_extracti128_si256(v2, 1));
  _mm_storeu_si128((__m128i_u *)(p_dest + 0xA0), _mm256_extracti128_si256(v3, 1));
  _mm_storeu_si128((__m128i_u *)(p_dest + 0xB0), _mm256_extracti128_si256(v4, 1));
  _mm_storeu_si128((__m128i_u *)(p_dest + 0xC0), _mm256_extracti128_si256(v5, 1));
  _mm_storeu_si128((__m128i_u *)(p_dest + 0xD0), _mm256_extracti128_si256(v6, 1));
  _mm_storeu_si128((__m128i_u *)(p_dest + 0xE0), _mm256_extracti128_si256(v7, 1));
  _mm_storeu_si128((__m128i_u *)(p_dest + 0xF0), _mm256_extracti128_si256(v8, 1));
}

static inline void convert2base2(__m256i_u *cellVec, __m256i_u *nineCharVec, __m256i_u *nineBitVec) {
  __m256i_u cellsInBase10 = _mm256_sub_epi16(*nineCharVec, *cellVec);
  __m256i_u lowCellsInBase2 =
      _mm256_srlv_epi32(*nineBitVec, _mm256_cvtepu16_epi32(_mm256_extracti128_si256(cellsInBase10, 0)));

  __m256i_u highCellsInBase2 =
      _mm256_srlv_epi32(*nineBitVec, _mm256_cvtepu16_epi32(_mm256_extracti128_si256(cellsInBase10, 1)));

  __m256i_u cellsInBase2 = _mm256_packus_epi32(lowCellsInBase2, highCellsInBase2);
  // shuffle packed bytes into order 00 10 01 11 = 1,3,2,4
  cellsInBase2 = _mm256_permute4x64_epi64(cellsInBase2, 0b11011000);
  *cellVec = cellsInBase2;
}

static inline void transform_sudokus_2(const uint8_t *sudokus, uint16_t *data) {
  int i, j;
  const uint8_t *p_src = sudokus;
  uint16_t *p_dest = data;

  // 5x16 = 80
  for (i = 0; i < 10; i++) {
    // solve 16x8
    transpose16x8(p_src, p_dest);

    p_src += 8;
    p_dest += (8 << 4);
  }

  for (i = 0; i < 16; i++) {
    *p_dest = (uint16_t)(0b100000000 >> ('9' - *p_src));
    p_src += BYTES_FOR_1_SUDOKUS;
    ++p_dest;
  }
}
static void transpose16x8(const uint8_t *p_src, uint16_t *p_dest) {
  __m256i_u v1, v2, v3, v4, v, lo12, lo34, hi12, hi34, nineCharVec, permuteMask, nineBitVec;

  nineCharVec = _mm256_set1_epi8('9');

  v1 = _mm256_set_epi64x(*((uint64_t *)p_src), *((uint64_t *)(p_src + BYTES_FOR_4_SUDOKUS)),
                         *((uint64_t *)(p_src + BYTES_FOR_8_SUDOKUS)), *((uint64_t *)(p_src + BYTES_FOR_C_SUDOKUS)));

  v2 = _mm256_set_epi64x(*((uint64_t *)(p_src + BYTES_FOR_1_SUDOKUS)), *((uint64_t *)(p_src + BYTES_FOR_5_SUDOKUS)),
                         *((uint64_t *)(p_src + BYTES_FOR_9_SUDOKUS)), *((uint64_t *)(p_src + BYTES_FOR_D_SUDOKUS)));

  v3 = _mm256_set_epi64x(*((uint64_t *)(p_src + BYTES_FOR_2_SUDOKUS)), *((uint64_t *)(p_src + BYTES_FOR_6_SUDOKUS)),
                         *((uint64_t *)(p_src + BYTES_FOR_A_SUDOKUS)), *((uint64_t *)(p_src + BYTES_FOR_E_SUDOKUS)));

  v4 = _mm256_set_epi64x(*((uint64_t *)(p_src + BYTES_FOR_3_SUDOKUS)), *((uint64_t *)(p_src + BYTES_FOR_5_SUDOKUS)),
                         *((uint64_t *)(p_src + BYTES_FOR_B_SUDOKUS)), *((uint64_t *)(p_src + BYTES_FOR_F_SUDOKUS)));

  v1 = _mm256_sub_epi8(nineCharVec, v1);
  v2 = _mm256_sub_epi8(nineCharVec, v2);
  v3 = _mm256_sub_epi8(nineCharVec, v3);
  v4 = _mm256_sub_epi8(nineCharVec, v4);

  lo12 = _mm256_unpacklo_epi8(v1, v2);
  lo34 = _mm256_unpacklo_epi8(v3, v4);
  hi12 = _mm256_unpackhi_epi8(v1, v2);
  hi34 = _mm256_unpackhi_epi8(v3, v4);

  v1 = lo12;
  v2 = lo34;
  v3 = hi12;
  v4 = hi34;

  lo12 = _mm256_unpacklo_epi16(v1, v2);
  lo34 = _mm256_unpacklo_epi16(v3, v4);
  hi12 = _mm256_unpackhi_epi16(v1, v2);
  hi34 = _mm256_unpackhi_epi16(v3, v4);

  v1 = lo12;
  v2 = hi12;
  v3 = lo34;
  v4 = hi34;

  permuteMask = _mm256_set_epi32(7, 3, 5, 1, 6, 2, 4, 0);
  v1 = _mm256_permutevar8x32_epi32(v1, permuteMask);
  v2 = _mm256_permutevar8x32_epi32(v2, permuteMask);
  v3 = _mm256_permutevar8x32_epi32(v3, permuteMask);
  v4 = _mm256_permutevar8x32_epi32(v4, permuteMask);

  nineBitVec = _mm256_set1_epi32(0b100000000);
  v = _mm256_cvtepi8_epi16(_mm256_extracti128_si256(v1, 0));
  convert2base2_2(&v, &nineBitVec);
  _mm256_storeu_si256((__m256i_u *)p_dest, v);

  v = _mm256_cvtepi8_epi16(_mm256_extracti128_si256(v1, 1));
  convert2base2_2(&v, &nineBitVec);
  _mm256_storeu_si256((__m256i_u *)(p_dest + 0x10), v);

  v = _mm256_cvtepi8_epi16(_mm256_extracti128_si256(v2, 0));
  convert2base2_2(&v, &nineBitVec);
  _mm256_storeu_si256((__m256i_u *)(p_dest + 0x20), v);

  v = _mm256_cvtepi8_epi16(_mm256_extracti128_si256(v2, 1));
  convert2base2_2(&v, &nineBitVec);
  _mm256_storeu_si256((__m256i_u *)(p_dest + 0x30), v);

  v = _mm256_cvtepi8_epi16(_mm256_extracti128_si256(v3, 0));
  convert2base2_2(&v, &nineBitVec);
  _mm256_storeu_si256((__m256i_u *)(p_dest + 0x40), v);

  v = _mm256_cvtepi8_epi16(_mm256_extracti128_si256(v3, 1));
  convert2base2_2(&v, &nineBitVec);
  _mm256_storeu_si256((__m256i_u *)(p_dest + 0x50), v);

  v = _mm256_cvtepi8_epi16(_mm256_extracti128_si256(v4, 0));
  convert2base2_2(&v, &nineBitVec);
  _mm256_storeu_si256((__m256i_u *)(p_dest + 0x60), v);

  v = _mm256_cvtepi8_epi16(_mm256_extracti128_si256(v4, 1));
  convert2base2_2(&v, &nineBitVec);
  _mm256_storeu_si256((__m256i_u *)(p_dest + 0x70), v);
}
static void convert2base2_2(__m256i_u *cellVec, __m256i_u *nineBitVec) {
  __m256i_u cellsInBase10 = *cellVec;
  __m256i_u lowCellsInBase2 =
      _mm256_srlv_epi32(*nineBitVec, _mm256_cvtepu16_epi32(_mm256_extracti128_si256(cellsInBase10, 0)));

  __m256i_u highCellsInBase2 =
      _mm256_srlv_epi32(*nineBitVec, _mm256_cvtepu16_epi32(_mm256_extracti128_si256(cellsInBase10, 1)));

  __m256i_u cellsInBase2 = _mm256_packus_epi32(lowCellsInBase2, highCellsInBase2);
  *cellVec = cellsInBase2;
}

static inline void setup_step(uint16_t *data, int *r2b) {
  uint16_t *p_rows = &data[ROW_OFFSET], *p_boxs = &data[BOX_OFFSET], *p_cols = &data[COL_OFFSET];
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

typedef struct {
  __m256i_u *p_r, *p_b, *p_c, *p_p;
} cell_t;

static inline void solve_parallel(uint16_t *data, int *r2b) {
  uint16_t *p_rows = &data[ROW_OFFSET], *p_boxs = &data[BOX_OFFSET], *p_cols = &data[COL_OFFSET];

  int qLen = SUDOKU_CELL_COUNT << 1, qEnd = 0;
  cell_t queue[qLen];

  int i, p, r, b, c, maxB, maxC;
  __m256i_u *p_r, *p_b, *p_c, *p_p;
  __m256i_u zeroVec = _mm256_setzero_si256();
  __m256i_u oneVec = _mm256_set1_epi16((short)1);

  i = 2;
  do {
    for (p = 0, r = 0; r < 9; r++) {
      p_r = (__m256i_u *)&p_rows[r << 4];
      __m256i_u rVec = _mm256_loadu_si256(p_r);

      for (b = r2b[r], maxB = b + 3, c = 0; b < maxB; b++) {
        p_b = (__m256i_u *)&p_boxs[b << 4];
        __m256i_u bVec = _mm256_loadu_si256(p_b);

        for (maxC = c + 3; c < maxC; c++, p++) {
          p_c = (__m256i_u *)&p_cols[c << 4];
          p_p = (__m256i_u *)&data[p << 4];
          __m256i_u cVec = _mm256_loadu_si256(p_c);
          __m256i_u pVec = _mm256_loadu_si256(p_p);

          solve_cell(&pVec, &rVec, &bVec, &cVec, &zeroVec, &oneVec);

          _mm256_storeu_si256(p_p, pVec);
          _mm256_storeu_si256(p_c, cVec);

          if (i == 0 && (uint32_t)_mm256_movemask_epi8(_mm256_cmpeq_epi16(pVec, zeroVec)) > 0) {
            cell_t cell = (cell_t){p_r, p_b, p_c, p_p};
            queue[qEnd++] = cell;
          }
        }

        _mm256_storeu_si256(p_b, bVec);
      }
      _mm256_storeu_si256(p_r, rVec);
    }
  } while (i-- != 0);

  int qIdx = 0;
  while (qIdx < qEnd && qEnd < qLen) {
    cell_t cell = queue[qIdx];

    __m256i_u rVec = _mm256_loadu_si256(cell.p_r);
    __m256i_u bVec = _mm256_loadu_si256(cell.p_b);
    __m256i_u cVec = _mm256_loadu_si256(cell.p_c);
    __m256i_u pVec = _mm256_loadu_si256(cell.p_p);

    solve_cell(&pVec, &rVec, &bVec, &cVec, &zeroVec, &oneVec);

    _mm256_storeu_si256(cell.p_p, pVec);
    _mm256_storeu_si256(cell.p_c, cVec);
    _mm256_storeu_si256(cell.p_b, bVec);
    _mm256_storeu_si256(cell.p_r, rVec);

    if ((uint32_t)_mm256_movemask_epi8(_mm256_cmpeq_epi16(pVec, zeroVec)) > 0) {
      queue[qEnd++] = cell;
    }
    ++qIdx;
  }

  queueLengthTotal += qIdx;
}

static inline void solve_cell(__m256i_u *pVec, __m256i_u *rVec, __m256i_u *bVec, __m256i_u *cVec, __m256i_u *zeroVec,
                              __m256i_u *oneVec) {
  __m256i_u bits = _mm256_and_si256(*rVec, *bVec);
  bits = _mm256_and_si256(bits, *cVec);
  bits = _mm256_or_si256(bits, *pVec);

  __m256i_u mask = _mm256_cmpeq_epi16(_mm256_and_si256(bits, _mm256_sub_epi16(bits, *oneVec)), *zeroVec);
  bits = _mm256_and_si256(mask, bits);

  *pVec = _mm256_or_si256(bits, *pVec);
  *rVec = _mm256_andnot_si256(bits, *rVec);
  *bVec = _mm256_andnot_si256(bits, *bVec);
  *cVec = _mm256_andnot_si256(bits, *cVec);
}

static inline void check_solutions(uint16_t *data, uint16_t *solutions) {
  int maxI = SUDOKU_CELL_COUNT << 4;
  for (int i = 0; i < maxI; i += 16) {
    __m256i_u pVec = _mm256_loadu_si256((__m256i_u *)&data[i]);
    __m256i_u sVec = _mm256_loadu_si256((__m256i_u *)&solutions[i]);
    __m256i_u mask = _mm256_cmpeq_epi16(pVec, sVec);

    if (_mm256_movemask_epi8(mask) != 0xFFFFFFFF) {
      ++failedCount;
      break;
    }
  }
}

#pragma region tests
static inline void test_transform_sudokus(const uint8_t *sudokus, uint16_t *data) {
  int i, j, sOffset;
  int testData[SUDOKU_CELL_COUNT << 4];

  for (i = 0; i < 16; i++) {
    sOffset = i * BYTES_FOR_1_SUDOKUS;
    for (j = 0; j < SUDOKU_CELL_COUNT; j++) {
      testData[(j << 4) + i] = (uint16_t)(0b100000000 >> ('9' - sudokus[sOffset + j]));
    }
  }

  for (i = 0; i < SUDOKU_CELL_COUNT << 4; i++) {
    if (data[i] != testData[i]) {
      printf("transform fail: %d <> %d (i=%d)", data[i], testData[i], i);
      exit(1);
    }
  }
}
static void test_setup_step(uint16_t *data) {
  int len = DATA_LENGTH - ROW_OFFSET;
  uint16_t dataArr[len];
  for (int i = 0; i < len; i++) {
    dataArr[i] = data[i + ROW_OFFSET];
  }

  uint16_t testData[len];
  for (int i = 0; i < len; i++) {
    testData[i] = 0b111111111;
  }

  uint16_t *p_rows = &testData[0], *p_boxs = &testData[9 << 4], *p_cols = &testData[(9 << 4) * 2];
  for (int i = 0; i < 16; i++) {
    for (int p = 0; p < 81; p++) {
      int r = p / 9;
      int c = p % 9;
      int b = r / 3 * 3 + c / 3;

      uint16_t mask = ~data[(p << 4) + i];
      p_rows[(r << 4) + i] &= mask;
      p_boxs[(b << 4) + i] &= mask;
      p_cols[(c << 4) + i] &= mask;
    }
  }

  for (int i = 0; i < len; i++) {
    if (dataArr[i] != testData[i]) {
      printf("SetupStep fail: %d <> %d", dataArr[i], testData[i]);
      exit(1);
    }
  }
}
#pragma endregion

static double time_ms(const clock_t start, const clock_t end) { return ((double)(end - start) * 1000 / CLOCKS_PER_SEC); }
