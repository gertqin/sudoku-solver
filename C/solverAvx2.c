#include "stdio.h"
#include "immintrin.h"

int main()
{
  unsigned long long nums[8];
  for (int i = 0; i < 8; i++)
    nums[i] = i + 1;

  __m256i v = _mm256_loadu_si256((const __m256i_u *)nums);

  printf("%lld %lld %lld %lld", v[0], v[1], v[2], v[3]);
  return 0;
}