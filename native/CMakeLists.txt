cmake_minimum_required(VERSION 3.18)
project(balbal_native LANGUAGES C CXX)
set(CMAKE_CXX_STANDARD 17)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)
set(KENLM_ROOT "" CACHE PATH "KenLM source directory")
set(SENTENCEPIECE_ROOT "" CACHE PATH "SentencePiece source directory")
set(SPM_ENABLE_SHARED OFF CACHE BOOL "" FORCE)
set(SPM_ENABLE_TCMALLOC OFF CACHE BOOL "" FORCE)
set(SPM_BUILD_TEST OFF CACHE BOOL "" FORCE)
set(SPM_ENABLE_TRAINING OFF CACHE BOOL "" FORCE)
add_subdirectory(${SENTENCEPIECE_ROOT} sentencepiece EXCLUDE_FROM_ALL)
add_library(balbal_native SHARED bridge.cpp
 "${KENLM_ROOT}/util/bit_packing.cc"
 "${KENLM_ROOT}/util/ersatz_progress.cc"
 "${KENLM_ROOT}/util/exception.cc"
 "${KENLM_ROOT}/util/file.cc"
 "${KENLM_ROOT}/util/file_piece.cc"
 "${KENLM_ROOT}/util/float_to_string.cc"
 "${KENLM_ROOT}/util/integer_to_string.cc"
 "${KENLM_ROOT}/util/mmap.cc"
 "${KENLM_ROOT}/util/murmur_hash.cc"
 "${KENLM_ROOT}/util/parallel_read.cc"
 "${KENLM_ROOT}/util/pool.cc"
 "${KENLM_ROOT}/util/read_compressed.cc"
 "${KENLM_ROOT}/util/scoped.cc"
 "${KENLM_ROOT}/util/spaces.cc"
 "${KENLM_ROOT}/util/string_piece.cc"
 "${KENLM_ROOT}/util/usage.cc"
 "${KENLM_ROOT}/util/double-conversion/bignum-dtoa.cc"
 "${KENLM_ROOT}/util/double-conversion/bignum.cc"
 "${KENLM_ROOT}/util/double-conversion/cached-powers.cc"
 "${KENLM_ROOT}/util/double-conversion/double-to-string.cc"
 "${KENLM_ROOT}/util/double-conversion/fast-dtoa.cc"
 "${KENLM_ROOT}/util/double-conversion/fixed-dtoa.cc"
 "${KENLM_ROOT}/util/double-conversion/string-to-double.cc"
 "${KENLM_ROOT}/util/double-conversion/strtod.cc"
 "${KENLM_ROOT}/lm/bhiksha.cc"
 "${KENLM_ROOT}/lm/binary_format.cc"
 "${KENLM_ROOT}/lm/config.cc"
 "${KENLM_ROOT}/lm/lm_exception.cc"
 "${KENLM_ROOT}/lm/model.cc"
 "${KENLM_ROOT}/lm/quantize.cc"
 "${KENLM_ROOT}/lm/read_arpa.cc"
 "${KENLM_ROOT}/lm/search_hashed.cc"
 "${KENLM_ROOT}/lm/search_trie.cc"
 "${KENLM_ROOT}/lm/sizes.cc"
 "${KENLM_ROOT}/lm/trie.cc"
 "${KENLM_ROOT}/lm/trie_sort.cc"
 "${KENLM_ROOT}/lm/value_build.cc"
 "${KENLM_ROOT}/lm/virtual_interface.cc"
 "${KENLM_ROOT}/lm/vocab.cc"
)
target_include_directories(balbal_native PRIVATE include "${KENLM_ROOT}" "${SENTENCEPIECE_ROOT}/src")
target_compile_definitions(balbal_native PRIVATE KENLM_MAX_ORDER=6 NDEBUG)
target_link_libraries(balbal_native PRIVATE sentencepiece-static)
if(UNIX)
 target_link_libraries(balbal_native PRIVATE pthread)
endif()
install(TARGETS balbal_native LIBRARY DESTINATION . RUNTIME DESTINATION .)
